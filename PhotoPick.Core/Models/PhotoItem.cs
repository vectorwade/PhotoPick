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
    private DateTime? _dateTaken;
    private int _width;
    private int _height;
    private int _orientation = 1;

    public DateTime? DateTaken
    {
        get => _dateTaken;
        set { if (_dateTaken != value) { _dateTaken = value; OnPropertyChanged(); } }
    }

    public int Width
    {
        get => _width;
        set { if (_width != value) { _width = value; OnPropertyChanged(); } }
    }

    public int Height
    {
        get => _height;
        set { if (_height != value) { _height = value; OnPropertyChanged(); } }
    }

    public int Orientation
    {
        get => _orientation;
        set { if (_orientation != value) { _orientation = value; OnPropertyChanged(); } }
    }

    // Metadados EXIF
    private string? _cameraModel;
    private string? _cameraMake;
    private bool? _flashFired;
    private string? _lensModel;
    private int? _iso;
    private double? _fNumber;
    private double? _exposureTime;
    private double? _focalLength;
    private double? _exposureBias;

    public string? CameraModel
    {
        get => _cameraModel;
        set { if (_cameraModel != value) { _cameraModel = value; OnPropertyChanged(); } }
    }

    public string? CameraMake
    {
        get => _cameraMake;
        set { if (_cameraMake != value) { _cameraMake = value; OnPropertyChanged(); } }
    }

    public bool? FlashFired
    {
        get => _flashFired;
        set { if (_flashFired != value) { _flashFired = value; OnPropertyChanged(); } }
    }

    public string? LensModel
    {
        get => _lensModel;
        set { if (_lensModel != value) { _lensModel = value; OnPropertyChanged(); } }
    }

    public int? Iso
    {
        get => _iso;
        set { if (_iso != value) { _iso = value; OnPropertyChanged(); } }
    }

    public double? FNumber
    {
        get => _fNumber;
        set { if (_fNumber != value) { _fNumber = value; OnPropertyChanged(); } }
    }

    public double? ExposureTime
    {
        get => _exposureTime;
        set { if (_exposureTime != value) { _exposureTime = value; OnPropertyChanged(); } }
    }

    public double? FocalLength
    {
        get => _focalLength;
        set { if (_focalLength != value) { _focalLength = value; OnPropertyChanged(); } }
    }

    public double? ExposureBias
    {
        get => _exposureBias;
        set { if (_exposureBias != value) { _exposureBias = value; OnPropertyChanged(); } }
    }

    // Análise Inteligente de Qualidade
    private bool _isBlurry;
    private bool _isUnderexposed;
    private bool _isOverexposed;
    private double _sharpnessScore;
    private double _brightnessScore;

    public bool IsBlurry
    {
        get => _isBlurry;
        set
        {
            if (_isBlurry != value)
            {
                _isBlurry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDefect));
                OnPropertyChanged(nameof(IsGoodQuality));
                OnPropertyChanged(nameof(QualityDefectLabel));
            }
        }
    }

    public bool IsUnderexposed
    {
        get => _isUnderexposed;
        set
        {
            if (_isUnderexposed != value)
            {
                _isUnderexposed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDefect));
                OnPropertyChanged(nameof(IsGoodQuality));
                OnPropertyChanged(nameof(QualityDefectLabel));
            }
        }
    }

    public bool IsOverexposed
    {
        get => _isOverexposed;
        set
        {
            if (_isOverexposed != value)
            {
                _isOverexposed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDefect));
                OnPropertyChanged(nameof(IsGoodQuality));
                OnPropertyChanged(nameof(QualityDefectLabel));
            }
        }
    }

    public double SharpnessScore
    {
        get => _sharpnessScore;
        set
        {
            if (Math.Abs(_sharpnessScore - value) > 0.001)
            {
                _sharpnessScore = value;
                OnPropertyChanged();
            }
        }
    }

    public double BrightnessScore
    {
        get => _brightnessScore;
        set
        {
            if (Math.Abs(_brightnessScore - value) > 0.001)
            {
                _brightnessScore = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasDefect => _isBlurry || _isUnderexposed || _isOverexposed;
    public bool IsGoodQuality => !HasDefect;

    public string? QualityDefectLabel
    {
        get
        {
            if (_isBlurry) return "🌫️ Embaçada";
            if (_isUnderexposed) return "🌑 Escura";
            if (_isOverexposed) return "☀️ Estourada";
            return null;
        }
    }

    // Stacking de Rajadas (Burst Mode)
    private string? _burstGroupId;
    private int _burstIndex = 1;
    private int _burstTotal = 1;
    private bool _isBurstLead;

    public string? BurstGroupId
    {
        get => _burstGroupId;
        set
        {
            if (_burstGroupId != value)
            {
                _burstGroupId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInBurst));
            }
        }
    }

    public int BurstIndex
    {
        get => _burstIndex;
        set
        {
            if (_burstIndex != value)
            {
                _burstIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public int BurstTotal
    {
        get => _burstTotal;
        set
        {
            if (_burstTotal != value)
            {
                _burstTotal = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInBurst));
            }
        }
    }

    public bool IsBurstLead
    {
        get => _isBurstLead;
        set
        {
            if (_isBurstLead != value)
            {
                _isBurstLead = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsInBurst => _burstTotal > 1;

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
                OnPropertyChanged(nameof(IsDoubt));
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

    public bool IsDoubt
    {
        get => string.Equals(_colorLabel, "Yellow", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                ColorLabel = "Yellow";
                Rating = 0;
            }
            else if (string.Equals(_colorLabel, "Yellow", StringComparison.OrdinalIgnoreCase))
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
