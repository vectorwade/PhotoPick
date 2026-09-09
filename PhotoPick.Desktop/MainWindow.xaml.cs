using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    #region Abertura de Pasta e Arquivos

    private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenFolderDialogAsync();
    }

    private async void BtnOpenPhotos_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenPhotosDialogAsync();
    }

    private void BtnToggleView_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleViewMode();
    }

    private async void BtnSyncLightroom_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SyncAndOpenLightroomAsync();
    }

    private async void BtnExportSelected_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportSelectedToFolderAsync();
    }

    private async void ModalBtnSubfolder_Click(object sender, MouseButtonEventArgs e)
    {
        await ViewModel.SendToLightroomViaSubfolderAsync();
    }

    private async void ModalBtnInPlace_Click(object sender, MouseButtonEventArgs e)
    {
        await ViewModel.SendToLightroomInPlaceAsync();
    }

    private void ModalBtnCancel_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseLightroomModal();
    }

    #endregion

    #region Filtros e Notas

    private void FilterAll_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.All);
    private void FilterPicked_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.PickedOnly);
    private void FilterUnflagged_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.UnflaggedOnly);
    private void FilterRejected_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.RejectedOnly);

    private void FilterRating5_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.Rating5);
    private void FilterRating4_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.Rating4);
    private void FilterRating3_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.Rating3);
    private void FilterRating2_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.Rating2);
    private void FilterRating1_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.Rating1);

    #endregion

    #region Ações de Clique nos Cards de Foto

    private void PhotoCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PhotoViewModel photo)
        {
            ViewModel.SelectedPhoto = photo;
            if (e.ClickCount == 2)
            {
                ViewModel.IsSingleViewMode = true;
            }
        }
    }

    private void CardBtnPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PhotoViewModel photo)
        {
            photo.TogglePick();
            ViewModel.SelectedPhoto = photo;
        }
    }

    private void CardBtnReject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PhotoViewModel photo)
        {
            photo.ToggleReject();
            ViewModel.SelectedPhoto = photo;
        }
    }

    private void CardBtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PhotoViewModel photo)
        {
            photo.ClearMarks();
            ViewModel.SelectedPhoto = photo;
        }
    }

    private void CardStar1_Click(object sender, RoutedEventArgs e) => ClickCardStar(sender, 1);
    private void CardStar2_Click(object sender, RoutedEventArgs e) => ClickCardStar(sender, 2);
    private void CardStar3_Click(object sender, RoutedEventArgs e) => ClickCardStar(sender, 3);
    private void CardStar4_Click(object sender, RoutedEventArgs e) => ClickCardStar(sender, 4);
    private void CardStar5_Click(object sender, RoutedEventArgs e) => ClickCardStar(sender, 5);

    private void ClickCardStar(object sender, int star)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PhotoViewModel photo)
        {
            photo.ClickStar(star);
            ViewModel.SelectedPhoto = photo;
        }
    }

    #endregion

    #region Ações no Modo Loupe (Foto Única)

    private void LoupePick_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TogglePickSelected();
    }

    private void LoupeReject_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleRejectSelected();
    }

    private void LoupeClear_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearSelected();
    }

    private void LoupeStar1_Click(object sender, RoutedEventArgs e) => ViewModel.RateSelected(1);
    private void LoupeStar2_Click(object sender, RoutedEventArgs e) => ViewModel.RateSelected(2);
    private void LoupeStar3_Click(object sender, RoutedEventArgs e) => ViewModel.RateSelected(3);
    private void LoupeStar4_Click(object sender, RoutedEventArgs e) => ViewModel.RateSelected(4);
    private void LoupeStar5_Click(object sender, RoutedEventArgs e) => ViewModel.RateSelected(5);

    private void BtnPrevious_Click(object sender, RoutedEventArgs e) => ViewModel.PreviousPhoto();
    private void BtnNext_Click(object sender, RoutedEventArgs e) => ViewModel.NextPhoto();

    #endregion

    #region Drag and Drop

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

    #endregion

    #region Atalhos de Teclado Globais

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
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
                await ViewModel.SyncAndOpenLightroomAsync();
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
                ViewModel.ClearSelected();
                break;

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

    #endregion
}