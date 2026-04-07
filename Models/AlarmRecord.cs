using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class AlarmRecord : ObservableObject
{
    [ObservableProperty]
    private DateTime time = DateTime.Now;

    [ObservableProperty]
    private string level = "Info";

    [ObservableProperty]
    private string source = string.Empty;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private bool active = true;

    [ObservableProperty]
    private bool acknowledged;

    [ObservableProperty]
    private DateTime? clearTime;

    [ObservableProperty]
    private string state = "Active";

    [ObservableProperty]
    private int count = 1;

    [ObservableProperty]
    private string acknowledgedBy = string.Empty;

    [ObservableProperty]
    private string handlingSuggestion = string.Empty;

    [ObservableProperty]
    private string causeArchive = string.Empty;

    [ObservableProperty]
    private bool isHighlighted;
}
