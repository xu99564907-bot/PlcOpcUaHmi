using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class IoTableRow : ObservableObject
{
    [ObservableProperty]
    private string inputModule = string.Empty;

    [ObservableProperty]
    private string inputAddress = string.Empty;

    [ObservableProperty]
    private string inputStation = string.Empty;

    [ObservableProperty]
    private string inputComment = string.Empty;

    [ObservableProperty]
    private string inputRemark = string.Empty;

    [ObservableProperty]
    private string outputModule = string.Empty;

    [ObservableProperty]
    private string outputAddress = string.Empty;

    [ObservableProperty]
    private string outputStation = string.Empty;

    [ObservableProperty]
    private string outputComment = string.Empty;

    [ObservableProperty]
    private string outputRemark = string.Empty;
}
