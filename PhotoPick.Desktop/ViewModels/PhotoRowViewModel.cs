using System.Collections.ObjectModel;

namespace PhotoPick.Desktop.ViewModels;

public class PhotoRowViewModel
{
    public ObservableCollection<PhotoViewModel> Columns { get; } = [];
    public ObservableCollection<PhotoViewModel> Items => Columns;
    public ObservableCollection<PhotoViewModel> Photos => Columns;

    public PhotoRowViewModel() { }

    public PhotoRowViewModel(params PhotoViewModel[] photos)
    {
        foreach (var p in photos)
        {
            Columns.Add(p);
        }
    }
}
