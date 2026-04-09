using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class ManualCylinderBlockItem : ObservableObject
{
    [ObservableProperty]
    private int cylinderIndex;

    [ObservableProperty]
    private int displayOrder;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string homeCommandTagName = string.Empty;

    [ObservableProperty]
    private string workCommandTagName = string.Empty;

    [ObservableProperty]
    private string homeSensorTagName = string.Empty;

    [ObservableProperty]
    private string workSensorTagName = string.Empty;

    [ObservableProperty]
    private string homeInterlockTagName = string.Empty;

    [ObservableProperty]
    private string workInterlockTagName = string.Empty;

    [ObservableProperty]
    private string homeValueTagName = string.Empty;

    [ObservableProperty]
    private string workValueTagName = string.Empty;

    [ObservableProperty]
    private bool homeActive;

    [ObservableProperty]
    private bool workActive;

    [ObservableProperty]
    private bool homeInterlockActive = true;

    [ObservableProperty]
    private bool workInterlockActive = true;

    [ObservableProperty]
    private bool outputActive;

    [ObservableProperty]
    private bool homeCommandActive;

    [ObservableProperty]
    private bool workCommandActive;

    [ObservableProperty]
    private string statusText = "待机";

    [ObservableProperty]
    private string currentStateText = "未绑定状态";

    [ObservableProperty]
    private string interlockHint = string.Empty;
}
