using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class DesignerPage : ObservableObject
{
    [ObservableProperty]
    private string name = "主界面";

    [ObservableProperty]
    private double canvasWidth = 1280;

    [ObservableProperty]
    private double canvasHeight = 720;

    public List<DesignerElement> Elements { get; set; } = new();

    public override string ToString() => Name;
}
