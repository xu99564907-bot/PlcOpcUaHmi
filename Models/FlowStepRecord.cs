using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class FlowStepRecord : ObservableObject
{
    [ObservableProperty]
    private string flowId = string.Empty;

    [ObservableProperty]
    private string flowName = string.Empty;

    [ObservableProperty]
    private DateTime time = DateTime.Now;

    [ObservableProperty]
    private DateTime startTime = DateTime.Now;

    [ObservableProperty]
    private DateTime endTime = DateTime.Now;

    [ObservableProperty]
    private double durationSeconds;

    [ObservableProperty]
    private int stepNo;

    [ObservableProperty]
    private string icon = "▶";

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string comment = string.Empty;

    [ObservableProperty]
    private string result = "运行中";

    [ObservableProperty]
    private string relatedAlarm = string.Empty;

    [ObservableProperty]
    private bool isAbnormal;

    [ObservableProperty]
    private string shiftKey = string.Empty;

    [ObservableProperty]
    private string archiveDate = string.Empty;

    [ObservableProperty]
    private bool isHighlighted;
}
