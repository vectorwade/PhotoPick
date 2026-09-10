using PhotoPick.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using PhotoPick.Core.Models;
using PhotoPick.Desktop.Services;
using PhotoPick.Desktop.ViewModels;

namespace PhotoPick.Desktop;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainViewModel ViewModel => (MainViewModel)DataContext;
    private readonly GamepadService _gamepadService;
    private bool _isPanning;
    private Point _panStartPoint;
    private Point _panClickOrigin;
    private Point _panStartTranslate;
    private bool _isLoupeZoomed;

    public MainWindow()
    {
        InitializeComponent();

        // Inicializa o serviço de Gamepad (Joysticks XInput)
        _gamepadService = new GamepadService();
        _gamepadService.ActionTriggered += action => ViewModel.HandleGamepadAction(action);
        _gamepadService.ConnectionChanged += connected => ViewModel.IsGamepadConnected = connected;

        ViewModel.PhotoSelected += OnPhotoSelected;
    }

    private void OnPhotoSelected(PhotoViewModel photo)
    {
        if (ViewModel == null || ViewModel.IsLoadingFolder) return;
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (ViewModel.Rows.Count > 0 && GridScrollViewer != null)
                {
                    var row = ViewModel.Rows.FirstOrDefault(r => r.Columns.Contains(photo));
                    if (row != null)
                    {
                        GridScrollViewer.ScrollIntoView(row);
                    }
                }
            }
            catch { }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _gamepadService.Start();
        if (DataContext is MainViewModel vm) vm.UpdateColumnsForWidth(GridScrollViewer?.ActualWidth ?? ActualWidth - 560);

        try
        {
            var helper = new WindowInteropHelper(this);
            int preference = 2; // DWMWCP_ROUND (cantos arredondados nativos no Windows 11)
            DwmSetWindowAttribute(helper.Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
        catch { }

        UpdateWindowCorners();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateWindowCorners();
    }

    private void UpdateWindowCorners()
    {
        if (MainWindowRootBorder == null) return;
        if (WindowState == WindowState.Maximized)
        {
            MainWindowRootBorder.CornerRadius = new CornerRadius(0);
            MainWindowRootBorder.BorderThickness = new Thickness(0);
        }
        else
        {
            MainWindowRootBorder.CornerRadius = new CornerRadius(8);
            MainWindowRootBorder.BorderThickness = new Thickness(1);
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _gamepadService.Dispose();
    }

    private double _lastAvailableWidth = 0;
    private bool _isUpdatingColumns = false;

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isUpdatingColumns) return;
        if (DataContext is not MainViewModel vm) return;

        double available = (GridScrollViewer?.ActualWidth > 100)
            ? GridScrollViewer.ActualWidth
            : Math.Max(300, ActualWidth - 560);

        if (Math.Abs(available - _lastAvailableWidth) < 20) return;
        _lastAvailableWidth = available;

        try
        {
            _isUpdatingColumns = true;
            vm.UpdateColumnsForWidth(available);
        }
        catch { }
        finally
        {
            _isUpdatingColumns = false;
        }
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
        if (ViewModel == null) return;

        // Se houver fotos carregadas e a tela de boas-vindas estiver visível, fecha e volta ao workspace ativo
        if (ViewModel.Photos.Count > 0 && ViewModel.IsWelcomeScreenVisible)
        {
            ViewModel.IsWelcomeScreenVisible = false;
            if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
        }
        else if (ViewModel.Photos.Count > 0 && !ViewModel.IsWelcomeScreenVisible)
        {
            ViewModel.IsWelcomeScreenVisible = true;
            if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Visible;
        }
        else
        {
            ViewModel.IsWelcomeScreenVisible = true;
            if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Visible;
        }
    }

    #endregion

    #region Toolbar Ações

    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleTheme();
    }

    private void FormatItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileFormatItemViewModel item })
        {
            ViewModel.FilterByFormat(item.Extension);
        }
    }

    private void BtnGamepadMode_Click(object sender, RoutedEventArgs e)
    {
        if (GamepadFlyoutPopup != null) GamepadFlyoutPopup.IsOpen = !GamepadFlyoutPopup.IsOpen;
    }

    private void BtnViewMode_Click(object sender, RoutedEventArgs e) { ResetLoupeZoom(); ViewModel.ToggleViewMode(); }
    private void BtnAutoAdvance_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleAutoAdvance();
    private async void BtnLightroom_Click(object sender, RoutedEventArgs e) => await ViewModel.SyncAndOpenLightroomAsync();
    private async void BtnExport_Click(object sender, RoutedEventArgs e) => await ViewModel.ExportSelectedToFolderAsync();
    private void BtnHelp_Click(object sender, RoutedEventArgs e) => ViewModel.OpenHelpModal();
    private void BtnCloseHelp_Click(object sender, RoutedEventArgs e) => ViewModel.CloseHelpModal();
    private void HelpModalBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ViewModel.CloseHelpModal();
    private void HelpModalCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void InspectorCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.OpenMetadataModal();
    }

    private void MetadataModalBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.CloseMetadataModal();
    }

    private void MetadataModalCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void BtnCloseMetadataModal_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseMetadataModal();
    }

    private void BtnCopyAllMetadata_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CopyAllMetadataToClipboard();
    }

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
        if (ViewModel != null) ViewModel.IsWelcomeScreenVisible = false;
        if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
    }

    private void WelcomeOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && ViewModel.Photos.Count > 0)
        {
            ViewModel.IsWelcomeScreenVisible = false;
            if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void WelcomeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private async void BtnWelcomeFolderOnly_Click(object sender, RoutedEventArgs e)
    {
        await PromptSelectFolderOnlyAsync();
    }

    private string GetSafeInitialDirectory()
    {
        try
        {
            string? current = ViewModel?.Session?.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            {
                return current;
            }
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(pictures))
            {
                return pictures;
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }

    private async Task PromptOpenFolderAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Mavi Select — Selecionar Fotos (Miniaturas Visíveis)",
                Filter = "Fotos RAW e Imagens (*.dng;*.cr2;*.cr3;*.arw;*.nef;*.raf;*.jpg;*.jpeg)|*.dng;*.cr2;*.cr3;*.arw;*.nef;*.raf;*.orf;*.pef;*.rw2;*.jpg;*.jpeg;*.png;*.webp|Todos os Arquivos (*.*)|*.*",
                Multiselect = true,
                InitialDirectory = GetSafeInitialDirectory()
            };

            bool? result = dialog.ShowDialog(this);
            if (result == true && dialog.FileNames.Length > 0)
            {
                if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
                if (ViewModel != null) ViewModel.IsWelcomeScreenVisible = false;

                string target = dialog.FileNames[0];
                string? dir = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && ViewModel != null)
                {
                    await ViewModel.LoadDirectoryAsync(dir);

                    string selectedFile = Path.GetFileName(target);
                    var match = ViewModel.Photos.FirstOrDefault(p => string.Equals(p.FileName, selectedFile, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        ViewModel.SelectedPhoto = match;
                    }

                    ViewModel.IsWelcomeScreenVisible = false;
                }

                if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível abrir o seletor de fotos: {ex.Message}",
                "Aviso — Mavi Select",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task PromptSelectFolderOnlyAsync()
    {
        try
        {
            // Seletor de diretório nativo para selecionar a pasta diretamente
            var dialog = new OpenFolderDialog
            {
                Title = "Mavi Select — Selecionar Pasta com Fotos",
                InitialDirectory = GetSafeInitialDirectory(),
                Multiselect = false
            };

            bool? result = dialog.ShowDialog(this);
            if (result == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
                if (ViewModel != null) ViewModel.IsWelcomeScreenVisible = false;

                if (ViewModel != null)
                {
                    await ViewModel.LoadDirectoryAsync(dialog.FolderName);
                    ViewModel.IsWelcomeScreenVisible = false;
                }

                if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível abrir a pasta selecionada: {ex.Message}",
                "Aviso — Mavi Select",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.All);
    private void FilterPicked_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.PickedOnly);
    private void FilterDoubt_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(PhotoFilterMode.DoubtOnly);
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
        this.Focus();
        e.Handled = true;
    }

    private void CardBtnPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoViewModel photo })
        {
            ViewModel.SelectedPhoto = photo;
            ViewModel.TogglePickSelected();
        }
    }

    private void CardBtnDoubt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoViewModel photo })
        {
            ViewModel.SelectedPhoto = photo;
            ViewModel.ToggleDoubtSelected();
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
            ViewModel.RateSelected(star, autoAdvance: false);
        }
    }

    #endregion

    #region Modo Loupe / Foto Única & Zoom Interativo

    public void ToggleLoupeZoom()
    {
        _isLoupeZoomed = !_isLoupeZoomed;
        if (_isLoupeZoomed)
        {
            if (LoupeScaleTransform != null)
            {
                LoupeScaleTransform.ScaleX = 2.5;
                LoupeScaleTransform.ScaleY = 2.5;
            }
            if (LoupeTranslateTransform != null)
            {
                LoupeTranslateTransform.X = 0;
                LoupeTranslateTransform.Y = 0;
            }
            if (LoupeZoomBtnText != null) LoupeZoomBtnText.Text = "🔍 Ajustar";
        }
        else
        {
            ResetLoupeZoom();
        }
    }

    public void ResetLoupeZoom()
    {
        _isLoupeZoomed = false;
        if (LoupeScaleTransform != null)
        {
            LoupeScaleTransform.ScaleX = 1.0;
            LoupeScaleTransform.ScaleY = 1.0;
        }
        if (LoupeTranslateTransform != null)
        {
            LoupeTranslateTransform.X = 0;
            LoupeTranslateTransform.Y = 0;
        }
        if (LoupeZoomBtnText != null) LoupeZoomBtnText.Text = "🔍 100%";
    }

    private void BtnToggleLoupeZoom_Click(object sender, RoutedEventArgs e) => ToggleLoupeZoom();

    private void LoupeContainer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (LoupeScaleTransform == null || LoupeTranslateTransform == null) return;

        Point mouseOnImage = e.GetPosition(LoupeMainImage);
        bool isOverImage = (mouseOnImage.X >= 0 && mouseOnImage.X <= LoupeMainImage.ActualWidth &&
                            mouseOnImage.Y >= 0 && mouseOnImage.Y <= LoupeMainImage.ActualHeight);

        // Se NÃO estiver com zoom OU se o cursor estiver fora da foto (nas barras pretas):
        // Rolar o scroll caminha entre as fotos!
        if (!_isLoupeZoomed || !isOverImage)
        {
            if (e.Delta < 0)
            {
                ResetLoupeZoom();
                ViewModel.NextPhoto();
            }
            else if (e.Delta > 0)
            {
                ResetLoupeZoom();
                ViewModel.PreviousPhoto();
            }
            e.Handled = true;
            return;
        }

        // Se estiver com zoom e com o cursor em cima da foto:
        double factor = e.Delta > 0 ? 1.25 : 0.8;
        double newScale = Math.Clamp(LoupeScaleTransform.ScaleX * factor, 0.8, 6.0);

        if (newScale <= 1.05)
        {
            ResetLoupeZoom();
        }
        else
        {
            _isLoupeZoomed = true;
            LoupeScaleTransform.ScaleX = newScale;
            LoupeScaleTransform.ScaleY = newScale;
            if (LoupeZoomBtnText != null) LoupeZoomBtnText.Text = "🔍 Ajustar";
        }
        e.Handled = true;
    }

    private void LoupeContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Se clicar fora da foto nas barras pretas, tira o zoom imediatamente
        if (_isLoupeZoomed)
        {
            ResetLoupeZoom();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            ViewModel.ToggleViewMode();
            e.Handled = true;
        }
    }

    private void LoupeContainer_MouseMove(object sender, MouseEventArgs e)
    {
    }

    private void LoupeContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
    }

    private void LoupeImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetLoupeZoom();
            ViewModel.ToggleViewMode();
            e.Handled = true;
            return;
        }

        if (!_isLoupeZoomed)
        {
            // Zoom inteligente no ponto clicado pelo mouse
            if (LoupeContainer != null && LoupeScaleTransform != null && LoupeTranslateTransform != null)
            {
                Point clickPos = e.GetPosition(LoupeContainer);
                double containerCenterX = LoupeContainer.ActualWidth / 2.0;
                double containerCenterY = LoupeContainer.ActualHeight / 2.0;
                double dx = clickPos.X - containerCenterX;
                double dy = clickPos.Y - containerCenterY;

                double targetScale = 2.5;
                _isLoupeZoomed = true;
                LoupeScaleTransform.ScaleX = targetScale;
                LoupeScaleTransform.ScaleY = targetScale;
                LoupeTranslateTransform.X = -dx * (targetScale - 1.0);
                LoupeTranslateTransform.Y = -dy * (targetScale - 1.0);
                if (LoupeZoomBtnText != null) LoupeZoomBtnText.Text = "🔍 Ajustar";
            }
            e.Handled = true;
            return;
        }

        // Se já está com zoom: inicia pan
        _isPanning = true;
        _panStartPoint = e.GetPosition(this);
        _panClickOrigin = _panStartPoint;
        _panStartTranslate = new Point(LoupeTranslateTransform?.X ?? 0, LoupeTranslateTransform?.Y ?? 0);
        LoupeMainImage.CaptureMouse();
        e.Handled = true;
    }

    private void LoupeImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && LoupeTranslateTransform != null)
        {
            Point current = e.GetPosition(this);
            LoupeTranslateTransform.X = _panStartTranslate.X + (current.X - _panStartPoint.X);
            LoupeTranslateTransform.Y = _panStartTranslate.Y + (current.Y - _panStartPoint.Y);
            e.Handled = true;
        }
    }

    private void LoupeImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            LoupeMainImage.ReleaseMouseCapture();

            Point upPoint = e.GetPosition(this);
            double dist = Math.Sqrt(Math.Pow(upPoint.X - _panClickOrigin.X, 2) + Math.Pow(upPoint.Y - _panClickOrigin.Y, 2));

            // Se o usuário apenas clicou (sem arrastar), tira o zoom para ajustar à tela
            if (dist < 5.0)
            {
                ResetLoupeZoom();
            }
            e.Handled = true;
        }
    }

    private void BtnFocusPeaking_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFocusPeaking();
    private void BtnPickBurstBest_Click(object sender, RoutedEventArgs e) => ViewModel.PickCurrentBurstBest();
    private void BtnLoupePick_Click(object sender, RoutedEventArgs e) => ViewModel.TogglePickSelected();
    private void BtnLoupeDoubt_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleDoubtSelected();
    private void BtnLoupeReject_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleRejectSelected();
    private void BtnLoupeClear_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelected();
    private async void BtnLoupeDelete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteSelectedPhotoAsync();

    #endregion

    #region Painel Direito: Inspetor Ações

    private void InspectorStar1_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(1, autoAdvance: false);
    private void InspectorStar2_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(2, autoAdvance: false);
    private void InspectorStar3_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(3, autoAdvance: false);
    private void InspectorStar4_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(4, autoAdvance: false);
    private void InspectorStar5_Click(object sender, MouseButtonEventArgs e) => ViewModel.RateSelected(5, autoAdvance: false);

    private void BtnInspectorPick_Click(object sender, RoutedEventArgs e) => ViewModel.TogglePickSelected();
    private void BtnInspectorDoubt_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleDoubtSelected();
    private void BtnInspectorReject_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleRejectSelected();
    private void BtnInspectorClear_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelected();
    private async void BtnInspectorDelete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteSelectedPhotoAsync();

    #endregion

    #region Atalhos de Teclado

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.IsLicenseLocked)
        {
            // O app está travado aguardando chave. Não processa atalhos.
            return;
        }

        if (ViewModel.IsLicenseDialogOpen)
        {
            if (e.Key is Key.Escape)
            {
                ViewModel.CloseLicenseDialog();
                e.Handled = true;
            }
            return;
        }

        if (ViewModel.IsMetadataModalOpen)
        {
            if (e.Key is Key.Escape)
            {
                ViewModel.CloseMetadataModal();
                e.Handled = true;
            }
            return;
        }

        if (ViewModel.IsHelpModalOpen)
        {
            if (e.Key is Key.Escape or Key.F1 or Key.H)
            {
                ViewModel.CloseHelpModal();
                e.Handled = true;
            }
            return;
        }

        if (ViewModel.IsWelcomeScreenVisible && ViewModel.Photos.Count > 0)
        {
            if (e.Key == Key.Escape)
            {
                ViewModel.IsWelcomeScreenVisible = false;
                if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Key.Escape:
                if (ViewModel.IsWelcomeScreenVisible && ViewModel.Photos.Count > 0)
                {
                    ViewModel.IsWelcomeScreenVisible = false;
                    if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                await ViewModel.DeleteSelectedPhotoAsync();
                e.Handled = true;
                break;

            case Key.F1 or Key.H:
                ViewModel.OpenHelpModal();
                e.Handled = true;
                break;

            case Key.P:
                ViewModel.TogglePickSelected();
                e.Handled = true;
                break;

            case Key.D:
                ViewModel.ToggleDoubtSelected();
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

            case Key.Z:
                ToggleLoupeZoom();
                e.Handled = true;
                break;

            case Key.D1 or Key.NumPad1: ViewModel.RateSelected(1, autoAdvance: false); e.Handled = true; break;
            case Key.D2 or Key.NumPad2: ViewModel.RateSelected(2, autoAdvance: false); e.Handled = true; break;
            case Key.D3 or Key.NumPad3: ViewModel.RateSelected(3, autoAdvance: false); e.Handled = true; break;
            case Key.D4 or Key.NumPad4: ViewModel.RateSelected(4, autoAdvance: false); e.Handled = true; break;
            case Key.D5 or Key.NumPad5: ViewModel.RateSelected(5, autoAdvance: false); e.Handled = true; break;
            case Key.D0 or Key.NumPad0: ViewModel.RateSelected(0, autoAdvance: false); e.Handled = true; break;

            case Key.Right:
                ResetLoupeZoom();
                ViewModel.NextPhoto();
                e.Handled = true;
                break;

            case Key.Left:
                ResetLoupeZoom();
                ViewModel.PreviousPhoto();
                e.Handled = true;
                break;

            case Key.Up:
                ResetLoupeZoom();
                ViewModel.NavigateGridUp();
                e.Handled = true;
                break;

            case Key.Down when !Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                ResetLoupeZoom();
                ViewModel.NavigateGridDown();
                e.Handled = true;
                break;

            case Key.Space or Key.Enter:
                ResetLoupeZoom();
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
        try
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
                if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
                if (ViewModel != null) ViewModel.IsWelcomeScreenVisible = false;

                if (ViewModel != null)
                {
                    await ViewModel.LoadDirectoryAsync(target);
                    ViewModel.IsWelcomeScreenVisible = false;
                }

                if (WelcomeOverlayGrid != null) WelcomeOverlayGrid.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível carregar a pasta arrastada: {ex.Message}",
                "Aviso — Mavi Select",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Licença e Ativação

    private void BtnLicenseInfo_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLicenseDialog();
    }

    private void BtnLockCopyMachineId_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CopyMachineId();
    }

    private void BtnLockActivate_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TryActivateLicense();
    }

    private void BtnLockExit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLicenseLocked)
        {
            ViewModel.ExitApplication();
        }
        else
        {
            ViewModel.CloseLicenseDialog();
        }
    }

    private void BtnCloseLicense_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanDismissLicenseOverlay)
        {
            ViewModel.CloseLicenseDialog();
        }
    }

    private void LicenseOverlayBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.CanDismissLicenseOverlay)
        {
            ViewModel.CloseLicenseDialog();
        }
    }

    private void LicenseCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Evita que o clique dentro do card feche o modal
        e.Handled = true;
    }

    #endregion
}
