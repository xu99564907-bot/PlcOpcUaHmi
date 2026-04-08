using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class IoTableRow : ObservableObject
{
    [ObservableProperty]
    private string inputAddress = string.Empty;

    [ObservableProperty]
    private string inputComment = string.Empty;

    [ObservableProperty]
    private string outputAddress = string.Empty;

    [ObservableProperty]
    private string outputComment = string.Empty;
}
