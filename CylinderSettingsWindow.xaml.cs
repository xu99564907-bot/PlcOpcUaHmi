using System.Windows;

namespace PlcOpcUaHmi;

public partial class CylinderSettingsWindow : Window
{
    public CylinderSettingsWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
