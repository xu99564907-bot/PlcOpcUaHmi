using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcOpcUaHmi.Models;

public partial class OpcUaBrowseNode : ObservableObject
{
    public ObservableCollection<OpcUaBrowseNode> Children { get; } = new();

    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string nodeId = string.Empty;
    [ObservableProperty] private string nodeClass = string.Empty;
    [ObservableProperty] private string dataType = "--";
    [ObservableProperty] private string value = "--";
    [ObservableProperty] private bool hasChildren;
    [ObservableProperty] private bool isLoaded;
    [ObservableProperty] private bool isPlaceholder;

    public static OpcUaBrowseNode CreatePlaceholder()
    {
        return new OpcUaBrowseNode
        {
            DisplayName = "Loading",
            IsPlaceholder = true
        };
    }
}
