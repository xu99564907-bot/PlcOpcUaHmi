using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class AutoProgramFlowNode : ObservableObject
{
    [ObservableProperty] private int stepNo;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string action = string.Empty;
    [ObservableProperty] private string nextStep = string.Empty;
    [ObservableProperty] private double left;
    [ObservableProperty] private double top;
    [ObservableProperty] private string fill = "#DBEAFE";
    [ObservableProperty] private bool isDecision;

    public string StepCode => $"STEP {StepNo:000}";
}
