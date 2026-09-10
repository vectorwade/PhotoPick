using System;

namespace PhotoPick.Desktop.ViewModels;

public class FileFormatItemViewModel
{
    public string Extension { get; }
    public string DisplayName { get; }
    public string Icon { get; }
    public int Count { get; set; }
    public bool IsSelected { get; set; }

    public FileFormatItemViewModel(string extension, string displayName, string icon, int count, bool isSelected = false)
    {
        Extension = extension;
        DisplayName = displayName;
        Icon = icon;
        Count = count;
        IsSelected = isSelected;
    }
}
