using System;
using System.Globalization;
using System.Windows.Data;

namespace PlcOpcUaHmi.Converters;

public sealed class NavigationIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var title = value as string ?? string.Empty;
        return title switch
        {
            "主界面" => "\uE80F",
            "监控" => "\uE9D9",
            "手动操作" => "\uE7C9",
            "参数设定" => "\uE713",
            "报警画面" => "\uE7BA",
            "登录" => "\uE77B",
            "设计器" => "\uE70F",
            _ => "\uE8A5"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
