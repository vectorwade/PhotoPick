using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(RatingStars));
                OnPropertyChanged(nameof(IsPicked));
                OnPropertyChanged(nameof(StatusBadgeColor));
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPicked));
                OnPropertyChanged(nameof(IsRejected));
                OnPropertyChanged(nameof(LabelBrush));
                OnPropertyChanged(nameof(StatusBadgeColor));
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(Rating));
                OnPropertyChanged(nameof(ColorLabel));
                OnPropertyChanged(nameof(RatingStars));
                OnPropertyChanged(nameof(StatusBadgeColor));
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorLabel));
                OnPropertyChanged(nameof(StatusBadgeColor));
            }
        }
    }

    public string RatingStars => _model.RatingStars;
    public bool IsModified => _model.IsModified;

    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnail == null && !_isLoading)
            {
                _ = LoadThumbnailAsync();
            }
            return _thumbnail;
        }
        private set
        {
            if (_thumbnail != value)
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
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

    public Brush StatusBadgeColor
    {
        get
        {
            if (IsPicked) return new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Verde
            if (IsRejected) return new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Vermelho
            if (Rating > 0) return new SolidColorBrush(Color.FromRgb(243, 156, 18)); // Laranja/Ouro
            return new SolidColorBrush(Color.FromRgb(100, 100, 100)); // Neutro
        }
    }

    public PhotoViewModel(PhotoItem model, CullingSession session)
    {
        _model = model;
        _session = session;
        _model.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(PhotoItem.ThumbnailCachePath))
            {
                _ = LoadThumbnailAsync();
            }
        };
    }

    public async Task LoadThumbnailAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            string? thumbPath = await _session.EnsureThumbnailAsync(_model);
            if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
            {
                // Carrega em resolução reduzida (320px) para máxima performance de memória na grade
                var bmp = ImageHelper.LoadBitmapFromFile(thumbPath, _model.Orientation, decodePixelWidth: 360);
                Thumbnail = bmp;
            }
        }
        catch { }
        finally
        {
            _isLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
