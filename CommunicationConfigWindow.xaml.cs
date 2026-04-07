using System.Windows;
using System.Windows.Controls;
using PlcOpcUaHmi.ViewModels;

namespace PlcOpcUaHmi;

public partial class CommunicationConfigWindow : Window
{
    public CommunicationConfigWindow()
    {
        InitializeComponent();
    }

    private void ConnectionPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is PasswordBox passwordBox)
        {
            vm.Connection.Password = passwordBox.Password;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
