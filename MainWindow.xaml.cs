using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PlcOpcUaHmi.Models;
using PlcOpcUaHmi.ViewModels;

namespace PlcOpcUaHmi;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _startPressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _startHandled;
    private int _startHoldTicks;

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

    private void ShowUsageHelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        const string message =
            "文件 > 导入 XML 变量：导入 XML 变量表\r\n" +
            "文件 > 导入 CSV 变量：导入 CSV 变量表\r\n" +
            "文件 > 导入流程 CSV：导入流程分析数据\r\n" +
            "窗口：运行/设计态切换、最大化、还原、置顶\r\n" +
            "帮助：查看 README 和关于信息\r\n" +
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
