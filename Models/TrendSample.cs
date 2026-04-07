using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class TrendSample : ObservableObject
{
    [ObservableProperty]
    private DateTime time = DateTime.Now;

    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private double value;

    [ObservableProperty]
    private string source = string.Empty;
}
