using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Win32;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using PhotoPick.Desktop.Helpers;

namespace PhotoPick.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly CullingSession _session;
    private readonly IRawPreviewExtractor _extractor;
    private bool _isLoadingFolder;
    private bool _isSavingXmp;
    private bool _isSingleViewMode;
    private PhotoViewModel? _selectedPhoto;
    private ImageSource? _loupeImage;
    private string _statusMessage = "Pronto para abrir uma pasta de fotos.";
    private string _filterName = "Todas";

    public CullingSession Session => _session;
    public ObservableCollection<PhotoViewModel> Photos { get; } = [];

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

    public MainViewModel()
    {
        _extractor = new RawPreviewExtractor();
        _session = new CullingSession(extractor: _extractor);
        _session.StatsChanged += RefreshStats;
    }

    public async Task OpenFolderDialogAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecionar Pasta de Fotos RAW / JPG para Triagem",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadDirectoryAsync(dialog.FolderName);
        }
    }

    public async Task LoadDirectoryAsync(string directoryPath)
    {
        IsLoadingFolder = true;
        StatusMessage = $"Escaneando {Path.GetFileName(directoryPath)}...";

        try
        {
            int count = await _session.LoadDirectoryAsync(directoryPath);
            RebuildViewModels();
            StatusMessage = $"{count} fotos carregadas. Pressione 'P' para Pick, '1-5' para Estrelas, 'Espaço' para ampliar.";

            if (Photos.Count > 0)
            {
                SelectedPhoto = Photos[0];
            }
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

    public void ApplyFilter(PhotoFilterMode mode)
    {
        _session.ApplyFilter(mode);
        FilterName = mode switch
        {
            PhotoFilterMode.PickedOnly => "Selecionadas (P)",
            PhotoFilterMode.RejectedOnly => "Rejeitadas (X)",
            PhotoFilterMode.UnflaggedOnly => "Não Avaliadas",
            PhotoFilterMode.RatedOnly => "Com Estrelas",
            _ => "Todas"
        };
        RebuildViewModels();
    }

    public async Task SaveXmpAsync()
    {
        if (UnsavedCount == 0 && TotalCount == 0) return;

        IsSavingXmp = true;
        StatusMessage = "Gravando arquivos sidecar .xmp para o Lightroom...";

        try
        {
            int saved = await _session.SaveAllXmpAsync();
            StatusMessage = $"{saved} metadados sincronizados em .xmp! Pronto para o Lightroom Classic.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao gravar XMP: {ex.Message}";
        }
        finally
        {
            IsSavingXmp = false;
            RefreshStats();
        }
    }

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
            ? $"[PICK] Selecionada! 1 Estrela + Rótulo Verde ({SelectedPhoto.FileName})"
            : $"Seleção removida ({SelectedPhoto.FileName})";
        NextPhoto();
    }

    public void ToggleRejectSelected()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsRejected = !SelectedPhoto.IsRejected;
        StatusMessage = SelectedPhoto.IsRejected
            ? $"[REJECT] Rejeitada! Rótulo Vermelho ({SelectedPhoto.FileName})"
            : $"Rejeição removida ({SelectedPhoto.FileName})";
        NextPhoto();
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

    private void RebuildViewModels()
    {
        var currentSelectedPath = SelectedPhoto?.FilePath;
        Photos.Clear();

        foreach (var model in _session.FilteredPhotos)
        {
            Photos.Add(new PhotoViewModel(model, _session));
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
            // Tenta obter o preview original em alta resolução
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
