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
using System.Windows.Media.Imaging;
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
    private readonly PredictivePrecacheService _precacheService = new();

    private bool _isLoadingFolder;
    private bool _isSavingXmp;
    private bool _isExporting;
    private bool _isSingleViewMode;
    private bool _isDashboardVisible = true;
    private bool _isAutoAdvanceEnabled = true;
    private bool _isFocusPeakingActive;
    private bool _isHelpModalOpen;
    private PhotoViewModel? _selectedPhoto;
    private ImageSource? _loupeImage;
    private ImageSource? _peakingOverlayImage;
    private string _statusMessage = "Pronto para abrir uma pasta de fotos ou arrastar arquivos aqui.";
    private string _filterName = "Todas";
    private int _columnsPerRow = 4;

    private readonly PhotoPick.Core.Services.LicenseManager _licenseManager;
    private LicenseState _licenseState = new();
    private string _enteredLicenseKey = string.Empty;
    private string _licenseErrorMessage = string.Empty;
    private bool _isLicenseDialogOpen;

    public CullingSession Session => _session;
    public ObservableCollection<PhotoViewModel> Photos { get; } = [];
    public ObservableCollection<PhotoRowViewModel> Rows { get; } = [];
    public ObservableCollection<string> CameraList { get; } = ["Todas as Câmeras"];

    public string WindowTitle => "Mavi Select";

    private bool _isDarkTheme = true;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (_isDarkTheme != value)
            {
                _isDarkTheme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeToggleText));
                OnPropertyChanged(nameof(ThemeToggleIcon));
                ThemeService.SetTheme(value ? AppTheme.Dark : AppTheme.Light);
            }
        }
    }

    public string ThemeToggleText => IsDarkTheme ? "Modo Escuro" : "Modo Claro";
    public string ThemeToggleIcon => IsDarkTheme ? "🌙" : "☀️";

    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
    }

    public ObservableCollection<FileFormatItemViewModel> Formats { get; } = [];
    private string? _selectedFormat;
    public string? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (_selectedFormat != value)
            {
                _selectedFormat = value;
                OnPropertyChanged();
                _session.SetFormatFilter(value);
                RebuildViewModels();
            RebuildFormatList();
                UpdateFormatSelection();
            }
        }
    }

    public void FilterByFormat(string? ext)
    {
        SelectedFormat = (ext == "ALL" || string.IsNullOrEmpty(ext)) ? null : ext;
    }

    public void RebuildFormatList()
    {
        Formats.Clear();
        Formats.Add(new FileFormatItemViewModel("ALL", "Todos os Formatos", "🖼️", _session.TotalCount, string.IsNullOrEmpty(_selectedFormat)));

        foreach (var kvp in _session.FormatCounts.OrderByDescending(k => k.Value))
        {
            string ext = kvp.Key;
            string displayName = ext.ToUpperInvariant() switch
            {
                ".CR3" or "CR3" => "Canon RAW (.CR3)",
                ".CR2" or "CR2" => "Canon RAW (.CR2)",
                ".ARW" or "ARW" => "Sony RAW (.ARW)",
                ".NEF" or "NEF" => "Nikon RAW (.NEF)",
                ".RAF" or "RAF" => "Fuji RAW (.RAF)",
                ".DNG" or "DNG" => "Adobe DNG (.DNG)",
                ".JPG" or "JPG" or ".JPEG" or "JPEG" => "Imagens JPEG (.JPG)",
                _ => $"{ext.TrimStart('.').ToUpper()} ({ext})"
            };

            string icon = ext.ToUpperInvariant() switch
            {
                ".CR3" or "CR3" or ".CR2" or "CR2" => "🔴",
                ".ARW" or "ARW" => "🟠",
                ".NEF" or "NEF" => "🟡",
                ".RAF" or "RAF" => "🟢",
                ".DNG" or "DNG" => "🔵",
                _ => "🟣"
            };

            bool isSel = string.Equals(_selectedFormat, ext, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(_selectedFormat, "." + ext, StringComparison.OrdinalIgnoreCase);

            string cleanExt = ext.StartsWith(".") ? ext : "." + ext;
            Formats.Add(new FileFormatItemViewModel(cleanExt, displayName, icon, kvp.Value, isSel));
        }
    }

    private void UpdateFormatSelection()
    {
        foreach (var item in Formats)
        {
            if (string.IsNullOrEmpty(_selectedFormat) || _selectedFormat == "ALL")
            {
                item.IsSelected = (item.Extension == "ALL");
            }
            else
            {
                item.IsSelected = string.Equals(item.Extension, _selectedFormat, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public bool IsWelcomeScreenVisible
    {
        get => _isDashboardVisible;
        set => IsDashboardVisible = value;
    }

    public ObservableCollection<string> RecentFolders { get; } = [];
    public ObservableCollection<string> FlashList { get; } = ["Todos os Disparos", "Disparo Godox X3 / V1 Pro", "Sem Flash"];

    public string CurrentDirectoryName => !string.IsNullOrEmpty(_session.CurrentDirectory) 
        ? Path.GetFileName(_session.CurrentDirectory) 
        : "Nenhuma pasta aberta";

    public string CurrentDirectoryPath => _session.CurrentDirectory ?? "Selecione uma pasta para começar";

    private bool _isMetadataModalOpen;
    public bool IsMetadataModalOpen
    {
        get => _isMetadataModalOpen;
        set
        {
            if (_isMetadataModalOpen != value)
            {
                _isMetadataModalOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public void OpenMetadataModal()
    {
        if (SelectedPhoto == null) return;
        IsMetadataModalOpen = true;
        if (string.IsNullOrEmpty(SelectedPhoto.CameraModel) || !SelectedPhoto.Model.Iso.HasValue)
        {
            _ = EnsureSelectedPhotoMetadataAsync(SelectedPhoto);
        }
    }

    public void CloseMetadataModal()
    {
        IsMetadataModalOpen = false;
    }

    public void CopyAllMetadataToClipboard()
    {
        if (SelectedPhoto == null) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== METADADOS DA IMAGEM — MAVI SELECT ===");
            sb.AppendLine($"Arquivo: {SelectedPhoto.FileName}");
            sb.AppendLine($"Caminho: {SelectedPhoto.FilePath}");
            sb.AppendLine($"Formato: {SelectedPhoto.Extension.TrimStart('.').ToUpperInvariant()}");
            sb.AppendLine($"Tamanho: {SelectedPhoto.FormattedSize}");
            sb.AppendLine($"Dimensões: {SelectedPhoto.FormattedDimensions}");
            sb.AppendLine($"Data/Hora: {SelectedPhoto.FormattedDate}");
            sb.AppendLine();
            sb.AppendLine("--- EQUIPAMENTO ---");
            sb.AppendLine($"Fabricante: {SelectedPhoto.Model.CameraMake ?? "-"}");
            sb.AppendLine($"Câmera: {SelectedPhoto.FormattedCamera}");
            sb.AppendLine($"Número de Série: {SelectedPhoto.FormattedSerialNumber}");
            sb.AppendLine($"Software / Firmware: {SelectedPhoto.FormattedSoftware}");
            sb.AppendLine();
            sb.AppendLine("--- LENTE & ÓPTICA ---");
            sb.AppendLine($"Lente: {SelectedPhoto.FormattedLens}");
            sb.AppendLine($"Distância Focal: {SelectedPhoto.FormattedFocalLength}");
            sb.AppendLine($"Equiv. 35mm: {SelectedPhoto.Formatted35mmEquiv}");
            sb.AppendLine($"Abertura Máxima: {SelectedPhoto.FormattedMaxAperture}");
            sb.AppendLine();
            sb.AppendLine("--- EXPOSIÇÃO & DISPARO ---");
            sb.AppendLine($"Velocidade: {SelectedPhoto.FormattedShutter}");
            sb.AppendLine($"Abertura: {SelectedPhoto.FormattedAperture}");
            sb.AppendLine($"ISO: {SelectedPhoto.FormattedIso}");
            sb.AppendLine($"Compensação EV: {SelectedPhoto.FormattedExposureBias}");
            sb.AppendLine($"Modo de Medição: {SelectedPhoto.FormattedMeteringMode}");
            sb.AppendLine($"Programa de Exposição: {SelectedPhoto.FormattedExposureProgram}");
            sb.AppendLine($"Modo de Exposição: {SelectedPhoto.FormattedExposureMode}");
            sb.AppendLine($"Balanço de Branco: {SelectedPhoto.FormattedWhiteBalance}");
            sb.AppendLine($"Flash: {SelectedPhoto.FormattedFlash}");
            sb.AppendLine();
            sb.AppendLine("--- QUALIDADE INTELIGENTE ---");
            sb.AppendLine($"Diagnóstico: {SelectedPhoto.QualityStatusText}");
            sb.AppendLine($"Score de Foco: {SelectedPhoto.SharpnessPercent:0.#}%");
            sb.AppendLine($"Balanço de Exposição: {SelectedPhoto.BrightnessPercent:0.#}%");

            Clipboard.SetText(sb.ToString());
        }
        catch { }
    }

    private bool _isGamepadConnected;
    public bool IsGamepadConnected
    {
        get => _isGamepadConnected;
        set
        {
            if (_isGamepadConnected != value)
            {
                _isGamepadConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GamepadStatusText));
                OnPropertyChanged(nameof(GamepadToolTip));
                OnPropertyChanged(nameof(GamepadIconBrush));
                OnPropertyChanged(nameof(GamepadIconOpacity));
            }
        }
    }

    public string GamepadStatusText => IsGamepadConnected ? "🎮 Gamepad Conectado" : "🎮 Nenhum Gamepad";
    public string GamepadToolTip => IsGamepadConnected ? "Controle Gamepad Conectado" : "Nenhum controle detectado";
    public double GamepadIconOpacity => IsGamepadConnected ? 1.0 : 0.45;
    public Brush GamepadIconBrush => IsGamepadConnected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3));

    public bool HasSelectedPhoto => SelectedPhoto != null;
    public string SelectedPhotoName => SelectedPhoto?.FileName ?? "Nenhuma foto selecionada";
    public string SelectedPhotoCamera => SelectedPhoto?.FormattedCamera ?? "-";
    public string SelectedPhotoLens => SelectedPhoto?.FormattedLens ?? "-";
    public string SelectedPhotoExposure => SelectedPhoto?.FormattedExposureLine ?? "-";
    public string SelectedPhotoFocalLength => SelectedPhoto?.FormattedFocalLength ?? "-";
    public string SelectedPhotoFlash => SelectedPhoto?.FormattedFlash ?? "-";
    public string SelectedPhotoDimensions => SelectedPhoto?.FormattedMegapixels ?? "-";
    public string SelectedPhotoFileInfo => SelectedPhoto?.FormattedFileInfo ?? "-";
    public string SelectedPhotoDate => SelectedPhoto?.FormattedDate ?? "-";
    public double SelectedPhotoSharpnessPercent => SelectedPhoto?.SharpnessPercent ?? 0;
    public double SelectedPhotoBrightnessPercent => SelectedPhoto?.BrightnessPercent ?? 0;
    public string SelectedPhotoQualityText => SelectedPhoto?.QualityStatusText ?? "-";
    public Brush SelectedPhotoQualityBrush => SelectedPhoto?.QualityStatusBrush ?? Brushes.Transparent;

    public bool IsDashboardVisible
    {
        get => _isDashboardVisible;
        set
        {
            if (_isDashboardVisible != value)
            {
                _isDashboardVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDashboardVisible));
                OnPropertyChanged(nameof(IsWelcomeScreenVisible));
                OnPropertyChanged(nameof(IsWorkspaceVisible));
            }
        }
    }

    public bool IsWorkspaceVisible => !IsDashboardVisible;

    public LicenseState LicenseState
    {
        get => _licenseState;
        private set
        {
            _licenseState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLicenseLocked));
            OnPropertyChanged(nameof(IsTrialActive));
            OnPropertyChanged(nameof(IsTrialExpired));
            OnPropertyChanged(nameof(IsClockTampered));
            OnPropertyChanged(nameof(TrialDaysRemaining));
            OnPropertyChanged(nameof(TrialBadgeText));
            OnPropertyChanged(nameof(MachineId));
            OnPropertyChanged(nameof(LicenseStatusMessage));
            OnPropertyChanged(nameof(IsLicenseOverlayVisible));
            OnPropertyChanged(nameof(CanDismissLicenseOverlay));
            OnPropertyChanged(nameof(LicenseLockTitle));
            OnPropertyChanged(nameof(LicenseLockSubtitle));
            OnPropertyChanged(nameof(LicenseExitButtonText));
        }
    }

    public bool IsLicenseLocked => !_licenseState.CanUseApp;
    public bool IsTrialActive => !_licenseState.IsActivated && _licenseState.CanUseApp;
    public bool IsTrialExpired => _licenseState.IsTrialExpired;
    public bool IsClockTampered => _licenseState.IsTampered;
    public int TrialDaysRemaining => _licenseState.DaysRemaining;
    public string TrialBadgeText => _licenseState.IsActivated 
        ? "✔ Licença Ativada" 
        : $"⏳ Teste: {_licenseState.DaysRemaining} dia(s)";
    public string MachineId => _licenseState.MachineId;
    public string LicenseStatusMessage => _licenseState.StatusMessage;

    public bool IsLicenseOverlayVisible => IsLicenseLocked || IsLicenseDialogOpen;
    public bool CanDismissLicenseOverlay => !IsLicenseLocked;

    public string LicenseLockTitle
    {
        get
        {
            if (IsClockTampered) return "Alteração de Data do Sistema Detectada";
            if (IsTrialExpired) return "Período de Avaliação Encerrado";
            if (_licenseState.IsActivated) return "Mavi Select — Licença Vitalícia Ativada";
            return $"Mavi Select — Avaliação ({TrialDaysRemaining} dias restantes)";
        }
    }

    public string LicenseLockSubtitle
    {
        get
        {
            if (IsClockTampered) 
                return "O relógio do Windows foi retrocedido para uma data anterior à última execução. Para continuar, sincronize a data/hora correta ou insira sua chave de ativação vitalícia.";
            if (IsTrialExpired) 
                return "Seu período de teste gratuito de 10 dias expirou. Para desbloquear o Mavi Select permanentemente e continuar selecionando suas fotos com máxima velocidade, insira sua chave de ativação.";
            if (_licenseState.IsActivated) 
                return "Sua cópia do Mavi Select está ativada com sucesso e com todos os recursos profissionais liberados permanentemente para este computador.";
            return $"Você está no período de avaliação gratuita com todos os recursos liberados ({TrialDaysRemaining} dia(s) restante(s)). Deseja registrar sua chave de ativação definitiva?";
        }
    }

    public string LicenseExitButtonText => IsLicenseLocked ? "Sair do Aplicativo" : "Continuar Avaliação";

    public string EnteredLicenseKey
    {
        get => _enteredLicenseKey;
        set
        {
            if (_enteredLicenseKey != value)
            {
                _enteredLicenseKey = value;
                OnPropertyChanged();
                LicenseErrorMessage = string.Empty;
            }
        }
    }

    public string LicenseErrorMessage
    {
        get => _licenseErrorMessage;
        set { _licenseErrorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLicenseError)); }
    }

    public bool HasLicenseError => !string.IsNullOrEmpty(_licenseErrorMessage);

    public bool IsLicenseDialogOpen
    {
        get => _isLicenseDialogOpen;
        set
        {
            _isLicenseDialogOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLicenseOverlayVisible));
            OnPropertyChanged(nameof(CanDismissLicenseOverlay));
            OnPropertyChanged(nameof(LicenseLockTitle));
            OnPropertyChanged(nameof(LicenseLockSubtitle));
            OnPropertyChanged(nameof(LicenseExitButtonText));
        }
    }

    public void RefreshLicenseState()
    {
        LicenseState = _licenseManager.GetLicenseState();
    }

    public bool TryActivateLicense()
    {
        if (string.IsNullOrWhiteSpace(EnteredLicenseKey))
        {
            LicenseErrorMessage = "Por favor, digite ou cole sua chave de ativação.";
            return false;
        }

        bool ok = _licenseManager.Activate(EnteredLicenseKey);
        if (ok)
        {
            LicenseErrorMessage = string.Empty;
            RefreshLicenseState();
            IsLicenseDialogOpen = false;
            StatusMessage = "Mavi Select ativado com sucesso! Licença vitalícia vinculada.";
            MessageBox.Show(
                "O Mavi Select foi ativado com sucesso!\n\nSua licença vitalícia está confirmada para este computador.",
                "Ativação Concluída",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return true;
        }
        else
        {
            LicenseErrorMessage = "Chave de ativação inválida para este computador. Verifique os caracteres e tente novamente.";
            return false;
        }
    }

    public void CopyMachineId()
    {
        try
        {
            Clipboard.SetText(MachineId);
            StatusMessage = $"ID da Máquina ({MachineId}) copiado para a Área de Transferência!";
        }
        catch { }
    }

    public void OpenLicenseDialog()
    {
        LicenseErrorMessage = string.Empty;
        EnteredLicenseKey = string.Empty;
        IsLicenseDialogOpen = true;
    }

    public void CloseLicenseDialog()
    {
        IsLicenseDialogOpen = false;
    }

    public void ExitApplication()
    {
        Application.Current.Shutdown();
    }

    public bool IsAutoAdvanceEnabled
    {
        get => _isAutoAdvanceEnabled;
        set
        {
            if (_isAutoAdvanceEnabled != value)
            {
                _isAutoAdvanceEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoAdvanceLabel));
            }
        }
    }

    public string AutoAdvanceLabel => IsAutoAdvanceEnabled ? "⚡ Auto-Advance (ON)" : "⏸ Auto-Advance (OFF)";

    public bool IsFocusPeakingActive
    {
        get => _isFocusPeakingActive;
        set
        {
            if (_isFocusPeakingActive != value)
            {
                _isFocusPeakingActive = value;
                OnPropertyChanged();
                UpdatePeakingOverlay();
            }
        }
    }

    public ImageSource? PeakingOverlayImage
    {
        get => _peakingOverlayImage;
        private set
        {
            _peakingOverlayImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPeakingOverlay));
        }
    }

    public bool HasPeakingOverlay => _isFocusPeakingActive && _peakingOverlayImage != null;

    public bool IsHelpModalOpen
    {
        get => _isHelpModalOpen;
        set
        {
            if (_isHelpModalOpen != value)
            {
                _isHelpModalOpen = value;
                OnPropertyChanged();
            }
        }
    }

    private string _selectedCamera = "Todas as Câmeras";
    public string SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (_selectedCamera != value)
            {
                _selectedCamera = value;
                OnPropertyChanged();
                _session.SetCameraFilter(value == "Todas as Câmeras" ? null : value);
                RebuildViewModels();
            }
        }
    }

    private string _selectedFlashFilter = "Todos";
    public string SelectedFlashFilter
    {
        get => _selectedFlashFilter;
        set
        {
            if (_selectedFlashFilter != value)
            {
                _selectedFlashFilter = value;
                OnPropertyChanged();
                bool? f = value switch
                {
                    "⚡ Flash Disparou" => true,
                    "🚫 Sem Flash" => false,
                    _ => null
                };
                _session.SetFlashFilter(f);
                RebuildViewModels();
            }
        }
    }

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
                OnPropertyChanged(nameof(ViewModeLabel));
                OnPropertyChanged(nameof(ViewModeToolTip));
                if (_isSingleViewMode && SelectedPhoto != null)
                {
                    _ = LoadLoupeImageAsync(SelectedPhoto);
                }
            }
        }
    }

    public bool IsGridViewMode => !IsSingleViewMode;
    public string ViewModeLabel => IsSingleViewMode ? "⊞ Modo Grade" : "🔍 Foto Única";
    public string ViewModeToolTip => IsSingleViewMode ? "Voltar para a Grade de Fotos [Espaço]" : "Ver Foto Única ampliada [Espaço]";

    public event Action<PhotoViewModel>? PhotoSelected;

    public void SelectPhoto(PhotoViewModel? p) => SelectedPhoto = p;
    public PhotoViewModel? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            if (_selectedPhoto != value)
            {
                if (_selectedPhoto != null)
                {
                    _selectedPhoto.IsSelected = false;
                    _selectedPhoto.PropertyChanged -= SelectedPhoto_PropertyChanged;
                }
                _selectedPhoto = value;
                if (_selectedPhoto != null)
                {
                    _selectedPhoto.IsSelected = true;
                    _selectedPhoto.PropertyChanged += SelectedPhoto_PropertyChanged;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasSelectedPhoto));
                OnPropertyChanged(nameof(SelectedPhotoName));
                OnPropertyChanged(nameof(SelectedPhotoCamera));
                OnPropertyChanged(nameof(SelectedPhotoLens));
                OnPropertyChanged(nameof(SelectedPhotoExposure));
                OnPropertyChanged(nameof(SelectedPhotoFocalLength));
                OnPropertyChanged(nameof(SelectedPhotoFlash));
                OnPropertyChanged(nameof(SelectedPhotoDimensions));
                OnPropertyChanged(nameof(SelectedPhotoFileInfo));
                OnPropertyChanged(nameof(SelectedPhotoDate));
                OnPropertyChanged(nameof(SelectedPhotoSharpnessPercent));
                OnPropertyChanged(nameof(SelectedPhotoBrightnessPercent));
                OnPropertyChanged(nameof(SelectedPhotoQualityText));
                OnPropertyChanged(nameof(SelectedPhotoQualityBrush));

                if (_selectedPhoto != null)
                {
                    PhotoSelected?.Invoke(_selectedPhoto);

                    if (_selectedPhoto.Thumbnail == null)
                    {
                        _ = _loaderQueue.LoadThumbnailDirectAsync(_selectedPhoto);
                    }

                    // Se a foto selecionada estiver sem metadados (ex: cache antigo), extrai imediatamente!
                    if (string.IsNullOrEmpty(_selectedPhoto.CameraModel) || !_selectedPhoto.Model.Iso.HasValue)
                    {
                        _ = EnsureSelectedPhotoMetadataAsync(_selectedPhoto);
                    }

                    if (IsSingleViewMode)
                    {
                        _ = LoadLoupeImageAsync(_selectedPhoto);
                    }
                }
            }
        }
    }

    private async Task EnsureSelectedPhotoMetadataAsync(PhotoViewModel photo)
    {
        try
        {
            var res = await _extractor.ExtractPreviewAsync(photo.FilePath);
            if (res.Success)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    photo.ApplyMetadata(res);
                    OnPropertyChanged(nameof(SelectedPhotoCamera));
                    OnPropertyChanged(nameof(SelectedPhotoLens));
                    OnPropertyChanged(nameof(SelectedPhotoExposure));
                    OnPropertyChanged(nameof(SelectedPhotoFocalLength));
                    OnPropertyChanged(nameof(SelectedPhotoFlash));
                    OnPropertyChanged(nameof(SelectedPhotoDimensions));
                    OnPropertyChanged(nameof(SelectedPhotoFileInfo));
                    OnPropertyChanged(nameof(SelectedPhotoDate));
                });
                await _session.SavePhotoMetadataAsync(photo.Model);
            }
        }
        catch { }
    }

    private void SelectedPhoto_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedPhotoName));
        OnPropertyChanged(nameof(SelectedPhotoCamera));
        OnPropertyChanged(nameof(SelectedPhotoLens));
        OnPropertyChanged(nameof(SelectedPhotoExposure));
        OnPropertyChanged(nameof(SelectedPhotoFocalLength));
        OnPropertyChanged(nameof(SelectedPhotoFlash));
        OnPropertyChanged(nameof(SelectedPhotoDimensions));
        OnPropertyChanged(nameof(SelectedPhotoFileInfo));
        OnPropertyChanged(nameof(SelectedPhotoDate));
        OnPropertyChanged(nameof(SelectedPhotoSharpnessPercent));
        OnPropertyChanged(nameof(SelectedPhotoBrightnessPercent));
        OnPropertyChanged(nameof(SelectedPhotoQualityText));
        OnPropertyChanged(nameof(SelectedPhotoQualityBrush));
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
    public int DoubtCount => _session.DoubtCount;
    public int RejectedCount => _session.RejectedCount;
    public int UnflaggedCount => _session.UnflaggedCount;
    public int UnsavedCount => _session.UnsavedCount;

    // Contagens de Qualidade e Rajadas
    public int GoodCount => _session.GoodCount;
    public int BlurryCount => _session.BlurryCount;
    public int UnderexposedCount => _session.UnderexposedCount;
    public int OverexposedCount => _session.OverexposedCount;
    public int BurstCount => _session.BurstCount;

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
        _loaderQueue = new ThumbnailLoaderQueue(_session, _extractor, _session.CacheService);
        _loaderQueue.Start();

        _licenseManager = new PhotoPick.Core.Services.LicenseManager();
        RefreshLicenseState();

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
            _loaderQueue.Start();
            RebuildViewModels();
            RebuildFormatList();
            StatusMessage = $"{count} fotos carregadas. Use os botões ou atalhos: [P] Escolher, [X] Rejeitar, [D] Dúvida, [U] Limpar, [1-5] Notas.";

            if (Photos.Count > 0)
            {
                IsDashboardVisible = false;
                if (!RecentFolders.Contains(directoryPath)) { RecentFolders.Insert(0, directoryPath); }
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

    #endregion

    #region Filtros

    public void ApplyFilter(PhotoFilterMode mode)
    {
        _session.ApplyFilter(mode);
        FilterName = mode switch
        {
            PhotoFilterMode.PickedOnly => "Selecionadas [P]",
            PhotoFilterMode.DoubtOnly => "Dúvidas para Revisão [D]",
            PhotoFilterMode.RejectedOnly => "Rejeitadas [X]",
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

    #region Sincronização & Abertura Exclusiva de Selecionadas no Lightroom Classic

    private bool _isLightroomModalOpen;
    public bool IsLightroomModalOpen
    {
        get => _isLightroomModalOpen;
        set { _isLightroomModalOpen = value; OnPropertyChanged(); }
    }

    public int SelectedToSendCount
    {
        get
        {
            if (_session.CurrentFilter is PhotoFilterMode.PickedOnly or >= PhotoFilterMode.Rating5)
            {
                return _session.FilteredPhotos.Count;
            }
            int count = _session.FilteredPhotos.Count(p => p.IsPicked);
            return count > 0 ? count : _session.PickedCount;
        }
    }

    public void OpenLightroomModal()
    {
        if (TotalCount == 0)
        {
            MessageBox.Show("Abra uma pasta de fotos antes de enviar para o Lightroom.", "MaviSelect", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedToSendCount == 0)
        {
            MessageBox.Show("Nenhuma foto selecionada! Marque as melhores fotos com a tecla 'P' ou botão verde 'Escolher' antes de enviar ao Lightroom.", "MaviSelect", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OnPropertyChanged(nameof(SelectedToSendCount));
        IsLightroomModalOpen = true;
    }

    public void CloseLightroomModal()
    {
        IsLightroomModalOpen = false;
    }

    public async Task SendToLightroomViaSubfolderAsync()
    {
        IsLightroomModalOpen = false;
        IsSavingXmp = true;

        var targetPhotos = GetActiveTargetPhotos();
        string targetSubdir = Path.Combine(_session.CurrentDirectory!, "_SELECIONADAS_LIGHTROOM");
        StatusMessage = $"Copiando {targetPhotos.Count} fotos selecionadas para a pasta _SELECIONADAS_LIGHTROOM...";

        try
        {
            int copied = await _session.ExportSpecificPhotosAsync(targetPhotos, targetSubdir);
            StatusMessage = $"{copied} fotos selecionadas preparadas em: _SELECIONADAS_LIGHTROOM";

            // Inicia o Lightroom apontando EXCLUSIVAMENTE para a pasta com as selecionadas
            bool launched = _lightroomService.LaunchLightroom(targetSubdir);

            string info = launched
                ? $"✅ SUCESSO!\n\nForam enviadas EXCLUSIVAMENTE as {copied} fotos selecionadas para o Lightroom Classic!\n\nPasta criada: {targetSubdir}\n\nO Lightroom Classic foi iniciado apontando diretamente para essa pasta. Na tela de importação, aparecem APENAS as fotos selecionadas, sem nenhuma foto rejeitada!"
                : $"✅ Foram copiadas {copied} fotos selecionadas para:\n{targetSubdir}\n\nAbra o Lightroom Classic e importe essa pasta.";

            MessageBox.Show(info, "Enviado para o Lightroom Classic", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao preparar selecionadas: {ex.Message}";
            MessageBox.Show($"Erro: {ex.Message}", "Falha", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSavingXmp = false;
            RefreshStats();
        }
    }

    public async Task SendToLightroomInPlaceAsync()
    {
        IsLightroomModalOpen = false;
        IsSavingXmp = true;

        var targetPhotos = GetActiveTargetPhotos();
        StatusMessage = $"Gravando metadados XMP apenas nas {targetPhotos.Count} fotos selecionadas...";

        try
        {
            int saved = await _session.SaveOnlySelectedXmpAsync(targetPhotos);
            StatusMessage = $"{saved} fotos selecionadas sincronizadas com XMP!";

            bool launched = _lightroomService.LaunchLightroom(_session.CurrentDirectory);

            string info = launched
                ? $"✅ Metadados XMP gravados exclusivamente nas {saved} fotos selecionadas!\n\nO Lightroom Classic foi aberto.\n\n📌 DICA DE IMPORTAÇÃO NO LIGHTROOM:\n1. Se a pasta for NOVA: Na tela de importação ou na grade de biblioteca, filtre por 1 Estrela ou Rótulo Verde para ver apenas as fotos escolhidas.\n2. Se a pasta JÁ ESTAVA no catálogo: Pressione Ctrl + Alt + R na pasta para ler os novos arquivos XMP."
                : $"Metadados gravados com sucesso exclusivamente nas fotos selecionadas.";

            MessageBox.Show(info, "Lightroom Classic", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro: {ex.Message}", "Falha", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSavingXmp = false;
            RefreshStats();
        }
    }

    private List<PhotoItem> GetActiveTargetPhotos()
    {
        if (_session.CurrentFilter is PhotoFilterMode.PickedOnly or >= PhotoFilterMode.Rating5)
        {
            return _session.FilteredPhotos.ToList();
        }

        var picked = _session.FilteredPhotos.Where(p => p.IsPicked).ToList();
        if (picked.Count > 0) return picked;

        return _session.FilteredPhotos.ToList();
    }

    public async Task SyncAndOpenLightroomAsync()
    {
        // Abre o modal de escolha com foco em enviar apenas as selecionadas
        OpenLightroomModal();
        await Task.CompletedTask;
    }

    #endregion

    #region Exportar / Copiar Selecionadas para Nova Pasta

    public async Task ExportSelectedToFolderAsync()
    {
        if (PickedCount == 0)
        {
            MessageBox.Show("Nenhuma foto marcada como Escolhida. Marque algumas fotos com a tecla 'P' ou botão verde 'Escolher' antes de exportar.", "MaviSelect", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string defaultExportDir = !string.IsNullOrEmpty(_session.CurrentDirectory)
            ? Path.Combine(_session.CurrentDirectory, "_SELECIONADAS_PICKS")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MaviSelect_Selecionadas");

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

    public void HandleGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.PreviousPhoto:
            case GamepadAction.NavigateLeft:
                PreviousPhoto();
                break;
            case GamepadAction.NextPhoto:
            case GamepadAction.NavigateRight:
                NextPhoto();
                break;
            case GamepadAction.Pick:
                TogglePickSelected();
                break;
            case GamepadAction.Reject:
                ToggleRejectSelected();
                break;
            case GamepadAction.ToggleFocusPeaking:
                ToggleFocusPeaking();
                break;
            case GamepadAction.NavigateUp:
                NavigateGridUp();
                break;
            case GamepadAction.NavigateDown:
                NavigateGridDown();
                break;
        }
    }

    public void NavigateGridUp()
    {
        if (SelectedPhoto == null || Photos.Count == 0) return;
        int idx = Photos.IndexOf(SelectedPhoto);
        int target = Math.Max(0, idx - _columnsPerRow);
        SelectedPhoto = Photos[target];
    }

    public void NavigateGridDown()
    {
        if (SelectedPhoto == null || Photos.Count == 0) return;
        int idx = Photos.IndexOf(SelectedPhoto);
        int target = Math.Min(Photos.Count - 1, idx + _columnsPerRow);
        SelectedPhoto = Photos[target];
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

    public void RateSelected(int rating, bool autoAdvance = false)
    {
        if (SelectedPhoto == null) return;
        int newRating = (SelectedPhoto.Rating == rating) ? 0 : rating;
        SelectedPhoto.Rating = newRating;
        StatusMessage = newRating > 0 ? $"Classificado: {newRating} estrelas ({SelectedPhoto.FileName})" : $"Classificação removida ({SelectedPhoto.FileName})";
        if (autoAdvance && IsAutoAdvanceEnabled) NextPhoto();
    }

    public async Task DeleteSelectedPhotoAsync()
    {
        if (SelectedPhoto == null) return;

        var photoToDelete = SelectedPhoto;
        var result = MessageBox.Show(
            $"Deseja realmente excluir a foto '{photoToDelete.FileName}' do seu computador?\n\nO arquivo original e suas configurações serão movidos para a Lixeira do Windows.",
            "Confirmar Exclusão de Arquivo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        int currentIndex = Photos.IndexOf(photoToDelete);

        try
        {
            // 1. Move arquivo original para a Lixeira do Windows
            bool trashed = FileTrashHelper.SendToTrash(photoToDelete.FilePath);
            if (!trashed && File.Exists(photoToDelete.FilePath))
            {
                MessageBox.Show(
                    $"Não foi possível mover o arquivo '{photoToDelete.FileName}' para a lixeira.",
                    "Erro ao Excluir",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 2. Move arquivo .xmp associado (se existir) para a Lixeira
            string xmpPath = Path.ChangeExtension(photoToDelete.FilePath, ".xmp");
            if (File.Exists(xmpPath))
            {
                FileTrashHelper.SendToTrash(xmpPath);
            }

            // 3. Remove arquivo do cache de miniaturas se existir
            if (!string.IsNullOrEmpty(photoToDelete.Model.ThumbnailCachePath) && File.Exists(photoToDelete.Model.ThumbnailCachePath))
            {
                try { File.Delete(photoToDelete.Model.ThumbnailCachePath); } catch { }
            }

            // 4. Remove do banco SQLite e da sessão de triagem
            await _session.DeletePhotoAsync(photoToDelete.Model);

            // 5. Remove da lista observável
            Photos.Remove(photoToDelete);
            RebuildRows();
            RebuildFormatList();
            RefreshStats();

            // 6. Seleciona a próxima foto ou limpa a seleção
            if (Photos.Count > 0)
            {
                int nextIndex = Math.Clamp(currentIndex, 0, Photos.Count - 1);
                SelectedPhoto = Photos[nextIndex];
            }
            else
            {
                SelectedPhoto = null;
                LoupeImage = null;
                PeakingOverlayImage = null;
            }

            StatusMessage = $"Foto '{photoToDelete.FileName}' enviada para a Lixeira do Windows.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ocorreu um erro ao tentar excluir o arquivo:\n{ex.Message}",
                "Erro de Exclusão",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void TogglePickSelected()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsPicked = !SelectedPhoto.IsPicked;
        StatusMessage = SelectedPhoto.IsPicked
            ? $"Escolhida! ({SelectedPhoto.FileName})"
            : $"Escolha removida ({SelectedPhoto.FileName})";
        if (IsAutoAdvanceEnabled) NextPhoto();
    }

    public void ToggleDoubtSelected()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsDoubt = !SelectedPhoto.IsDoubt;
        StatusMessage = SelectedPhoto.IsDoubt
            ? $"Marcada como Dúvida para revisão posterior ({SelectedPhoto.FileName})"
            : $"Dúvida removida ({SelectedPhoto.FileName})";
        if (IsAutoAdvanceEnabled) NextPhoto();
    }

    public void ToggleRejectSelected()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsRejected = !SelectedPhoto.IsRejected;
        StatusMessage = SelectedPhoto.IsRejected
            ? $"Rejeitada! ({SelectedPhoto.FileName})"
            : $"Rejeição removida ({SelectedPhoto.FileName})";
        if (IsAutoAdvanceEnabled) NextPhoto();
    }

    public void PickCurrentBurstBest()
    {
        if (SelectedPhoto == null) return;
        var affected = _session.PickBurstBest(SelectedPhoto.Model);
        StatusMessage = $"[RAJADA] Foto {SelectedPhoto.FileName} selecionada e {affected.Count - 1} fotos da sequência rejeitadas.";
        if (IsAutoAdvanceEnabled) NextPhoto();
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

    public void ToggleAutoAdvance()
    {
        IsAutoAdvanceEnabled = !IsAutoAdvanceEnabled;
    }

    public void ToggleFocusPeaking()
    {
        IsFocusPeakingActive = !IsFocusPeakingActive;
    }

    public void ShowDashboard() => IsDashboardVisible = true;
    public void HideDashboard() => IsDashboardVisible = false;
    public void OpenHelpModal() => IsHelpModalOpen = true;
    public void CloseHelpModal() => IsHelpModalOpen = false;

    #region Filtros de Qualidade e Rajadas

    public void FilterGood()
    {
        _session.ApplyFilter(PhotoFilterMode.GoodOnly);
        FilterName = "✨ Boas";
        RebuildViewModels();
    }

    public void FilterBlurry()
    {
        _session.ApplyFilter(PhotoFilterMode.BlurryOnly);
        FilterName = "🌫️ Embaçadas";
        RebuildViewModels();
    }

    public void FilterUnderexposed()
    {
        _session.ApplyFilter(PhotoFilterMode.UnderexposedOnly);
        FilterName = "🌑 Muito Escuras";
        RebuildViewModels();
    }

    public void FilterOverexposed()
    {
        _session.ApplyFilter(PhotoFilterMode.OverexposedOnly);
        FilterName = "☀️ Muito Claras";
        RebuildViewModels();
    }

    public void FilterBurstStacks()
    {
        _session.ApplyFilter(PhotoFilterMode.BurstStacksOnly);
        FilterName = "⚡ Rajadas";
        RebuildViewModels();
    }

    #endregion

    #endregion

    public void UpdateColumnsForWidth(double availableWidth)
    {
        if (availableWidth <= 0) return;
        // Ajusta colunas: de 2 a 7 colunas proporcionalmente
        int cols = Math.Clamp((int)(availableWidth / 285), 2, 7);
        if (cols != _columnsPerRow || (Rows.Count == 0 && Photos.Count > 0))
        {
            _columnsPerRow = cols;
            RebuildRows();
        }
    }

    private void RebuildRows()
    {
        Rows.Clear();
        int itemsPerRow = _columnsPerRow;
        for (int i = 0; i < Photos.Count; i += itemsPerRow)
        {
            var chunk = Photos.Skip(i).Take(itemsPerRow).ToArray();
            Rows.Add(new PhotoRowViewModel(chunk));
        }
    }

    private void RebuildViewModels()
    {
        var currentSelectedPath = SelectedPhoto?.FilePath;
        Photos.Clear();

        var viewModels = new List<PhotoViewModel>();
        foreach (var model in _session.FilteredPhotos)
        {
            var vm = new PhotoViewModel(model, _session);
            Photos.Add(vm);
            viewModels.Add(vm);
        }

        RebuildRows();

        if (currentSelectedPath != null)
        {
            SelectedPhoto = Photos.FirstOrDefault(p => p.FilePath == currentSelectedPath) ?? Photos.FirstOrDefault();
        }
        else
        {
            SelectedPhoto = Photos.FirstOrDefault();
        }

        // Prioriza imediatamente os visíveis e enfileira o restante sempre que a visualização for reconstruída
        _loaderQueue.PrioritizeAndEnqueue(Photos);
    }

    private async Task LoadLoupeImageAsync(PhotoViewModel photo)
    {
        try
        {
            if (_precacheService.TryGet(photo.FilePath, out var cached) && cached != null)
            {
                if (SelectedPhoto == photo)
                {
                    LoupeImage = cached;
                    if (IsFocusPeakingActive) UpdatePeakingOverlay();
                }
            }
            else
            {
                var res = await _extractor.ExtractPreviewAsync(photo.FilePath);
                if (res.Success && res.JpegBytes != null)
                {
                    var bmp = ImageHelper.LoadBitmapFromBytes(res.JpegBytes, res.Orientation);
                    if (bmp != null)
                    {
                        _precacheService.Store(photo.FilePath, bmp);
                        if (SelectedPhoto == photo)
                        {
                            LoupeImage = bmp;
                            if (IsFocusPeakingActive) UpdatePeakingOverlay();
                        }
                    }
                }
                else if (File.Exists(photo.Model.ThumbnailCachePath))
                {
                    var bmp = ImageHelper.LoadBitmapFromFile(photo.Model.ThumbnailCachePath, photo.Orientation);
                    if (bmp != null && SelectedPhoto == photo)
                    {
                        LoupeImage = bmp;
                        if (IsFocusPeakingActive) UpdatePeakingOverlay();
                    }
                }
            }

            int currentIdx = _session.FilteredPhotos.IndexOf(photo.Model);
            if (currentIdx >= 0)
            {
                _precacheService.SchedulePrecache(_session.FilteredPhotos, currentIdx);
            }
        }
        catch { }
    }

    public void UpdatePeakingOverlay()
    {
        if (!IsFocusPeakingActive || LoupeImage is not BitmapSource bms)
        {
            PeakingOverlayImage = null;
            return;
        }

        Task.Run(() =>
        {
            var overlay = FocusPeakingHelper.GeneratePeakingOverlay(bms);
            Application.Current.Dispatcher.Invoke(() =>
            {
                PeakingOverlayImage = overlay;
            });
        });
    }

    private void RefreshStats()
    {
        OnPropertyChanged(nameof(CurrentDirectory));
        OnPropertyChanged(nameof(CurrentDirectoryName));
        OnPropertyChanged(nameof(CurrentDirectoryPath));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(DoubtCount));
        OnPropertyChanged(nameof(RejectedCount));
        OnPropertyChanged(nameof(UnflaggedCount));
        OnPropertyChanged(nameof(UnsavedCount));
        OnPropertyChanged(nameof(GoodCount));
        OnPropertyChanged(nameof(BlurryCount));
        OnPropertyChanged(nameof(UnderexposedCount));
        OnPropertyChanged(nameof(OverexposedCount));
        OnPropertyChanged(nameof(BurstCount));
        OnPropertyChanged(nameof(Star5Count));
        OnPropertyChanged(nameof(Star4Count));
        OnPropertyChanged(nameof(Star3Count));
        OnPropertyChanged(nameof(Star2Count));
        OnPropertyChanged(nameof(Star1Count));

        // Atualiza câmeras disponíveis no ComboBox
        var cameras = _session.AvailableCameras;
        if (cameras.Count > 0)
        {
            var current = SelectedCamera;
            CameraList.Clear();
            CameraList.Add("Todas as Câmeras");
            foreach (var cam in cameras) CameraList.Add(cam);
            if (CameraList.Contains(current)) SelectedCamera = current;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
