using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PhotoPick.Core.Models;

public class PhotoItem : INotifyPropertyChanged
{
    private int _rating;
    private string? _colorLabel;
    private bool _isModified;
    private string? _thumbnailCachePath;
    private bool _isLoadingThumbnail;

    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string DirectoryPath { get; init; }
    public required string Extension { get; init; }
    public long FileSize { get; init; }
    public DateTime? DateTaken { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Orientation { get; set; } = 1;

    public string XmpPath => Path.ChangeExtension(FilePath, ".xmp");
    public bool HasXmp => File.Exists(XmpPath);

    public int Rating
    {
        get => _rating;
        set
        {
            if (_rating != value)
            {
                _rating = Math.Clamp(value, 0, 5);
                OnPropertyChanged();
                OnPropertyChanged(nameof(RatingStars));
                OnPropertyChanged(nameof(IsPicked));
            }
        }
    }

    public string? ColorLabel
    {
        get => _colorLabel;
        set
        {
            if (_colorLabel != value)
            {
                _colorLabel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPicked));
                OnPropertyChanged(nameof(IsRejected));
            }
        }
    }

    public bool IsPicked
    {
        get => _rating >= 1 || string.Equals(_colorLabel, "Green", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                if (_rating == 0) Rating = 1;
                ColorLabel = "Green";
            }
            else
            {
                Rating = 0;
                if (string.Equals(_colorLabel, "Green", StringComparison.OrdinalIgnoreCase))
                {
                    ColorLabel = null;
                }
            }
        }
    }

    public bool IsRejected
    {
        get => string.Equals(_colorLabel, "Red", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                ColorLabel = "Red";
                Rating = 0;
            }
            else if (string.Equals(_colorLabel, "Red", StringComparison.OrdinalIgnoreCase))
            {
                ColorLabel = null;
            }
        }
    }

    public string RatingStars => _rating switch
    {
        1 => "★☆☆☆☆",
        2 => "★★☆☆☆",
        3 => "★★★☆☆",
        4 => "★★★★☆",
        5 => "★★★★★",
        _ => "☆☆☆☆☆"
    };

    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified != value)
            {
                _isModified = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ThumbnailCachePath
    {
        get => _thumbnailCachePath;
        set
        {
            if (_thumbnailCachePath != value)
            {
                _thumbnailCachePath = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLoadingThumbnail
    {
        get => _isLoadingThumbnail;
        set
        {
            if (_isLoadingThumbnail != value)
            {
                _isLoadingThumbnail = value;
                OnPropertyChanged();
            }
        }
    }

    public string FormattedSize
    {
        get
        {
            double mb = FileSize / (1024.0 * 1024.0);
            return $"{mb:F1} MB";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
