using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class OperationAuditRecord : ObservableObject
{
    [ObservableProperty]
    private DateTime time = DateTime.Now;

    [ObservableProperty]
    private string user = string.Empty;

    [ObservableProperty]
    private string action = string.Empty;

    [ObservableProperty]
    private string target = string.Empty;

    [ObservableProperty]
    private string result = string.Empty;

    [ObservableProperty]
    private string detail = string.Empty;
}
