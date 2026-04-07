using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class TagItem : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string nodeId = string.Empty;

    [ObservableProperty]
    private string dataType = "String";

    [ObservableProperty]
    private string category = "General";

    [ObservableProperty]
    private string group = "Default";

    [ObservableProperty]
    private string direction = "Input";

    [ObservableProperty]
    private string currentValue = "";

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private bool isAlarm;

    [ObservableProperty]
    private bool isWritable;
}
