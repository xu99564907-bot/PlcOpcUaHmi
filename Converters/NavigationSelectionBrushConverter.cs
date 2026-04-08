using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PlcOpcUaHmi.Converters;

public sealed class NavigationSelectionBrushConverter : IMultiValueConverter
{
    private static readonly Brush ActiveBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
    private static readonly Brush InactiveBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
    private static readonly Brush ActiveForeground = Brushes.White;
    private static readonly Brush InactiveForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var currentSection = values.Length > 0 ? values[0] as string : string.Empty;
        var title = values.Length > 1 ? values[1] as string : string.Empty;
        var mode = parameter as string ?? "Background";
        var isActive = IsActive(currentSection ?? string.Empty, title ?? string.Empty);

        return mode switch
        {
            "Foreground" => isActive ? ActiveForeground : InactiveForeground,
            _ => isActive ? ActiveBackground : InactiveBackground
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => Array.Empty<object>();

    private static bool IsActive(string currentSection, string title)
    {
        return title switch
        {
            "主界面" => currentSection is "主界面" or "运行总览",
            "监控" => currentSection is "监控" or "监视画面" or "输入输出监控" or "程序监控" or "通讯状态监控" or "详细生产数据",
            "配方管理" => currentSection is "配方管理",
            "手动操作" => currentSection is "手动操作" or "手动画面" or "气缸" or "轴" or "机械手" or "电机" or "挡停",
            "参数设定" => currentSection is "参数设定" or "系统参数设定" or "轴参数设定" or "气缸参数设定" or "真空参数设定" or "传感器参数设定",
            "报警画面" => currentSection is "报警画面" or "当前报警" or "历史报警" or "日志" or "报警统计",
            "登录" => currentSection is "登录" or "登录权限",
            "操作审计" => currentSection is "操作审计",
            "设计器" => currentSection.Contains("设计器", StringComparison.Ordinal),
            _ => false
        };
    }
}
