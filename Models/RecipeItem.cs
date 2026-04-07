using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class RecipeItem : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string productCode = string.Empty;

    [ObservableProperty]
    private string version = "V1.0";

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private DateTime updatedAt = DateTime.Now;

    [ObservableProperty]
    private string updatedBy = "System";

    [ObservableProperty]
    private ObservableCollection<ParameterItem> parameters = new();
}
