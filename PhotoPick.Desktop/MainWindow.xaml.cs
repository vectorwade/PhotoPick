using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PhotoPick.Core.Services;
using PhotoPick.Desktop.ViewModels;

namespace PhotoPick.Desktop;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenFolderDialogAsync();
    }

    private void BtnToggleView_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleViewMode();
    }

    private async void BtnSaveXmp_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveXmpAsync();
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.All);
    private void FilterPicked_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.PickedOnly);
    private void FilterUnflagged_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.UnflaggedOnly);
    private void FilterRejected_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.RejectedOnly);
    private void FilterRated_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.RatedOnly);

    private void PhotoListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedPhoto != null)
        {
            ViewModel.IsSingleViewMode = true;
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                string targetDir = Directory.Exists(files[0]) ? files[0] : Path.GetDirectoryName(files[0])!;
                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    await ViewModel.LoadDirectoryAsync(targetDir);
                }
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Atalhos com Ctrl
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.O)
            {
                e.Handled = true;
                await ViewModel.OpenFolderDialogAsync();
                return;
            }
            if (e.Key == Key.S)
            {
                e.Handled = true;
                await ViewModel.SaveXmpAsync();
                return;
            }
        }

        switch (e.Key)
        {
            case Key.Space:
            case Key.Enter:
                e.Handled = true;
                ViewModel.ToggleViewMode();
                break;

            case Key.P:
                e.Handled = true;
                ViewModel.TogglePickSelected();
                break;

            case Key.X:
                e.Handled = true;
                ViewModel.ToggleRejectSelected();
                break;

            case Key.U:
                e.Handled = true;
                ViewModel.RateSelected(0);
                ViewModel.SetColorSelected(null);
                break;

            // Rating de estrelas (1-5)
            case Key.D1 or Key.NumPad1:
                e.Handled = true;
                ViewModel.RateSelected(1);
                break;
            case Key.D2 or Key.NumPad2:
                e.Handled = true;
                ViewModel.RateSelected(2);
                break;
            case Key.D3 or Key.NumPad3:
                e.Handled = true;
                ViewModel.RateSelected(3);
                break;
            case Key.D4 or Key.NumPad4:
                e.Handled = true;
                ViewModel.RateSelected(4);
                break;
            case Key.D5 or Key.NumPad5:
                e.Handled = true;
                ViewModel.RateSelected(5);
                break;
            case Key.D0 or Key.NumPad0:
                e.Handled = true;
                ViewModel.RateSelected(0);
                break;

            // Rótulos de Cor
            case Key.D6 or Key.NumPad6:
                e.Handled = true;
                ViewModel.SetColorSelected("Red");
                break;
            case Key.D7 or Key.NumPad7:
                e.Handled = true;
                ViewModel.SetColorSelected("Yellow");
                break;
            case Key.D8 or Key.NumPad8:
                e.Handled = true;
                ViewModel.SetColorSelected("Green");
                break;
            case Key.D9 or Key.NumPad9:
                e.Handled = true;
                ViewModel.SetColorSelected("Blue");
                break;

            // Navegação
            case Key.Right:
            case Key.D:
            case Key.K:
                e.Handled = true;
                ViewModel.NextPhoto();
                break;

            case Key.Left:
            case Key.A:
            case Key.J:
                e.Handled = true;
                ViewModel.PreviousPhoto();
                break;

            case Key.Escape:
                if (ViewModel.IsSingleViewMode)
                {
                    e.Handled = true;
                    ViewModel.IsSingleViewMode = false;
                }
                break;
        }
    }
}