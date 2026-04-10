using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PlcOpcUaHmi.Models;
using PlcOpcUaHmi.ViewModels;

namespace PlcOpcUaHmi;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _startPressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _startHandled;
    private int _startHoldTicks;
    private bool _isProgramTraceSelecting;
    private double _programTraceSelectionStartX;
    private readonly Dictionary<ScrollViewer, double> _pageScrollOffsets = new();

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        _startPressTimer.Tick += StartPressTimer_Tick;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowVisibleOnScreen();
        UpdateMaxRestoreButtonIcon();

        if (DataContext is MainViewModel vm)
        {
            vm.PopupRequested -= Vm_PopupRequested;
            vm.PopupRequested += Vm_PopupRequested;
            vm.ConfirmationRequested -= Vm_ConfirmationRequested;
            vm.ConfirmationRequested += Vm_ConfirmationRequested;
            vm.SectionJumpRequested -= Vm_SectionJumpRequested;
            vm.SectionJumpRequested += Vm_SectionJumpRequested;
            vm.HighlightRequested -= Vm_HighlightRequested;
            vm.HighlightRequested += Vm_HighlightRequested;
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaxRestoreButtonIcon();
    }

    private void UpdateMaxRestoreButtonIcon()
    {
        if (MaximizeIcon is null || RestoreIcon is null)
        {
            return;
        }

        MaximizeIcon.Visibility = WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = WindowState == WindowState.Maximized ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EnsureWindowVisibleOnScreen()
    {
        const double margin = 24;
        var workArea = SystemParameters.WorkArea;

        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;

        if (Width > workArea.Width - margin)
        {
            Width = Math.Max(1280, workArea.Width - margin);
        }

        if (Height > workArea.Height - margin)
        {
            Height = Math.Max(720, workArea.Height - margin);
        }

        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private void Vm_PopupRequested(string title, string message, string level)
    {
        var icon = level switch
        {
            "Error" => MessageBoxImage.Error,
            "Interlock" => MessageBoxImage.Stop,
            "Warning" => MessageBoxImage.Warning,
            _ => MessageBoxImage.Information
        };
        MessageBox.Show(this, message, title, MessageBoxButton.OK, icon);
    }

    private bool Vm_ConfirmationRequested(string title, string message)
    {
        return MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void Vm_SectionJumpRequested(string section, string? keyword)
    {
        var message = string.IsNullOrWhiteSpace(keyword)
            ? $"已跳转到：{section}"
            : $"已跳转到：{section}\n定位关键字：{keyword}";
        MessageBox.Show(this, message, "分析联动", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Vm_HighlightRequested(string targetType, string? keyword)
    {
        // 当前版本先做高亮数据联动，滚动定位保留到后续增强。
    }

    private void StartPressTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            _startPressTimer.Stop();
            return;
        }

        _startHoldTicks++;
        vm.UpdateStartHoldProgress(Math.Min(100, _startHoldTicks * 10));

        if (_startHoldTicks < 10 || _startHandled)
        {
            return;
        }

        _startHandled = true;
        _startPressTimer.Stop();
        vm.UpdateStartHoldProgress(100);
        vm.StartDeviceCommand.Execute(null);
    }

    private void StartDeviceButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startHandled = false;
        _startHoldTicks = 0;
        _startPressTimer.Stop();
        if (DataContext is MainViewModel vm)
        {
            vm.UpdateStartHoldProgress(0);
        }
        _startPressTimer.Start();
    }

    private void StartDeviceButton_PreviewMouseLeftButtonUp(object sender, RoutedEventArgs e)
    {
        _startPressTimer.Stop();
        _startHoldTicks = 0;
        if (DataContext is MainViewModel vm)
        {
            vm.UpdateStartHoldProgress(0);
        }
    }

    private void RolePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is PasswordBox passwordBox)
        {
            vm.LoginPassword = passwordBox.Password;
        }
    }

    private void ConnectionPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is PasswordBox passwordBox)
        {
            vm.Connection.Password = passwordBox.Password;
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            vm.CopySelectedDesignerElementCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            vm.PasteDesignerElementCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            if (vm.SaveIoTableToSourceCommand.CanExecute(null))
            {
                vm.SaveIoTableToSourceCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Delete)
        {
            vm.RemoveSelectedDesignerElementCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            vm.MoveSelectedElementCommand.Execute("Left");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            vm.MoveSelectedElementCommand.Execute("Right");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            vm.MoveSelectedElementCommand.Execute("Up");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            vm.MoveSelectedElementCommand.Execute("Down");
            e.Handled = true;
            return;
        }
    }

    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindScrollableAncestor(source, e.Delta);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void PageScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            _pageScrollOffsets[scrollViewer] = scrollViewer.VerticalOffset;
        }
    }

    private void PageScrollViewer_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            _pageScrollOffsets[scrollViewer] = scrollViewer.VerticalOffset;
        }
    }

    private void PageScrollViewer_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || e.OriginalSource is not FrameworkElement target
            || ReferenceEquals(scrollViewer, target))
        {
            return;
        }

        var offset = _pageScrollOffsets.TryGetValue(scrollViewer, out var rememberedOffset)
            ? rememberedOffset
            : scrollViewer.VerticalOffset;

        Dispatcher.BeginInvoke(() => scrollViewer.ScrollToVerticalOffset(offset), DispatcherPriority.Background);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static ScrollViewer? FindScrollableAncestor(DependencyObject? current, int delta)
    {
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer && scrollViewer.ScrollableHeight > 0)
            {
                var canScrollUp = delta > 0 && scrollViewer.VerticalOffset > 0;
                var canScrollDown = delta < 0 && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

                if (canScrollUp || canScrollDown)
                {
                    return scrollViewer;
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ToolboxItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is string tool)
        {
            DragDrop.DoDragDrop(element, tool, DragDropEffects.Copy);
        }
    }

    private void DesignerCanvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void DesignerCanvas_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (sender is not Canvas canvas)
        {
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            return;
        }

        var tool = e.Data.GetData(DataFormats.StringFormat)?.ToString();
        if (string.IsNullOrWhiteSpace(tool))
        {
            return;
        }

        var position = e.GetPosition(canvas);
        vm.AddDesignerElementAtDropCommand.Execute($"{tool}|{position.X}|{position.Y}");
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaxRestoreWindowButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxRestoreButtonIcon();
    }

    private void MaximizeWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        UpdateMaxRestoreButtonIcon();
    }

    private void RestoreWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Normal;
        UpdateMaxRestoreButtonIcon();
    }

    private void ToggleTopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            Topmost = menuItem.IsChecked;
        }
    }

    private void ProgramTraceChartHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetPosition(element);
        var normalized = point.X / element.ActualWidth;
        vm.UpdateProgramMonitorCursor(normalized);
        UpdateProgramTraceHoverLines(element, point.X, point.Y);

        if (_isProgramTraceSelecting)
        {
            UpdateProgramTraceSelection(element, point.X);
        }
    }

    private void ProgramTraceChartHost_MouseLeave(object sender, MouseEventArgs e)
    {
        HideProgramTraceHoverLines(sender as FrameworkElement);
    }

    private void ProgramTraceChartHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            vm.ResetProgramMonitorTraceZoom();
            HideProgramTraceSelection(element);
            e.Handled = true;
            return;
        }

        var point = e.GetPosition(element);
        var normalized = point.X / element.ActualWidth;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            _isProgramTraceSelecting = true;
            _programTraceSelectionStartX = point.X;
            element.CaptureMouse();
            UpdateProgramTraceSelection(element, point.X);
            e.Handled = true;
            return;
        }

        vm.SetProgramMonitorCursorA(normalized);
        UpdateProgramTraceCursorLine(ProgramTraceCursorALine, element, point.X);
    }

    private void ProgramTraceChartHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isProgramTraceSelecting || DataContext is not MainViewModel vm || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetPosition(element);
        var startNormalized = _programTraceSelectionStartX / element.ActualWidth;
        var endNormalized = point.X / element.ActualWidth;
        HideProgramTraceSelection(element);
        element.ReleaseMouseCapture();
        _isProgramTraceSelecting = false;

        if (Math.Abs(point.X - _programTraceSelectionStartX) >= 8)
        {
            vm.ZoomProgramMonitorTraceRange(startNormalized, endNormalized);
        }

        e.Handled = true;
    }

    private void ProgramTraceChartHost_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetPosition(element);
        var normalized = point.X / element.ActualWidth;
        vm.SetProgramMonitorCursorB(normalized);
        UpdateProgramTraceCursorLine(ProgramTraceCursorBLine, element, point.X);
        e.Handled = true;
    }

    private void ProgramTraceChartHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.ZoomProgramMonitorTrace(e.Delta);
        e.Handled = true;
    }

    private static void UpdateProgramTraceCursorLine(System.Windows.Shapes.Line? line, FrameworkElement host, double x)
    {
        if (line is null)
        {
            return;
        }

        var clampedX = Math.Max(0, Math.Min(host.ActualWidth, x));
        line.Visibility = Visibility.Visible;
        line.X1 = clampedX;
        line.X2 = clampedX;
        line.Y1 = 0;
        line.Y2 = Math.Max(0, host.ActualHeight);
    }

    private void UpdateProgramTraceHoverLines(FrameworkElement host, double x, double y)
    {
        var clampedX = Math.Max(0, Math.Min(host.ActualWidth, x));
        var clampedY = Math.Max(0, Math.Min(host.ActualHeight, y));

        if (ProgramTraceHoverXLine is not null)
        {
            ProgramTraceHoverXLine.Visibility = Visibility.Visible;
            ProgramTraceHoverXLine.X1 = clampedX;
            ProgramTraceHoverXLine.X2 = clampedX;
            ProgramTraceHoverXLine.Y1 = 0;
            ProgramTraceHoverXLine.Y2 = Math.Max(0, host.ActualHeight);
        }

        if (ProgramTraceHoverYLine is not null)
        {
            ProgramTraceHoverYLine.Visibility = Visibility.Visible;
            ProgramTraceHoverYLine.X1 = 0;
            ProgramTraceHoverYLine.X2 = Math.Max(0, host.ActualWidth);
            ProgramTraceHoverYLine.Y1 = clampedY;
            ProgramTraceHoverYLine.Y2 = clampedY;
        }
    }

    private void HideProgramTraceHoverLines(FrameworkElement? host)
    {
        if (ProgramTraceHoverXLine is not null)
        {
            ProgramTraceHoverXLine.Visibility = Visibility.Collapsed;
            ProgramTraceHoverXLine.X1 = 0;
            ProgramTraceHoverXLine.X2 = 0;
            ProgramTraceHoverXLine.Y1 = 0;
            ProgramTraceHoverXLine.Y2 = Math.Max(0, host?.ActualHeight ?? 0);
        }

        if (ProgramTraceHoverYLine is not null)
        {
            ProgramTraceHoverYLine.Visibility = Visibility.Collapsed;
            ProgramTraceHoverYLine.X1 = 0;
            ProgramTraceHoverYLine.X2 = Math.Max(0, host?.ActualWidth ?? 0);
            ProgramTraceHoverYLine.Y1 = 0;
            ProgramTraceHoverYLine.Y2 = 0;
        }
    }

    private void UpdateProgramTraceSelection(FrameworkElement host, double currentX)
    {
        if (ProgramTraceSelectionRect is null)
        {
            return;
        }

        var left = Math.Max(0, Math.Min(_programTraceSelectionStartX, currentX));
        var right = Math.Min(host.ActualWidth, Math.Max(_programTraceSelectionStartX, currentX));

        ProgramTraceSelectionRect.Visibility = Visibility.Visible;
        ProgramTraceSelectionRect.Width = Math.Max(0, right - left);
        ProgramTraceSelectionRect.Height = Math.Max(0, host.ActualHeight);
        Canvas.SetLeft(ProgramTraceSelectionRect, left);
        Canvas.SetTop(ProgramTraceSelectionRect, 0);
    }

    private void HideProgramTraceSelection(FrameworkElement host)
    {
        if (ProgramTraceSelectionRect is null)
        {
            return;
        }

        ProgramTraceSelectionRect.Visibility = Visibility.Collapsed;
        ProgramTraceSelectionRect.Width = 0;
        ProgramTraceSelectionRect.Height = Math.Max(0, host.ActualHeight);
        Canvas.SetLeft(ProgramTraceSelectionRect, 0);
        Canvas.SetTop(ProgramTraceSelectionRect, 0);
    }

    private void ShowUsageHelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        const string message =
            "文件 > 导入 XML 变量：导入 XML 变量表\r\n" +
            "文件 > 导入 CSV 变量：导入 CSV 变量表\r\n" +
            "文件 > 导入 IO 表：导入四列表头的 IO 地址和注释\r\n" +
            "文件 > 生成手动程序：按模板生成 IO、对象和手动程序文件\r\n" +
            "文件 > 导入流程 CSV：导入流程分析数据\r\n" +
            "窗口：运行/设计态切换、最大化、还原、置顶\r\n" +
            "帮助：查看 README 和关于信息\r\n" +
            "设计器：支持手动程序生成、自动程序生成和结果预览\r\n" +
            "监视画面：可直接浏览 OPC UA 节点并加入变量表";

        MessageBox.Show(this, message, "使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenReadmeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var readmePath = ResolveReadmePath();
        if (readmePath is null)
        {
            MessageBox.Show(this, "未找到 README.md 文件。", "帮助", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = readmePath,
            UseShellExecute = true
        });
    }

    private void ShowAboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        const string message =
            "PLC OPC UA HMI 组态设计器\n" +
            "当前版本包含 OPC UA 通讯、运行监控、设计器、报警与参数管理，" +
            "并支持内置 OPC UA 节点浏览与调试。";

        MessageBox.Show(this, message, "关于", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenCommunicationConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new CommunicationConfigWindow
        {
            Owner = this,
            DataContext = DataContext
        };
        window.ShowDialog();
    }

    private void OpenCylinderSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedCylinderSettingsBlock = (sender as FrameworkElement)?.DataContext as Models.ManualCylinderBlockItem
                ?? vm.ManualCylinderBlocks.FirstOrDefault();
        }

        var window = new CylinderSettingsWindow
        {
            Owner = this,
            DataContext = DataContext
        };
        window.ShowDialog();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        await vm.ConnectCommand.ExecuteAsync(null);
        return;
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    private void OpcUaBrowserTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is OpcUaBrowseNode node && !node.IsPlaceholder)
        {
            vm.SelectedOpcUaBrowseNode = node;
        }
    }

    private async void OpcUaBrowserTreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not TreeViewItem treeViewItem)
        {
            return;
        }

        if (treeViewItem.DataContext is OpcUaBrowseNode node && !node.IsPlaceholder)
        {
            await vm.ExpandOpcUaBrowserNodeCommand.ExecuteAsync(node);
        }
    }

    private void IoPreviewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var previewGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserSortColumns = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Background = System.Windows.Media.Brushes.White,
            Foreground = System.Windows.Media.Brushes.Black,
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            ItemsSource = vm.IoTableRows
        };
        previewGrid.Columns.Add(new DataGridTextColumn { Header = "杈撳叆鍦板潃", Binding = new System.Windows.Data.Binding("InputAddress"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        previewGrid.Columns.Add(new DataGridTextColumn { Header = "杈撳叆娉ㄩ噴", Binding = new System.Windows.Data.Binding("InputComment"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        previewGrid.Columns.Add(new DataGridTextColumn { Header = "杈撳嚭鍦板潃", Binding = new System.Windows.Data.Binding("OutputAddress"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        previewGrid.Columns.Add(new DataGridTextColumn { Header = "杈撳嚭娉ㄩ噴", Binding = new System.Windows.Data.Binding("OutputComment"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        ShowPreviewWindow("IO 表预览", previewGrid, 1200, 760);
    }

    private void IoProgramPreviewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var previewTextBox = new TextBox
        {
            Text = vm.SelectedGeneratedIoProgramContent,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Background = System.Windows.Media.Brushes.White,
            Foreground = System.Windows.Media.Brushes.Black,
            BorderBrush = System.Windows.Media.Brushes.LightGray
        };

        var title = vm.SelectedGeneratedIoProgram?.DisplayName is { Length: > 0 } displayName
            ? $"绋嬪簭棰勮 - {displayName}"
            : "绋嬪簭棰勮";
        ShowPreviewWindow(title, previewTextBox, 1100, 760);
    }

    private void CopyCurrentIoProgramButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var content = vm.SelectedGeneratedIoProgramContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            MessageBox.Show(this, "当前没有可复制的程序内容。", "复制程序", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(content);
        MessageBox.Show(this, "当前程序内容已复制到剪贴板。", "复制程序", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyCurrentAutoProgramButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var content = vm.SelectedGeneratedAutoProgramContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            MessageBox.Show(this, "当前没有可复制的自动程序内容。", "复制程序", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(content);
        MessageBox.Show(this, "当前自动程序内容已复制到剪贴板。", "复制程序", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowPreviewWindow(string title, FrameworkElement content, double width, double height)
    {
        var window = new Window
        {
            Owner = this,
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 900,
            MinHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White,
            Content = new Border
            {
                Padding = new Thickness(12),
                Background = System.Windows.Media.Brushes.White,
                Child = content
            }
        };
        window.ShowDialog();
    }

    private static string? ResolveReadmePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "README.md"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "README.md"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
