using System.Text;
using System.Windows;

namespace PlcOpcUaHmi;

public partial class App : Application
{
    public App()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
