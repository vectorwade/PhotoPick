using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using PhotoPick.Desktop.Helpers;

namespace PhotoPick.Desktop.ViewModels;

public class PhotoViewModel : INotifyPropertyChanged
{
    private readonly PhotoItem _model;
    private readonly CullingSession _session;
    private ImageSource? _thumbnail;
    private bool _isLoading;

    public PhotoItem Model => _model;
    public string FilePath => _model.FilePath;
    public string FileName => _model.FileName;
    public string FormattedSize => _model.FormattedSize;
    public string Extension => _model.Extension;
    public int Orientation => _model.Orientation;

    public int Rating
    {
        get => _model.Rating;
        set
        {
            if (_model.Rating != value)
            {
                _session.SetRating(_model, value);
                NotifyRatingAndAuraChanged();
            }
        }
    }

    public string? ColorLabel
    {
        get => _model.ColorLabel;
        set
        {
            if (_model.ColorLabel != value)
            {
                _session.SetColorLabel(_model, value);
                NotifyRatingAndAuraChanged();
            }
        }
    }

    public bool IsPicked
    {
        get => _model.IsPicked;
        set
        {
            if (_model.IsPicked != value)
            {
                _session.TogglePick(_model);
                NotifyRatingAndAuraChanged();
            }
        }
    }

    public bool IsRejected
    {
        get => _model.IsRejected;
        set
        {
            if (_model.IsRejected != value)
            {
                _session.ToggleReject(_model);
                NotifyRatingAndAuraChanged();
            }
        }
    }

    public string RatingStars => _model.RatingStars;
    public bool IsModified => _model.IsModified;

    #region Estrelas Individuais Interativas

    public bool IsStar1Active => Rating >= 1;
    public bool IsStar2Active => Rating >= 2;
    public bool IsStar3Active => Rating >= 3;
    public bool IsStar4Active => Rating >= 4;
    public bool IsStar5Active => Rating >= 5;

    public Brush Star1Brush => IsStar1Active ? BrushesGold : BrushesInactiveStar;
    public Brush Star2Brush => IsStar2Active ? BrushesGold : BrushesInactiveStar;
    public Brush Star3Brush => IsStar3Active ? BrushesGold : BrushesInactiveStar;
    public Brush Star4Brush => IsStar4Active ? BrushesGold : BrushesInactiveStar;
    public Brush Star5Brush => IsStar5Active ? BrushesGold : BrushesInactiveStar;

    private static readonly SolidColorBrush BrushesGold = new(Color.FromRgb(243, 156, 18));
    private static readonly SolidColorBrush BrushesInactiveStar = new(Color.FromRgb(70, 70, 70));

    public void ClickStar(int starNumber)
    {
        // Se já está na mesma estrela, zera. Senão, atribui a estrela clicada
        Rating = Rating == starNumber ? 0 : starNumber;
    }

    #endregion

    #region Aura Visual (Verde para Pick, Vermelho para Reject)

    public Brush CardBorderBrush
    {
        get
        {
            if (IsPicked) return new SolidColorBrush(Color.FromRgb(46, 204, 113));     // Verde esmeralda
            if (IsRejected) return new SolidColorBrush(Color.FromRgb(231, 76, 60));   // Vermelho coral
            if (Rating > 0) return new SolidColorBrush(Color.FromRgb(243, 156, 18));   // Dourado
            return new SolidColorBrush(Color.FromRgb(38, 38, 38));                    // Neutro escuro
        }
    }

    public double CardBorderThickness => (IsPicked || IsRejected) ? 2.5 : 1.5;

