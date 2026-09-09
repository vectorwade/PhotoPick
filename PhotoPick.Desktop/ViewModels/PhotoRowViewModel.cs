using System.Collections.ObjectModel;

namespace PhotoPick.Desktop.ViewModels;

public class PhotoRowViewModel
{
    public ObservableCollection<PhotoViewModel> Columns { get; } = [];

    public PhotoRowViewModel() { }

    public PhotoRowViewModel(params PhotoViewModel[] photos)
    {
        foreach (var p in photos)
        {
            Columns.Add(p);
        }
    }
}
