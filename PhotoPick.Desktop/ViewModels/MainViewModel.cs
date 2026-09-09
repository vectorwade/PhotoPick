using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using PhotoPick.Desktop.Helpers;
using PhotoPick.Desktop.Services;

namespace PhotoPick.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly CullingSession _session;
    private readonly IRawPreviewExtractor _extractor;
    private readonly LightroomService _lightroomService;
    private readonly ThumbnailLoaderQueue _loaderQueue;

    private bool _isLoadingFolder;
    private bool _isSavingXmp;
    private bool _isExporting;
    private bool _isSingleViewMode;
    private PhotoViewModel? _selectedPhoto;
    private ImageSource? _loupeImage;
    private string _statusMessage = "Pronto para abrir uma pasta de fotos ou arrastar arquivos aqui.";
    private string _filterName = "Todas";

    public CullingSession Session => _session;
    public ObservableCollection<PhotoViewModel> Photos { get; } = [];
    public ObservableCollection<PhotoRowViewModel> Rows { get; } = [];

    public bool IsLoadingFolder
    {
        get => _isLoadingFolder;
        set { _isLoadingFolder = value; OnPropertyChanged(); }
    }

    public bool IsSavingXmp
    {
        get => _isSavingXmp;
        set { _isSavingXmp = value; OnPropertyChanged(); }
    }

    public bool IsExporting
    {
        get => _isExporting;
        set { _isExporting = value; OnPropertyChanged(); }
    }

    public bool IsSingleViewMode
    {
        get => _isSingleViewMode;
        set
        {
            if (_isSingleViewMode != value)
            {
                _isSingleViewMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGridViewMode));
                if (_isSingleViewMode && SelectedPhoto != null)
                {
                    _ = LoadLoupeImageAsync(SelectedPhoto);
                }
            }
        }
    }

    public bool IsGridViewMode => !IsSingleViewMode;

    public PhotoViewModel? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            if (_selectedPhoto != value)
            {
                _selectedPhoto = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                if (IsSingleViewMode && _selectedPhoto != null)
                {
                    _ = LoadLoupeImageAsync(_selectedPhoto);
                }
            }
        }
    }

    public bool HasSelection => SelectedPhoto != null;

    public ImageSource? LoupeImage
    {
        get => _loupeImage;
        private set { _loupeImage = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string FilterName
    {
        get => _filterName;
        set { _filterName = value; OnPropertyChanged(); }
    }

    public string CurrentDirectory => _session.CurrentDirectory ?? "Nenhuma pasta selecionada";
    public int TotalCount => _session.TotalCount;
    public int PickedCount => _session.PickedCount;
    public int RejectedCount => _session.RejectedCount;
    public int UnflaggedCount => _session.UnflaggedCount;
    public int UnsavedCount => _session.UnsavedCount;

    public int Star5Count => _session.RatingCount(5);
    public int Star4Count => _session.RatingCount(4);
    public int Star3Count => _session.RatingCount(3);
    public int Star2Count => _session.RatingCount(2);
    public int Star1Count => _session.RatingCount(1);

    public MainViewModel()
    {
        _extractor = new RawPreviewExtractor();
        _session = new CullingSession(extractor: _extractor);
        _lightroomService = new LightroomService();
        _loaderQueue = new ThumbnailLoaderQueue(_session);
        _loaderQueue.Start();

        _session.StatsChanged += RefreshStats;
    }

    #region Abertura de Pasta e Arquivos

    public async Task OpenFolderDialogAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecionar Pasta de Fotos RAW/JPG (O Windows mostra apenas diretórios aqui)",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadDirectoryAsync(dialog.FolderName);
        }
    }

    public async Task OpenPhotosDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione qualquer foto para abrir a pasta correspondente",
            Filter = "Fotos RAW e JPEG (*.dng;*.cr2;*.cr3;*.arw;*.nef;*.raf;*.jpg)|*.dng;*.cr2;*.cr3;*.arw;*.nef;*.raf;*.orf;*.pef;*.rw2;*.jpg;*.jpeg|Todos os arquivos (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            string? dir = Path.GetDirectoryName(dialog.FileNames[0]);
            if (!string.IsNullOrEmpty(dir))
            {
                await LoadDirectoryAsync(dir);
            }
        }
    }

    public async Task LoadDirectoryAsync(string directoryPath)
    {
        IsLoadingFolder = true;
        StatusMessage = $"Indexando {Path.GetFileName(directoryPath)}...";
        _loaderQueue.Stop();

        try
        {
            int count = await _session.LoadDirectoryAsync(directoryPath);
            RebuildViewModels();
            StatusMessage = $"{count} fotos carregadas. Use os botões ou atalhos: 'P' para Pick, 'X' para Reject, '1-5' para Estrelas.";

            if (Photos.Count > 0)
            {
                SelectedPhoto = Photos[0];
            }

            // Inicia fila de carregamento de thumbnails em background controlado
            _loaderQueue.Start();
            _loaderQueue.EnqueueBatch(Photos);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao abrir pasta: {ex.Message}";
        }
        finally
        {
            IsLoadingFolder = false;
            RefreshStats();
        }
    }

    #endregion

    #region Filtros

    public void ApplyFilter(PhotoFilterMode mode)
    {
        _session.ApplyFilter(mode);
        FilterName = mode switch
        {
            PhotoFilterMode.PickedOnly => "Selecionadas (P)",
            PhotoFilterMode.RejectedOnly => "Rejeitadas (X)",
            PhotoFilterMode.UnflaggedOnly => "Não Avaliadas",
            PhotoFilterMode.RatedOnly => "Com Estrelas",
            PhotoFilterMode.Rating5 => "★ 5 Estrelas",
            PhotoFilterMode.Rating4 => "★ 4 Estrelas",
            PhotoFilterMode.Rating3 => "★ 3 Estrelas",
            PhotoFilterMode.Rating2 => "★ 2 Estrelas",
            PhotoFilterMode.Rating1 => "★ 1 Estrela",
            _ => "Todas"
        };
        RebuildViewModels();
    }

    #endregion

    #region Sincronização & Abertura do Lightroom Classic

    public async Task SyncAndOpenLightroomAsync()
    {
        if (TotalCount == 0)
        {
            MessageBox.Show("Abra uma pasta de fotos antes de sincronizar com o Lightroom.", "PhotoPick", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsSavingXmp = true;
        StatusMessage = "Gravando arquivos sidecar .xmp compatíveis com o Lightroom...";

        try
        {
            int saved = await _session.SaveAllXmpAsync();
            StatusMessage = $"{saved} metadados gravados em .xmp!";

            // Localiza e executa o Lightroom Classic
            string? lrPath = _lightroomService.FindLightroomExecutable();
            bool launched = false;
            if (lrPath != null)
            {
                launched = _lightroomService.LaunchLightroom(_session.CurrentDirectory);
            }

            string infoMessage = launched
                ? $"✅ Seleção sincronizada com sucesso!\n\nO Lightroom Classic foi iniciado automaticamente.\n\n" +
                  $"📌 IMPORTANTE SOBRE A SINCRONIZAÇÃO NO LIGHTROOM:\n" +
                  $"1. Se a pasta for NOVA: Clique em 'Importar' no Lightroom — todas as fotos com 1 estrela e rótulo verde já aparecerão identificadas!\n\n" +
                  $"2. Se a pasta JÁ ESTAVA importada no catálogo do Lightroom: Selecione as fotos na biblioteca do Lightroom e pressione Ctrl + Alt + R (ou clique com botão direito: Metadados -> Ler Metadados dos Arquivos). O Lightroom atualizará todas as estrelas e cores na hora!"
                : $"✅ Arquivos .xmp gravados com sucesso!\n\nNão foi possível abrir o executável do Lightroom automaticamente em C:\\Program Files\\Adobe.\nAbra o Lightroom Classic manualmente e importe ou pressione Ctrl+Alt+R na pasta.";

            MessageBox.Show(infoMessage, "Sincronização com Lightroom Classic", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro na sincronização: {ex.Message}";
            MessageBox.Show($"Erro: {ex.Message}", "Falha ao Sincronizar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSavingXmp = false;
            RefreshStats();
        }
    }

    #endregion

    #region Exportar / Copiar Selecionadas para Nova Pasta

    public async Task ExportSelectedToFolderAsync()
    {
        if (PickedCount == 0)
        {
            MessageBox.Show("Nenhuma foto marcada como Selecionada (Pick). Marque algumas fotos com a tecla 'P' ou botão verde antes de exportar.", "PhotoPick", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string defaultExportDir = !string.IsNullOrEmpty(_session.CurrentDirectory)
            ? Path.Combine(_session.CurrentDirectory, "_SELECIONADAS_PICKS")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PhotoPick_Selecionadas");

        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta de destino para copiar as fotos selecionadas",
            InitialDirectory = _session.CurrentDirectory
        };

        string targetFolder = defaultExportDir;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            targetFolder = dialog.FolderName;
        }

        IsExporting = true;
        StatusMessage = $"Copiando {PickedCount} fotos selecionadas para {Path.GetFileName(targetFolder)}...";

        try
        {
            int copied = await _session.ExportSelectedPhotosAsync(targetFolder);
            StatusMessage = $"Pronto! {copied} fotos e sidecars XMP copiados com sucesso para: {targetFolder}";
            MessageBox.Show($"Foram copiadas com sucesso {copied} fotos selecionadas (RAWs/JPGs e sidecars .xmp) para:\n\n{targetFolder}", "Exportação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao copiar: {ex.Message}";
            MessageBox.Show($"Erro ao exportar fotos: {ex.Message}", "Falha", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExporting = false;
            RefreshStats();
        }
    }

    #endregion

    #region Navegação & Ações Rápidas

    public void NextPhoto()
    {
        if (Photos.Count == 0) return;
        int idx = SelectedPhoto != null ? Photos.IndexOf(SelectedPhoto) : -1;
        if (idx < Photos.Count - 1)
        {
            SelectedPhoto = Photos[idx + 1];
        }
    }

    public void PreviousPhoto()
    {
        if (Photos.Count == 0) return;
        int idx = SelectedPhoto != null ? Photos.IndexOf(SelectedPhoto) : -1;
        if (idx > 0)
        {
            SelectedPhoto = Photos[idx - 1];
        }
    }

    public void RateSelected(int rating)
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.Rating = rating;
        StatusMessage = rating > 0 ? $"Classificado: {rating} estrelas ({SelectedPhoto.FileName})" : $"Classificação removida ({SelectedPhoto.FileName})";
        NextPhoto();
    }

    public void TogglePickSelected()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsPicked = !SelectedPhoto.IsPicked;
        StatusMessage = SelectedPhoto.IsPicked
            ? $"[PICK] Selecionada! 1 Estrela + Rótulo Verde + Aura Verde ({SelectedPhoto.FileName})"
            : $"Seleção removida ({SelectedPhoto.FileName})";
        NextPhoto();
    }

    public void ToggleRejectSelected()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsRejected = !SelectedPhoto.IsRejected;
        StatusMessage = SelectedPhoto.IsRejected
            ? $"[REJECT] Rejeitada! Rótulo Vermelho + Aura Vermelha ({SelectedPhoto.FileName})"
            : $"Rejeição removida ({SelectedPhoto.FileName})";
        NextPhoto();
    }

    public void ClearSelected()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.ClearMarks();
        StatusMessage = $"Marcações limpas ({SelectedPhoto.FileName})";
    }

    public void SetColorSelected(string? color)
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.ColorLabel = color;
        StatusMessage = $"Rótulo de cor definido: {color ?? "Nenhum"} ({SelectedPhoto.FileName})";
    }

    public void ToggleViewMode()
    {
        IsSingleViewMode = !IsSingleViewMode;
    }

    #endregion

    private void RebuildViewModels()
    {
        var currentSelectedPath = SelectedPhoto?.FilePath;
        Photos.Clear();
        Rows.Clear();

        var viewModels = new List<PhotoViewModel>();
        foreach (var model in _session.FilteredPhotos)
        {
            var vm = new PhotoViewModel(model, _session);
            Photos.Add(vm);
            viewModels.Add(vm);
        }

        // Divide em linhas de 4 colunas para o VirtualizingStackPanel
        int itemsPerRow = 4;
        for (int i = 0; i < viewModels.Count; i += itemsPerRow)
        {
            var chunk = viewModels.Skip(i).Take(itemsPerRow).ToArray();
            Rows.Add(new PhotoRowViewModel(chunk));
        }

        if (currentSelectedPath != null)
        {
            SelectedPhoto = Photos.FirstOrDefault(p => p.FilePath == currentSelectedPath) ?? Photos.FirstOrDefault();
        }
        else
        {
            SelectedPhoto = Photos.FirstOrDefault();
        }
    }

    private async Task LoadLoupeImageAsync(PhotoViewModel photo)
    {
        try
        {
            var res = await _extractor.ExtractPreviewAsync(photo.FilePath);
            if (res.Success && res.JpegBytes != null)
            {
                var bmp = ImageHelper.LoadBitmapFromBytes(res.JpegBytes, res.Orientation);
                if (SelectedPhoto == photo)
                {
                    LoupeImage = bmp;
                }
            }
            else if (File.Exists(photo.Model.ThumbnailCachePath))
            {
                var bmp = ImageHelper.LoadBitmapFromFile(photo.Model.ThumbnailCachePath, photo.Orientation);
                if (SelectedPhoto == photo)
                {
                    LoupeImage = bmp;
                }
            }
        }
        catch { }
    }

    private void RefreshStats()
    {
        OnPropertyChanged(nameof(CurrentDirectory));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(RejectedCount));
        OnPropertyChanged(nameof(UnflaggedCount));
        OnPropertyChanged(nameof(UnsavedCount));
        OnPropertyChanged(nameof(Star5Count));
        OnPropertyChanged(nameof(Star4Count));
        OnPropertyChanged(nameof(Star3Count));
        OnPropertyChanged(nameof(Star2Count));
        OnPropertyChanged(nameof(Star1Count));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
