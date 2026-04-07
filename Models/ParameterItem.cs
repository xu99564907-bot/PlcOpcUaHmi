using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class ParameterItem : ObservableObject
{
    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string value = string.Empty;

    [ObservableProperty]
    private string unit = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private UserRole minRole = UserRole.Engineer;

    [ObservableProperty]
    private bool isReadOnly = true;

    [ObservableProperty]
    private string permissionHint = "只读";
}
