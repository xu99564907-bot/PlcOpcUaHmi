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
    private string homeCommandDisplayName = string.Empty;

    [ObservableProperty]
    private string workCommandDisplayName = string.Empty;

    [ObservableProperty]
    private string homeSensorDisplayName = string.Empty;

    [ObservableProperty]
    private string workSensorDisplayName = string.Empty;

    [ObservableProperty]
    private string homeCommandAddress = string.Empty;

    [ObservableProperty]
    private string workCommandAddress = string.Empty;

    [ObservableProperty]
    private string homeSensorAddress = string.Empty;

    [ObservableProperty]
    private string workSensorAddress = string.Empty;

    [ObservableProperty]
    private bool isVerticalNaming;

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

    public string WorkCommandLabel => !string.IsNullOrWhiteSpace(WorkCommandDisplayName)
        ? WorkCommandDisplayName
        : IsVerticalNaming ? "上升" : "伸出";

    public string HomeCommandLabel => !string.IsNullOrWhiteSpace(HomeCommandDisplayName)
        ? HomeCommandDisplayName
        : IsVerticalNaming ? "下降" : "缩回";

    public string WorkPositionLabel => !string.IsNullOrWhiteSpace(WorkSensorDisplayName)
        ? WorkSensorDisplayName
        : IsVerticalNaming ? "上升到位" : "伸出到位";

    public string HomePositionLabel => !string.IsNullOrWhiteSpace(HomeSensorDisplayName)
        ? HomeSensorDisplayName
        : IsVerticalNaming ? "下降到位" : "缩回到位";

    partial void OnDisplayNameChanged(string value)
    {
        OnPropertyChanged(nameof(WorkCommandLabel));
        OnPropertyChanged(nameof(HomeCommandLabel));
        OnPropertyChanged(nameof(WorkPositionLabel));
        OnPropertyChanged(nameof(HomePositionLabel));
    }

    partial void OnWorkCommandDisplayNameChanged(string value) => OnPropertyChanged(nameof(WorkCommandLabel));

    partial void OnHomeCommandDisplayNameChanged(string value) => OnPropertyChanged(nameof(HomeCommandLabel));

    partial void OnWorkSensorDisplayNameChanged(string value) => OnPropertyChanged(nameof(WorkPositionLabel));

    partial void OnHomeSensorDisplayNameChanged(string value) => OnPropertyChanged(nameof(HomePositionLabel));

    partial void OnIsVerticalNamingChanged(bool value)
    {
        OnPropertyChanged(nameof(WorkCommandLabel));
        OnPropertyChanged(nameof(HomeCommandLabel));
        OnPropertyChanged(nameof(WorkPositionLabel));
        OnPropertyChanged(nameof(HomePositionLabel));
    }
}
