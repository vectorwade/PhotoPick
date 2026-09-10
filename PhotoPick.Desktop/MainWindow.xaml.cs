using PhotoPick.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PhotoPick.Core.Models;
using PhotoPick.Desktop.Services;
using PhotoPick.Desktop.ViewModels;

namespace PhotoPick.Desktop;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel => (MainViewModel)DataContext;
    private readonly GamepadService _gamepadService;

    public MainWindow()
    {
        InitializeComponent();

        // Inicializa o serviço de Gamepad (Joysticks XInput)
        _gamepadService = new GamepadService();
        _gamepadService.ActionTriggered += action => ViewModel.HandleGamepadAction(action);
        _gamepadService.ConnectionChanged += connected => ViewModel.IsGamepadConnected = connected;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _gamepadService.Start();
        if (DataContext is MainViewModel vm) vm.UpdateColumnsForWidth(GridScrollViewer?.ActualWidth ?? ActualWidth - 560);
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _gamepadService.Dispose();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Área disponível para a grade = largura total menos os dois painéis laterais (250 + 300 = 550)
        double available = (GridScrollViewer?.ActualWidth > 100)
            ? GridScrollViewer.ActualWidth
            : Math.Max(300, ActualWidth - 560);
        if (DataContext is MainViewModel vm) vm.UpdateColumnsForWidth(available);
    }

    #region Window Chrome (Min, Max, Close, Home)

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LogoHome_Click(object sender, RoutedEventArgs e)
    {
        // Alterna ou reabre a tela de boas-vindas / Novo Projeto
        ViewModel.IsWelcomeScreenVisible = !ViewModel.IsWelcomeScreenVisible;
    }

    #endregion

    #region Toolbar Ações

    private void BtnViewMode_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleViewMode();
    private void BtnAutoAdvance_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleAutoAdvance();
    private async void BtnLightroom_Click(object sender, RoutedEventArgs e) => await ViewModel.SyncAndOpenLightroomAsync();
    private async void BtnExport_Click(object sender, RoutedEventArgs e) => await ViewModel.ExportSelectedToFolderAsync();
    private void BtnHelp_Click(object sender, RoutedEventArgs e) => ViewModel.OpenHelpModal();
    private void BtnCloseHelp_Click(object sender, RoutedEventArgs e) => ViewModel.CloseHelpModal();

    #endregion

    #region Navegação Esquerda & Abertura de Pasta

    private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        await PromptOpenFolderAsync();
    }

    private async void BtnWelcomeOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        await PromptOpenFolderAsync();
    }

    private void BtnWelcomeReturn_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsWelcomeScreenVisible = false;
    }

    private async Task PromptOpenFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Mavi Select — Selecionar Pasta de Fotos RAW",
            InitialDirectory = ViewModel.Session.CurrentDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            await ViewModel.LoadDirectoryAsync(dialog.FolderName);
            ViewModel.IsWelcomeScreenVisible = false;
        }
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.All);
    private void FilterPicked_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.PickedOnly);
    private void FilterRejected_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.RejectedOnly);
    private void FilterUnflagged_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.UnflaggedOnly);

    #endregion

    #region Interações no Card de Fotos (Grade)

    private void PhotoCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoViewModel photo })
        {
            ViewModel.SelectedPhoto = photo;

            if (e.ClickCount == 2)
            {
                ViewModel.ToggleViewMode();
            }
        }
    }

    private void CardBtnPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoViewModel photo })
        {
            ViewModel.SelectedPhoto = photo;
            ViewModel.TogglePickSelected();
        }
    }

    private void CardBtnReject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoViewModel photo })
        {
            ViewModel.SelectedPhoto = photo;
            ViewModel.ToggleRejectSelected();
        }
    }

    private void Star1_Click(object sender, MouseButtonEventArgs e) => HandleCardStarClick(sender, 1);
    private void Star2_Click(object sender, MouseButtonEventArgs e) => HandleCardStarClick(sender, 2);
    private void Star3_Click(object sender, MouseButtonEventArgs e) => HandleCardStarClick(sender, 3);
    private void Star4_Click(object sender, MouseButtonEventArgs e) => HandleCardStarClick(sender, 4);
    private void Star5_Click(object sender, MouseButtonEventArgs e) => HandleCardStarClick(sender, 5);

    private void HandleCardStarClick(object sender, int star)
    {
        if (sender is FrameworkElement { DataContext: PhotoViewModel photo })
        {
            ViewModel.SelectedPhoto = photo;
            ViewModel.RateSelected(star);
        }
    }

    #endregion

    #region Modo Loupe / Foto Única

    private void LoupeImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ViewModel.ToggleViewMode();
        }
    }

    private void BtnFocusPeaking_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFocusPeaking();
    private void BtnPickBurstBest_Click(object sender, RoutedEventArgs e) => ViewModel.PickCurrentBurstBest();
    private void BtnLoupePick_Click(object sender, RoutedEventArgs e) => ViewModel.TogglePickSelected();
    private void BtnLoupeReject_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleRejectSelected();
    private void BtnLoupeClear_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelected();

    #endregion

    #region Painel Direito: Inspetor Ações

    private void InspectorStar1_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(1);
    private void InspectorStar2_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(2);
    private void InspectorStar3_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(3);
    private void InspectorStar4_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(4);
    private void InspectorStar5_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(5);

    private void BtnInspectorPick_Click(object sender, RoutedEventArgs e) => ViewModel.TogglePickSelected();
    private void BtnInspectorReject_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleRejectSelected();
    private void BtnInspectorClear_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelected();

    #endregion

    #region Atalhos de Teclado

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.IsHelpModalOpen)
        {
            if (e.Key is Key.Escape or Key.F1 or Key.H)
            {
                ViewModel.CloseHelpModal();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.F1 or Key.H:
                ViewModel.OpenHelpModal();
                e.Handled = true;
                break;

            case Key.P:
                ViewModel.TogglePickSelected();
                e.Handled = true;
                break;

            case Key.X:
                ViewModel.ToggleRejectSelected();
                e.Handled = true;
                break;

            case Key.U:
                ViewModel.ClearSelected();
                e.Handled = true;
                break;

            case Key.F:
                ViewModel.ToggleFocusPeaking();
                e.Handled = true;
                break;

            case Key.D1 or Key.NumPad1: ViewModel.RateSelected(1); e.Handled = true; break;
            case Key.D2 or Key.NumPad2: ViewModel.RateSelected(2); e.Handled = true; break;
            case Key.D3 or Key.NumPad3: ViewModel.RateSelected(3); e.Handled = true; break;
            case Key.D4 or Key.NumPad4: ViewModel.RateSelected(4); e.Handled = true; break;
            case Key.D5 or Key.NumPad5: ViewModel.RateSelected(5); e.Handled = true; break;
            case Key.D0 or Key.NumPad0: ViewModel.RateSelected(0); e.Handled = true; break;

            case Key.Right or Key.D:
                ViewModel.NextPhoto();
                e.Handled = true;
                break;

            case Key.Left or Key.A:
                ViewModel.PreviousPhoto();
                e.Handled = true;
                break;

            case Key.Up or Key.W:
                ViewModel.NavigateGridUp();
                e.Handled = true;
                break;

            case Key.Down or Key.S when !Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                ViewModel.NavigateGridDown();
                e.Handled = true;
                break;

            case Key.Space or Key.Enter:
                ViewModel.ToggleViewMode();
                e.Handled = true;
                break;

            case Key.O when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                await PromptOpenFolderAsync();
                e.Handled = true;
                break;

            case Key.S when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                await ViewModel.SyncAndOpenLightroomAsync();
                e.Handled = true;
                break;
        }
    }

    #endregion

    #region Drag and Drop de Pasta

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (paths == null || paths.Length == 0) return;

        string target = paths[0];
        if (File.Exists(target))
        {
            target = Path.GetDirectoryName(target) ?? target;
        }

        if (Directory.Exists(target))
        {
            await ViewModel.LoadDirectoryAsync(target);
            ViewModel.IsWelcomeScreenVisible = false;
        }
    }

    #endregion
}
