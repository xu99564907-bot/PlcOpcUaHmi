using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace PlcOpcUaHmi.ViewModels;

public partial class NavigationItemViewModel : ObservableObject
{
    public string Title { get; }
    public ObservableCollection<NavigationItemViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool isExpanded = true;

    public NavigationItemViewModel(string title, params string[] children)
    {
        Title = title;
        foreach (var child in children)
        {
            Children.Add(new NavigationItemViewModel(child));
        }
    }

    public NavigationItemViewModel(string title)
    {
        Title = title;
    }
}
