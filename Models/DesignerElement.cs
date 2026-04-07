using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class DesignerElement : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = "新建控件";

    [ObservableProperty]
    private string elementType = "Button";

    [ObservableProperty]
    private double left = 20;

    [ObservableProperty]
    private double top = 20;

    [ObservableProperty]
    private double width = 120;

    [ObservableProperty]
    private double height = 40;

    [ObservableProperty]
    private string text = "按钮";

    [ObservableProperty]
    private string background = "#E8EEF9";

    [ObservableProperty]
    private string foreground = "#1F2937";

    [ObservableProperty]
    private string borderBrush = "#94A3B8";

    [ObservableProperty]
    private int fontSize = 14;

    [ObservableProperty]
    private string tagBinding = string.Empty;

    [ObservableProperty]
    private string commandBinding = string.Empty;

    [ObservableProperty]
    private string navigationTarget = string.Empty;

    [ObservableProperty]
    private bool snapToGrid = true;
}