    public Effect? AuraEffect
    {
        get
        {
            if (IsPicked)
            {
                return new DropShadowEffect
                {
                    Color = Color.FromRgb(46, 204, 113),
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
            }
            if (IsRejected)
            {
                return new DropShadowEffect
                {
                    Color = Color.FromRgb(231, 76, 60),
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
            }
            return null;
        }
    }

    public Brush StatusBadgeColor
    {
        get
        {
            if (IsPicked) return new SolidColorBrush(Color.FromRgb(46, 204, 113));
            if (IsRejected) return new SolidColorBrush(Color.FromRgb(231, 76, 60));
            if (Rating > 0) return new SolidColorBrush(Color.FromRgb(243, 156, 18));
            return new SolidColorBrush(Color.FromRgb(80, 80, 80));
        }
    }

    public Brush LabelBrush => _model.ColorLabel?.ToLowerInvariant() switch
    {
        "green" => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
        "red" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
        "yellow" => new SolidColorBrush(Color.FromRgb(241, 196, 15)),
        "blue" => new SolidColorBrush(Color.FromRgb(52, 152, 219)),
        "purple" => new SolidColorBrush(Color.FromRgb(155, 89, 182)),
        _ => Brushes.Transparent
    };

    #endregion

    #region Thumbnail

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail != value)
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLoadingThumbnail
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region Qualidade e Rajadas

    public bool IsBlurry => _model.IsBlurry;
    public bool IsUnderexposed => _model.IsUnderexposed;
    public bool IsOverexposed => _model.IsOverexposed;
    public bool HasDefect => _model.HasDefect;
    public bool IsGoodQuality => _model.IsGoodQuality;
    public string? QualityDefectLabel => _model.QualityDefectLabel;

    public bool IsInBurst => _model.IsInBurst;
    public bool IsBurstLead => _model.IsBurstLead;
    public string BurstBadgeText => $"⚡ Rajada ({_model.BurstIndex}/{_model.BurstTotal})";
    public string? CameraModel => _model.CameraModel;
    public bool? FlashFired => _model.FlashFired;

    private static readonly SolidColorBrush BrushBlurry = new(Color.FromRgb(155, 89, 182));
    private static readonly SolidColorBrush BrushUnderexposed = new(Color.FromRgb(52, 73, 94));
    private static readonly SolidColorBrush BrushOverexposed = new(Color.FromRgb(230, 126, 34));
    private static readonly SolidColorBrush BrushBurst = new(Color.FromRgb(41, 128, 185));

    public Brush QualityBadgeBrush
    {
        get
        {
            if (IsBlurry) return BrushBlurry;
            if (IsUnderexposed) return BrushUnderexposed;
            if (IsOverexposed) return BrushOverexposed;
            return Brushes.Transparent;
        }
    }

    public Brush BurstBadgeBrush => BrushBurst;

    #endregion

    public PhotoViewModel(PhotoItem model, CullingSession session)
    {
        _model = model;
        _session = session;
        _model.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName is nameof(PhotoItem.Rating) or nameof(PhotoItem.ColorLabel))
            {
                NotifyRatingAndAuraChanged();
            }
            else if (e.PropertyName is nameof(PhotoItem.IsBlurry) or nameof(PhotoItem.IsUnderexposed) or nameof(PhotoItem.IsOverexposed))
            {
                OnPropertyChanged(nameof(IsBlurry));
                OnPropertyChanged(nameof(IsUnderexposed));
                OnPropertyChanged(nameof(IsOverexposed));
                OnPropertyChanged(nameof(HasDefect));
                OnPropertyChanged(nameof(IsGoodQuality));
                OnPropertyChanged(nameof(QualityDefectLabel));
                OnPropertyChanged(nameof(QualityBadgeBrush));
            }
            else if (e.PropertyName is nameof(PhotoItem.BurstGroupId) or nameof(PhotoItem.BurstTotal))
            {
                OnPropertyChanged(nameof(IsInBurst));
                OnPropertyChanged(nameof(IsBurstLead));
                OnPropertyChanged(nameof(BurstBadgeText));
            }
        };
    }

    public async Task EnsureThumbnailLoadedAsync()
    {
        if (_thumbnail != null || _isLoading) return;
        _isLoading = true;
        OnPropertyChanged(nameof(IsLoadingThumbnail));

        try
        {
            string? thumbPath = await _session.EnsureThumbnailAsync(_model);
            if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
            {
                // Carrega decodificando a 320px para não consumir RAM
                var bmp = ImageHelper.LoadBitmapFromFile(thumbPath, _model.Orientation, decodePixelWidth: 320);
                Thumbnail = bmp;
            }
        }
        catch { }
        finally
        {
            _isLoading = false;
            OnPropertyChanged(nameof(IsLoadingThumbnail));
        }
    }

    public void TogglePick()
    {
        IsPicked = !IsPicked;
    }

    public void ToggleReject()
    {
        IsRejected = !IsRejected;
    }

    public void ClearMarks()
    {
        Rating = 0;
        ColorLabel = null;
    }

    private void NotifyRatingAndAuraChanged()
    {
        OnPropertyChanged(nameof(Rating));
        OnPropertyChanged(nameof(RatingStars));
        OnPropertyChanged(nameof(ColorLabel));
        OnPropertyChanged(nameof(IsPicked));
        OnPropertyChanged(nameof(IsRejected));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
        OnPropertyChanged(nameof(AuraEffect));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(LabelBrush));
        OnPropertyChanged(nameof(IsStar1Active));
        OnPropertyChanged(nameof(IsStar2Active));
        OnPropertyChanged(nameof(IsStar3Active));
        OnPropertyChanged(nameof(IsStar4Active));
        OnPropertyChanged(nameof(IsStar5Active));
        OnPropertyChanged(nameof(Star1Brush));
        OnPropertyChanged(nameof(Star2Brush));
        OnPropertyChanged(nameof(Star3Brush));
        OnPropertyChanged(nameof(Star4Brush));
        OnPropertyChanged(nameof(Star5Brush));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
