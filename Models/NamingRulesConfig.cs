using System.Collections.Generic;

namespace PlcOpcUaHmi.Models;

public class NamingRulesConfig
{
    public CylinderNamingRules Cylinder { get; set; } = new();
    public AxisNamingRules Axis { get; set; } = new();

    public static NamingRulesConfig CreateDefault() => new()
    {
        Cylinder = new CylinderNamingRules
        {
            SegmentSeparators = new List<string> { "_" },
            HomeKeywords = new List<string> { "下降", "缩回", "松开", "打开", "取料", "退回", "复位", "关闭", "原点" },
            WorkKeywords = new List<string> { "上升", "伸出", "夹紧", "放料", "推出", "前进", "上料", "压下", "闭合" },
            VerticalKeywords = new List<string> { "上下", "上升", "下降", "升降" }
        },
        Axis = new AxisNamingRules
        {
            PositiveKeywords = new List<string> { "正转", "正向", "前进", "上升", "伸出" },
            NegativeKeywords = new List<string> { "反转", "反向", "后退", "下降", "缩回" }
        }
    };
}

public class CylinderNamingRules
{
    public string MotionAssignmentMode { get; set; } = "ByRowOrder";
    public string FirstOccurrenceRole { get; set; } = "Work";
    public string SecondOccurrenceRole { get; set; } = "Home";
    public List<string> GroupedSuffixes { get; set; } = new();
    public List<string> SegmentSeparators { get; set; } = new();
    public List<string> HomeKeywords { get; set; } = new();
    public List<string> WorkKeywords { get; set; } = new();
    public List<string> VerticalKeywords { get; set; } = new();
}

public class AxisNamingRules
{
    public List<string> PositiveKeywords { get; set; } = new();
    public List<string> NegativeKeywords { get; set; } = new();
}
