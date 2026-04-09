using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PlcOpcUaHmi.Models;
using PlcOpcUaHmi.Services;

namespace PlcOpcUaHmi.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly OpcUaService _opcUaService = new();
    private readonly CsvImportService _csvImportService = new();
    private readonly XmlImportService _xmlImportService = new();
    private readonly IoTableImportService _ioTableImportService = new();
    private readonly IoProgramGenerationService _ioProgramGenerationService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly DesignerLayoutService _designerLayoutService = new();
    private readonly DesignerProjectService _designerProjectService = new();
    private readonly ParameterService _parameterService = new();
    private readonly AlarmService _alarmService = new();
    private readonly FlowLogCsvService _flowLogCsvService = new();
    private readonly RecipeService _recipeService = new();
    private readonly TrendHistoryService _trendHistoryService = new();
    private readonly DispatcherTimer _subscriptionTimer;
    private readonly Dictionary<string, AlarmRecord> _activeAlarmMap = new(StringComparer.OrdinalIgnoreCase);
    private string _currentIoSourceFilePath = string.Empty;
    private int _currentIoSourceEncodingCodePage = 65001;
    private List<string> _currentIoSourceHeaders = new();
    private readonly List<IoTableRow> _importedIoRowsSnapshot = new();
    private readonly List<IoTableRow> _lastIoSavedSnapshot = new();
    private string _lastIoSavedFilePath = string.Empty;
    private string _lastIoHistoryFilePath = string.Empty;
    private DateTime _lastIoSaveAt = DateTime.MinValue;
    private DesignerElement? _clipboardElement;
    private DesignerElement? _designerSelectionSubscription;
    private bool _isRefreshing;

    public event Action<string, string, string>? PopupRequested;
    public event Func<string, string, bool>? ConfirmationRequested;
    public event Action<string, string?>? SectionJumpRequested;
    public event Action<string, string?>? HighlightRequested;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();
    public ObservableCollection<TagItem> Tags { get; } = new();
    public ObservableCollection<EventBinding> EventBindings { get; } = new();
    public ObservableCollection<AlarmRecord> CurrentAlarms { get; } = new();
    public ObservableCollection<AlarmRecord> AlarmHistory { get; } = new();
    public ObservableCollection<AlarmRecord> Logs { get; } = new();
    public ObservableCollection<DesignerElement> DesignerElements { get; } = new();
    public ObservableCollection<DesignerPage> DesignerPages { get; } = new();
    public ObservableCollection<string> ToolboxItems { get; } = new() { "Button", "Indicator", "Label", "ValueDisplay", "AlarmBanner", "Motor", "Cylinder", "Axis", "Robot", "Stopper", "PageButton" };
    public ObservableCollection<string> RuntimeTemplates { get; } = new() { "主界面", "监控画面", "手动画面", "参数设定", "报警画面" };
    public ObservableCollection<string> CommunicationProtocolOptions { get; } = new() { "OPC UA", "Modbus TCP", "MC Protocol" };
    public ObservableCollection<string> DesignerActionOptions { get; } = new() { "", "变量翻转", "置位", "复位", "脉冲", "页面跳转", "气缸回原", "气缸动点" };
    public ObservableCollection<string> DesignerNavigationTargetOptions { get; } = new() { "主界面", "监控", "手动操作", "参数设定", "配方管理", "报警画面", "登录", "设计器" };
    public ObservableCollection<ParameterItem> Parameters { get; } = new();
    public ObservableCollection<UserRole> Roles { get; } = new() { UserRole.Operator, UserRole.Engineer, UserRole.Administrator };
    public ObservableCollection<string> MonitorCategoryOptions { get; } = new() { "全部", "Production", "Alarm", "Axis", "Motor", "Cylinder", "Robot", "IO" };
    public ObservableCollection<string> AlarmLevelOptions { get; } = new() { "全部", "Alarm", "Error", "Warning", "Info" };
    public ObservableCollection<string> AlarmTimeRangeOptions { get; } = new() { "全部", "本班次", "今日", "近7天" };
    public ObservableCollection<AlarmRecord> AlarmStatistics { get; } = new();
    public ObservableCollection<OperationAuditRecord> OperationAudits { get; } = new();
    public ObservableCollection<FlowStepRecord> FlowSteps { get; } = new();
    public ObservableCollection<FlowIssueSummary> FlowIssueSummaries { get; } = new();
    public ObservableCollection<RecipeItem> Recipes { get; } = new();
    public ObservableCollection<ParameterItem> ActiveRecipeParameters { get; } = new();
    public ObservableCollection<TrendSample> TrendSamples { get; } = new();
    public ObservableCollection<OpcUaBrowseNode> OpcUaBrowserNodes { get; } = new();
    public ObservableCollection<IoTableRow> IoTableRows { get; } = new();
    public ObservableCollection<ManualCylinderBlockItem> ManualCylinderBlocks { get; } = new();
    public ObservableCollection<GeneratedProgramArtifact> GeneratedIoPrograms { get; } = new();
    public ObservableCollection<AutoProgramFlowNode> AutoProgramFlowNodes { get; } = new();
    public ObservableCollection<GeneratedProgramArtifact> GeneratedAutoPrograms { get; } = new();
    public ObservableCollection<string> FlowFilterOptions { get; } = new() { "全部", "主线1", "主线2", "主线3" };
    public ObservableCollection<string> FlowTimeRangeOptions { get; } = new() { "全部", "本班次", "今日", "近7天" };
    public ObservableCollection<string> FlowStepFilterOptions { get; } = new() { "全部", "10", "20", "30", "40", "50", "60" };
    public ObservableCollection<string> IoPlcTemplateOptions { get; } = new() { "汇川中型PLC" };

    [ObservableProperty] private OpcUaConnectionOptions connection = new();
    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty] private string currentMonitorSubSection = "输入输出监控";
[ObservableProperty] private string currentManualSubSection = "气缸";
    [ObservableProperty] private string currentParameterSubSection = "系统参数设定";
    [ObservableProperty] private string currentAlarmSubSection = "当前报警";
[ObservableProperty] private string currentDesignerSubSection = "画布设计";
    [ObservableProperty] private string currentSection = "主界面";
    [ObservableProperty] private string systemMessage = "系统就绪";
    [ObservableProperty] private string loginUser = "操作员";
    [ObservableProperty] private string manualWriteTagName = string.Empty;
    [ObservableProperty] private string manualWriteValue = string.Empty;
    [ObservableProperty] private string newTagName = string.Empty;
    [ObservableProperty] private string newTagNodeId = string.Empty;
    [ObservableProperty] private string newTagDataType = "Boolean";
    [ObservableProperty] private string newTagCategory = "General";
    [ObservableProperty] private string newTagGroup = "Default";
    [ObservableProperty] private string newTagDirection = "Input";
    [ObservableProperty] private bool newTagIsAlarm;
    [ObservableProperty] private bool newTagIsWritable = true;
    [ObservableProperty] private string newEventTagName = string.Empty;
    [ObservableProperty] private string newEventTriggerCondition = "ValueChanged";
    [ObservableProperty] private string newEventName = string.Empty;
    [ObservableProperty] private string newEventTarget = string.Empty;
    [ObservableProperty] private string newEventParameter = string.Empty;
    [ObservableProperty] private DesignerElement? selectedDesignerElement;
    [ObservableProperty] private string selectedToolboxItem = "Button";
    [ObservableProperty] private double designerCanvasWidth = 1280;
    [ObservableProperty] private double designerCanvasHeight = 720;
    [ObservableProperty] private string designerPageName = "主界面";
    [ObservableProperty] private string designerProjectName = "PLC HMI Project";
    [ObservableProperty] private DesignerPage? selectedDesignerPage;
    [ObservableProperty] private bool isRuntimeMode;
    [ObservableProperty] private string dragToolboxItem = string.Empty;
    [ObservableProperty] private bool enableGridSnap = true;
    [ObservableProperty] private int gridSize = 10;
    [ObservableProperty] private bool autoRefreshEnabled = true;
    [ObservableProperty] private int refreshIntervalMs = 1000;
    [ObservableProperty] private bool useOpcSubscription = true;
    [ObservableProperty] private string selectedRuntimeTemplate = "主界面";
    [ObservableProperty] private UserRole currentUserRole = UserRole.Operator;
    [ObservableProperty] private string loginPassword = string.Empty;
    [ObservableProperty] private string axisJogDistance = "10";
    [ObservableProperty] private string axisTargetPosition = "100";
    [ObservableProperty] private bool cylinderHomeMaskEnabled;
    [ObservableProperty] private bool cylinderWorkMaskEnabled;
    [ObservableProperty] private string cylinderConfiguredName = string.Empty;
    [ObservableProperty] private string cylinderHomeCommandTagName = string.Empty;
    [ObservableProperty] private string cylinderWorkCommandTagName = string.Empty;
    [ObservableProperty] private string cylinderHomeSensorTagName = string.Empty;
    [ObservableProperty] private string cylinderWorkSensorTagName = string.Empty;
    [ObservableProperty] private string cylinderHomeInterlockTagName = string.Empty;
    [ObservableProperty] private string cylinderWorkInterlockTagName = string.Empty;
    [ObservableProperty] private ManualCylinderBlockItem? selectedCylinderSettingsBlock;
    [ObservableProperty] private string cylinderAlarmTimeSetting = "2.0";
    [ObservableProperty] private string cylinderHomeDelaySetting = "0.3";
    [ObservableProperty] private string cylinderWorkDelaySetting = "0.3";
    [ObservableProperty] private string cylinderCurrentActionTimeDisplay = "--";
    [ObservableProperty] private string cylinderLastActionTimeDisplay = "--";
    [ObservableProperty] private int cylinderActionCount;
    [ObservableProperty] private string selectedMonitorCategory = "全部";
    [ObservableProperty] private string selectedAlarmLevel = "全部";
    [ObservableProperty] private string selectedAlarmTimeRange = "全部";
    [ObservableProperty] private bool showOnlyFocusAlarms;
    [ObservableProperty] private double startHoldProgress;
    [ObservableProperty] private int currentFlowStepNo;
    [ObservableProperty] private string currentFlowComment = "自动流程待命";
    [ObservableProperty] private string selectedRecipeName = "产品A";
    [ObservableProperty] private string selectedFlowFilter = "全部";
    [ObservableProperty] private string selectedFlowTimeRange = "全部";
    [ObservableProperty] private string selectedFlowStepFilter = "全部";
    [ObservableProperty] private bool showOnlyAbnormalFlow;
    [ObservableProperty] private string jumpAlarmKeyword = string.Empty;
    [ObservableProperty] private string selectedIoPlcTemplate = "汇川中型PLC";
    [ObservableProperty] private string ioOperationNumber = "OP10";
    [ObservableProperty] private int ioSaveIntervalMinutes = 5;
    [ObservableProperty] private string ioImportSummary = "尚未导入 IO 表";
    [ObservableProperty] private string generatedIoOutputDirectory = string.Empty;
    [ObservableProperty] private GeneratedProgramArtifact? selectedGeneratedIoProgram;
    [ObservableProperty] private string autoProgramName = "主装配流程";
    [ObservableProperty] private string autoProgramStation = "Station1";
    [ObservableProperty] private string generatedAutoOutputDirectory = string.Empty;
    [ObservableProperty] private GeneratedProgramArtifact? selectedGeneratedAutoProgram;

    [ObservableProperty] private OpcUaBrowseNode? selectedOpcUaBrowseNode;
    [ObservableProperty] private string selectedOpcUaNodeValue = "--";
    [ObservableProperty] private string selectedOpcUaNodeStatus = "未读取";
    [ObservableProperty] private string selectedOpcUaNodeTimestamp = "--";
    [ObservableProperty] private string opcUaBrowserStatus = "连接 OPC UA 后，可在这里浏览服务器节点。";

    public ICollectionView ManualCylinderBlocksView { get; }
    public IEnumerable<ManualCylinderBlockItem> ManualCylinderBlockCards =>
        ManualCylinderBlocks
            .GroupBy(item => item.CylinderIndex)
            .Select(group => group.OrderBy(item => item.DisplayOrder).First())
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.CylinderIndex);

    public MainViewModel()
    {
        BuildNavigation();
        DesignerElements.CollectionChanged += DesignerElements_CollectionChanged;
        DesignerPages.CollectionChanged += DesignerPages_CollectionChanged;
        Parameters.CollectionChanged += Parameters_CollectionChanged;
        IoTableRows.CollectionChanged += (_, _) => RefreshIoGenerationSummary();
        ManualCylinderBlocks.CollectionChanged += ManualCylinderBlocks_CollectionChanged;
        GeneratedIoPrograms.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGeneratedIoPrograms));
        GeneratedAutoPrograms.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGeneratedAutoPrograms));
        AutoProgramFlowNodes.CollectionChanged += (_, _) => RefreshAutoProgramSummary();
        AlarmHistory.CollectionChanged += (_, _) => RefreshAlarmStatistics();
        CurrentAlarms.CollectionChanged += (_, _) => RefreshAlarmStatistics();
        ManualCylinderBlocksView = CollectionViewSource.GetDefaultView(ManualCylinderBlocks);
        ManualCylinderBlocksView.SortDescriptions.Add(new SortDescription(nameof(ManualCylinderBlockItem.DisplayOrder), ListSortDirection.Ascending));
        ManualCylinderBlocksView.SortDescriptions.Add(new SortDescription(nameof(ManualCylinderBlockItem.CylinderIndex), ListSortDirection.Ascending));
        _subscriptionTimer = new DispatcherTimer();
        _subscriptionTimer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
        _subscriptionTimer.Tick += async (_, _) => await AutoRefreshTickAsync();
        _opcUaService.TagValueChanged += OpcUaService_TagValueChanged;
        SeedDemoData();
        SeedDesignerData();
        EnsureDefaultManualCylinderBlock();
        SeedParameters();
        ParametersView.Filter = FilterParameterItem;
        RefreshParameterPermissions();
        RefreshAlarmStatistics();
        RefreshMonitorView();
        SeedFlowSteps();
        SeedRecipes();
        SeedTrendSamples();
        RefreshFlowView();
        RefreshFlowIssueSummaries();
        SeedAutoProgramFlow();
        _ = InitializeAsync();
    }

    public ICollectionView MonitorTagsView => CollectionViewSource.GetDefaultView(Tags);
    public ICollectionView ParametersView => CollectionViewSource.GetDefaultView(Parameters);
    public ICollectionView AlarmStatisticsView => CollectionViewSource.GetDefaultView(AlarmStatistics);
    public ICollectionView FlowStepsView => CollectionViewSource.GetDefaultView(FlowSteps);
    public string CommunicationStatus => _opcUaService.ConnectionStatus;
    public int TagCount => Tags.Count;

    private async Task InitializeAsync()
    {
        try
        {
            await LoadConfigAsync();
            await LoadParametersAsync();
        }
        catch (Exception ex)
        {
            SystemMessage = $"启动加载配置失败：{ex.Message}";
            AddLog("配置", SystemMessage, "Error");
        }
    }
    public int AlarmCount => CurrentAlarms.Count(a => a.Active);
    public string DesignerModeText => IsRuntimeMode ? "运行态" : "设计态";
    public bool HasClipboard => _clipboardElement is not null;
    public bool CanEditParameters => CurrentUserRole >= UserRole.Engineer;
    public bool CanAdmin => CurrentUserRole == UserRole.Administrator;
    public bool CanOperateDevices => CurrentUserRole >= UserRole.Operator;
    public bool IsDesignMode => !IsRuntimeMode;
    public bool IsRuntimeDashboardVisible => IsRuntimeMode;
    public string RuntimeHeaderText => IsRuntimeMode ? "正式运行页" : "当前处于设计态";
    public string CurrentRoleText => CurrentUserRole switch
    {
        UserRole.Operator => "操作员",
        UserRole.Engineer => "工程师",
        UserRole.Administrator => "管理员",
        _ => "未知"
    };
    public int ActiveAlarmCount => CurrentAlarms.Count(a => a.Active);
    public int UnacknowledgedAlarmCount => CurrentAlarms.Count(a => a.Active && !a.Acknowledged);
    public int ProductionCount => GetIntTag("Production_Count", 1280);
    public int GoodCount => GetIntTag("Production_GoodCount", 1246);
    public int NgCount => GetIntTag("Production_NgCount", 34);
    public int ShiftProductionCount => GetIntTag("Shift_ProductionCount", 460);
    public int ShiftGoodCount => GetIntTag("Shift_GoodCount", 450);
    public int ShiftNgCount => GetIntTag("Shift_NgCount", 10);
    public int DailyProductionCount => GetIntTag("Daily_ProductionCount", 1280);
    public int DailyGoodCount => GetIntTag("Daily_GoodCount", 1246);
    public int DailyNgCount => GetIntTag("Daily_NgCount", 34);
    public int TargetCount => GetIntTag("Production_TargetCount", 1500);
    public double AvailabilityRate => CalculateAvailability();
    public double PerformanceRate => CalculatePerformance();
    public double QualityRate => CalculateQuality();
    public double OeeRate => Math.Round(AvailabilityRate * PerformanceRate * QualityRate / 10000.0, 1);
    public string DeviceStatusText => ActiveAlarmCount > 0 ? "报警中" : GetBoolTag("Device_Start") ? "运行中" : "待机";
    public string ShiftStatusText => ShiftProductionCount >= TargetCount ? "班次达成" : "班次生产中";
    public string CurrentRecipeText => string.IsNullOrWhiteSpace(SelectedRecipeName) ? (GetTagValue("Recipe_Name") == "--" ? "产品A" : GetTagValue("Recipe_Name")) : SelectedRecipeName;
    public string CurrentOrderText => GetTagValue("WorkOrder_No") == "--" ? "WO-20260404-01" : GetTagValue("WorkOrder_No");
    public string MotorStatusText => GetBoolTag("Motor1_Fault") ? "故障" : GetBoolTag("Y_RunLamp") ? "运行" : "停止";
    public string CylinderStatusText => CylinderForwardActive ? "前到位" : CylinderBackwardActive ? "后到位" : "切换中";
    public string CylinderDisplayName => string.IsNullOrWhiteSpace(CylinderConfiguredName) ? GetImportedCylinderDisplayName() : CylinderConfiguredName;
    public string CylinderHomeMaskButtonText => CylinderHomeMaskEnabled ? "原点屏蔽：开" : "原点屏蔽：关";
    public string CylinderWorkMaskButtonText => CylinderWorkMaskEnabled ? "动点屏蔽：开" : "动点屏蔽：关";
    public bool CylinderForwardActive => GetCylinderBool(CylinderWorkSensorTagName, ".Status.InWork", ".DevStatus.Sensor_Work", "Cylinder_FwdLS");
    public bool CylinderBackwardActive => GetCylinderBool(CylinderHomeSensorTagName, ".Status.InHome", ".DevStatus.Sensor_Home", "Cylinder_BwdLS");
    public bool CylinderOutputActive => GetCylinderBool(CylinderWorkCommandTagName, ".DevStatus.Valve_Work", ".Cmd.ManuToWork", "Cylinder_Extend");
    public string CylinderOutputText => CylinderOutputActive ? "伸出命令有效" : "回缩待命";
    public string CylinderCurrentStateText => ResolveCylinderCurrentStateText();
    public string CylinderActionHint => CylinderForwardActive ? "当前在前到位，适合执行回原或松开动作" : CylinderBackwardActive ? "当前在后到位，适合执行伸出或夹紧动作" : "气缸处于中间状态，等待动作完成后再切换";
    public bool CylinderInterlockBaseReady => !GetBoolTag("Alarm_EStop") && (!GetBoolTag("Y_RunLamp") || AllowManualCylinderWhenAuto) && (CylinderForwardActive || CylinderBackwardActive);
    public bool CylinderHomeInterlockActive => GetCylinderBool(CylinderHomeInterlockTagName, ".Parm.IC_Home", fallbackValue: CylinderInterlockBaseReady && CylinderForwardActive);
    public bool CylinderMoveInterlockActive => GetCylinderBool(CylinderWorkInterlockTagName, ".Parm.IC_Work", fallbackValue: CylinderInterlockBaseReady && CylinderBackwardActive);
    public string CylinderHomeInterlockText => CylinderHomeInterlockActive ? "允许回原" : GetCylinderBool(".Parm.IC_Home", fallbackValue: true) ? (CylinderBackwardActive ? "已在原点" : "回原互锁未满足") : "回原互锁未满足";
    public string CylinderMoveInterlockText => CylinderMoveInterlockActive ? "允许动点" : GetCylinderBool(".Parm.IC_Work", fallbackValue: true) ? (CylinderForwardActive ? "已在动点" : "动点互锁未满足") : "动点互锁未满足";
    public string CylinderInterlockHint => GetBoolTag("Alarm_EStop")
        ? "急停未复位，禁止操作气缸"
        : GetBoolTag("Y_RunLamp") && !AllowManualCylinderWhenAuto
            ? "设备自动运行中，当前联锁禁止手动气缸"
            : AllowManualCylinderWhenAuto
                ? "自动运行时允许手动切换气缸"
                : "当前允许手动操作气缸";
    public string AxisStatusText => GetBoolTag("Axis1_Alarm") ? "报警" : GetBoolTag("Axis1_Enable") ? $"使能 / 位置 {GetTagValue("Axis1_Pos")}" : "未使能";
    public string RobotStatusText => GetBoolTag("Robot_Pause") ? "暂停" : GetBoolTag("Robot_Run") ? "运行" : "待机";
    public bool IsDebugMode => GetBoolTag("Mode_Debug");
    public bool IsDryRunMode => GetBoolTag("Mode_DryRun");
    public bool IsBypassStationMode => GetBoolTag("Mode_BypassStation");
    public bool IsManualMode => GetBoolTag("Mode_Manual");
    public bool IsAutoMode => GetBoolTag("Mode_Auto");
    public string RunModeSummary => IsManualMode ? "人工模式" : IsAutoMode ? "自动模式" : "未选择";
    public string StartStopSummary => GetBoolTag("Device_Start") ? "设备已启动" : "设备已停止";
    public bool StartModeReady => IsManualMode || IsAutoMode;
    public bool StartAlarmReady => ActiveAlarmCount == 0;
    public bool StartInterlockReady => StartModeReady && StartAlarmReady;
    public string ProductionTrendSummary => $"班次 {ShiftProductionCount} / 日累计 {DailyProductionCount} / 目标 {TargetCount}";
    public string CurrentFlowStepText => $"STEP {CurrentFlowStepNo:000}";
    public string SelectedFlowSummary => SelectedFlowFilter == "全部" ? "多流程并行视图" : $"当前流程：{SelectedFlowFilter}";
    public string OeeTrendSummary => $"A {AvailabilityRate:F1}% / P {PerformanceRate:F1}% / Q {QualityRate:F1}% / OEE {OeeRate:F1}%";
    public string AlarmTrendSummary => $"活动 {ActiveAlarmCount} / 未确认 {UnacknowledgedAlarmCount} / 重点 {AlarmStatistics.Count(a => a.Count >= 3 || a.Active)}";
    public string TimeAxisSummary => $"采样点：最近 6 个周期 / 时间轴：{DateTime.Now.AddMinutes(-25):HH:mm} - {DateTime.Now:HH:mm}";
    public bool AllowManualCylinderWhenAuto => GetBoolParameter("自动运行允许手动气缸", false);
    public bool AllowManualStopperWhenAuto => GetBoolParameter("鑷姩杩愯鍏佽鎵嬪姩鎸″仠", false);
    public bool AllowRobotResetWhenRunning => GetBoolParameter("机械手运行时允许复位", false);
    public bool AllowAxisMoveWhenAlarm => GetBoolParameter("轴报警时允许运动", false);
    public double EstimatedDowntimeMinutes => AlarmStatistics.Where(a => a.Active || a.Count >= 3).Sum(a => a.Level switch { "Alarm" => a.Count * 12, "Error" => a.Count * 8, "Warning" => a.Count * 4, _ => a.Count * 2 });
    public int EstimatedProductionLoss => (int)Math.Round((EstimatedDowntimeMinutes / 60.0) * GetIntTag("Throughput_Hourly", 380));
    public string FocusAlarmHint => AlarmStatistics.FirstOrDefault()?.Message ?? "当前暂无重点报警";
    public string ProductionTrendPath => BuildSparklinePath(new double[] { Math.Max(0, ShiftProductionCount * 0.35), ShiftProductionCount * 0.5, ShiftProductionCount * 0.68, ShiftProductionCount * 0.8, ShiftProductionCount * 0.92, ShiftProductionCount });
    public string OeeTrendPath => BuildSparklinePath(new double[] { Math.Max(0, OeeRate - 9), OeeRate - 5, OeeRate - 3, OeeRate - 1, OeeRate + 1, OeeRate });
    public string AlarmTrendPath => BuildSparklinePath(new double[] { ActiveAlarmCount + 4, ActiveAlarmCount + 3, ActiveAlarmCount + 2, ActiveAlarmCount + 2, ActiveAlarmCount + 1, Math.Max(1, ActiveAlarmCount) });
    public string FlowStepTrendPath => BuildSparklinePath(FlowSteps.Take(6).Select(f => (double)f.StepNo).Reverse().DefaultIfEmpty(0));
    public string FlowIssueTrendPath => BuildSparklinePath(FlowIssueSummaries.Select((x, i) => (double)(i + 1) * 10).DefaultIfEmpty(0));
    public string FlowRankingSummary => BuildFlowRankingSummary();
    public string CurrentMonitorTitle => CurrentMonitorSubSection;
    public string CurrentManualTitle => CurrentManualSubSection;
    public string CurrentParameterTitle => CurrentParameterSubSection == "系统参数设定" ? "HMI变量和PLC变量绑定" : CurrentParameterSubSection;
    public string CurrentAlarmTitle => CurrentAlarmSubSection;
    public bool HasGeneratedIoPrograms => GeneratedIoPrograms.Count > 0;
    public string SelectedGeneratedIoProgramContent => SelectedGeneratedIoProgram?.Content ?? "生成完成后，程序预览会显示在这里。";
    public string IoGenerationHeadline => $"PLC 模板：{SelectedIoPlcTemplate} / 工位号：{IoOperationNumber}";
    public string IoGenerationCountSummary => $"输入 {IoTableRows.Count(r => !string.IsNullOrWhiteSpace(r.InputAddress))} 点 / 输出 {IoTableRows.Count(r => !string.IsNullOrWhiteSpace(r.OutputAddress))} 点";
    public bool HasGeneratedAutoPrograms => GeneratedAutoPrograms.Count > 0;
    public string SelectedGeneratedAutoProgramContent => SelectedGeneratedAutoProgram?.Content ?? "生成完成后，自动程序预览会显示在这里。";
    public string AutoProgramHeadline => $"流程：{AutoProgramName} / 工位：{AutoProgramStation} / 步序：{AutoProgramFlowNodes.Count}";
    public string AutoProgramSummary => AutoProgramFlowNodes.Count == 0
        ? "尚未配置自动流程节点"
        : $"已配置 {AutoProgramFlowNodes.Count} 个流程节点，支持绘制流程图并生成自动程序骨架。";
    public bool CanSaveIoTable => !string.IsNullOrWhiteSpace(_currentIoSourceFilePath);
    public string CurrentNavigationGroup => ResolveNavigationGroup(CurrentSection);
    public bool IsMonitorIoPageVisible => string.Equals(CurrentMonitorSubSection, "输入输出监控", StringComparison.Ordinal);
    public bool IsMonitorProgramPageVisible => string.Equals(CurrentMonitorSubSection, "程序监控", StringComparison.Ordinal);
    public bool IsMonitorCommunicationPageVisible => string.Equals(CurrentMonitorSubSection, "通讯状态监控", StringComparison.Ordinal);
    public bool IsMonitorProductionPageVisible => string.Equals(CurrentMonitorSubSection, "详细生产数据", StringComparison.Ordinal);
    public bool IsMonitorSinglePanelPageVisible => !IsMonitorProductionPageVisible;
    public bool IsManualCylinderPageVisible => string.Equals(CurrentManualSubSection, "气缸", StringComparison.Ordinal);
    public bool IsManualAxisPageVisible => string.Equals(CurrentManualSubSection, "轴", StringComparison.Ordinal);
    public bool IsManualRobotPageVisible => string.Equals(CurrentManualSubSection, "机械手", StringComparison.Ordinal);
    public bool IsManualMotorPageVisible => string.Equals(CurrentManualSubSection, "电机", StringComparison.Ordinal);
    public bool IsManualStopperPageVisible => string.Equals(CurrentManualSubSection, "挡停", StringComparison.Ordinal);
    public bool IsParameterSystemPageVisible => string.Equals(CurrentParameterSubSection, "系统参数设定", StringComparison.Ordinal);
    public bool IsParameterAxisPageVisible => string.Equals(CurrentParameterSubSection, "轴参数设定", StringComparison.Ordinal);
    public bool IsParameterCylinderPageVisible => string.Equals(CurrentParameterSubSection, "气缸参数设定", StringComparison.Ordinal);
    public bool IsParameterVacuumPageVisible => string.Equals(CurrentParameterSubSection, "真空参数设定", StringComparison.Ordinal);
    public bool IsParameterSensorPageVisible => string.Equals(CurrentParameterSubSection, "传感器参数设定", StringComparison.Ordinal);
    public bool IsAlarmCurrentPageVisible => string.Equals(CurrentAlarmSubSection, "当前报警", StringComparison.Ordinal);
    public bool IsAlarmHistoryPageVisible => string.Equals(CurrentAlarmSubSection, "历史报警", StringComparison.Ordinal);
    public bool IsAlarmLogPageVisible => string.Equals(CurrentAlarmSubSection, "日志", StringComparison.Ordinal);
    public bool IsAlarmStatisticsPageVisible => string.Equals(CurrentAlarmSubSection, "报警统计", StringComparison.Ordinal);
    public string CurrentDesignerTitle => CurrentDesignerSubSection;
public bool IsDesignerCanvasPageVisible => string.Equals(CurrentDesignerSubSection, "画布设计", StringComparison.Ordinal);
public bool IsDesignerIoProgramPageVisible => string.Equals(CurrentDesignerSubSection, "手动程序生成", StringComparison.Ordinal);
public bool IsDesignerAutoProgramPageVisible => string.Equals(CurrentDesignerSubSection, "自动程序生成", StringComparison.Ordinal);
    public bool IsSelectedDesignerElementButtonLike => SelectedDesignerElement is not null
        && SelectedDesignerElement.ElementType is "Button" or "Motor" or "Cylinder" or "Stopper" or "Robot" or "PageButton";
    public bool IsSelectedDesignerElementTagBindable => SelectedDesignerElement is not null
        && SelectedDesignerElement.ElementType is "Button" or "Indicator" or "ValueDisplay" or "AlarmBanner" or "Motor" or "Cylinder" or "Axis" or "Robot" or "Stopper";
    public bool IsSelectedDesignerElementNavigationAction => SelectedDesignerElement?.CommandBinding == "页面跳转";

    partial void OnIsRuntimeModeChanged(bool value)
    {
        OnPropertyChanged(nameof(DesignerModeText));
        OnPropertyChanged(nameof(IsDesignMode));
        OnPropertyChanged(nameof(IsRuntimeDashboardVisible));
        OnPropertyChanged(nameof(RuntimeHeaderText));
        SystemMessage = value ? "已切换到运行态" : "已切换到设计态";
        UpdateAutoRefreshState();
    }

    partial void OnCurrentUserRoleChanged(UserRole value)
    {
        LoginUser = CurrentRoleText;
        OnPropertyChanged(nameof(CanEditParameters));
        OnPropertyChanged(nameof(CanAdmin));
        OnPropertyChanged(nameof(CanOperateDevices));
        OnPropertyChanged(nameof(CurrentRoleText));
        RefreshParameterPermissions();
    }

    partial void OnCurrentMonitorSubSectionChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentMonitorTitle));
        OnPropertyChanged(nameof(IsMonitorIoPageVisible));
        OnPropertyChanged(nameof(IsMonitorProgramPageVisible));
        OnPropertyChanged(nameof(IsMonitorCommunicationPageVisible));
        OnPropertyChanged(nameof(IsMonitorProductionPageVisible));
        OnPropertyChanged(nameof(IsMonitorSinglePanelPageVisible));
    }

    partial void OnCurrentManualSubSectionChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentManualTitle));
        OnPropertyChanged(nameof(IsManualCylinderPageVisible));
        OnPropertyChanged(nameof(IsManualAxisPageVisible));
        OnPropertyChanged(nameof(IsManualRobotPageVisible));
        OnPropertyChanged(nameof(IsManualMotorPageVisible));
        OnPropertyChanged(nameof(IsManualStopperPageVisible));
    }

    partial void OnCurrentParameterSubSectionChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentParameterTitle));
        OnPropertyChanged(nameof(IsParameterSystemPageVisible));
        OnPropertyChanged(nameof(IsParameterAxisPageVisible));
        OnPropertyChanged(nameof(IsParameterCylinderPageVisible));
        OnPropertyChanged(nameof(IsParameterVacuumPageVisible));
        OnPropertyChanged(nameof(IsParameterSensorPageVisible));
        ParametersView.Refresh();
    }

    partial void OnCurrentAlarmSubSectionChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentAlarmTitle));
        OnPropertyChanged(nameof(IsAlarmCurrentPageVisible));
        OnPropertyChanged(nameof(IsAlarmHistoryPageVisible));
        OnPropertyChanged(nameof(IsAlarmLogPageVisible));
        OnPropertyChanged(nameof(IsAlarmStatisticsPageVisible));
    }

    partial void OnCurrentDesignerSubSectionChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentDesignerTitle));
        OnPropertyChanged(nameof(IsDesignerCanvasPageVisible));
        OnPropertyChanged(nameof(IsDesignerIoProgramPageVisible));
        OnPropertyChanged(nameof(IsDesignerAutoProgramPageVisible));
    }

    partial void OnCurrentSectionChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentNavigationGroup));
        var targetTabIndex = ResolveTabIndex(value);
        if (SelectedTabIndex != targetTabIndex)
        {
            SelectedTabIndex = targetTabIndex;
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        var section = GetSectionNameByTabIndex(value);
        if (ResolveTabIndex(CurrentSection) == value)
        {
            return;
        }

        if (!string.Equals(CurrentSection, section, StringComparison.Ordinal))
        {
            Navigate(section);
        }
    }

    partial void OnSelectedOpcUaBrowseNodeChanged(OpcUaBrowseNode? value)
    {
        if (value is null || value.IsPlaceholder)
        {
            return;
        }

        _ = RefreshSelectedOpcUaNodeAsync();
    }

    partial void OnSelectedMonitorCategoryChanged(string value) => RefreshMonitorView();
    partial void OnSelectedAlarmLevelChanged(string value) => RefreshAlarmStatistics();
    partial void OnSelectedAlarmTimeRangeChanged(string value) => RefreshAlarmStatistics();
    partial void OnShowOnlyFocusAlarmsChanged(bool value) => RefreshAlarmStatistics();
    partial void OnSelectedFlowFilterChanged(string value) => RefreshFlowView();
    partial void OnSelectedFlowTimeRangeChanged(string value) => RefreshFlowView();
    partial void OnSelectedFlowStepFilterChanged(string value) => RefreshFlowView();
    partial void OnShowOnlyAbnormalFlowChanged(bool value) => RefreshFlowView();
    partial void OnSelectedIoPlcTemplateChanged(string value) => OnPropertyChanged(nameof(IoGenerationHeadline));
    partial void OnIoOperationNumberChanged(string value) => OnPropertyChanged(nameof(IoGenerationHeadline));
    partial void OnAutoProgramNameChanged(string value) => OnPropertyChanged(nameof(AutoProgramHeadline));
    partial void OnAutoProgramStationChanged(string value) => OnPropertyChanged(nameof(AutoProgramHeadline));
    partial void OnCylinderHomeMaskEnabledChanged(bool value) => OnPropertyChanged(nameof(CylinderHomeMaskButtonText));
    partial void OnCylinderWorkMaskEnabledChanged(bool value) => OnPropertyChanged(nameof(CylinderWorkMaskButtonText));
    partial void OnCylinderConfiguredNameChanged(string value) => OnPropertyChanged(nameof(CylinderDisplayName));
    partial void OnCylinderHomeCommandTagNameChanged(string value) => RefreshCylinderBindingProperties();
    partial void OnCylinderWorkCommandTagNameChanged(string value) => RefreshCylinderBindingProperties();
    partial void OnCylinderHomeSensorTagNameChanged(string value) => RefreshCylinderBindingProperties();
    partial void OnCylinderWorkSensorTagNameChanged(string value) => RefreshCylinderBindingProperties();
    partial void OnCylinderHomeInterlockTagNameChanged(string value) => RefreshCylinderBindingProperties();
    partial void OnCylinderWorkInterlockTagNameChanged(string value) => RefreshCylinderBindingProperties();
    partial void OnSelectedDesignerElementChanged(DesignerElement? value)
    {
        if (_designerSelectionSubscription is not null)
        {
            _designerSelectionSubscription.PropertyChanged -= SelectedDesignerElement_PropertyChanged;
        }

        _designerSelectionSubscription = value;
        if (_designerSelectionSubscription is not null)
        {
            _designerSelectionSubscription.PropertyChanged += SelectedDesignerElement_PropertyChanged;
        }

        OnPropertyChanged(nameof(IsSelectedDesignerElementButtonLike));
        OnPropertyChanged(nameof(IsSelectedDesignerElementTagBindable));
        OnPropertyChanged(nameof(IsSelectedDesignerElementNavigationAction));
    }

    private void SelectedDesignerElement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesignerElement.CommandBinding) or nameof(DesignerElement.ElementType))
        {
            OnPropertyChanged(nameof(IsSelectedDesignerElementButtonLike));
            OnPropertyChanged(nameof(IsSelectedDesignerElementTagBindable));
            OnPropertyChanged(nameof(IsSelectedDesignerElementNavigationAction));
        }
    }

    private void RefreshCylinderBindingProperties()
    {
        OnPropertyChanged(nameof(CylinderStatusText));
        OnPropertyChanged(nameof(CylinderForwardActive));
        OnPropertyChanged(nameof(CylinderBackwardActive));
        OnPropertyChanged(nameof(CylinderOutputActive));
        OnPropertyChanged(nameof(CylinderOutputText));
        OnPropertyChanged(nameof(CylinderCurrentStateText));
        OnPropertyChanged(nameof(CylinderDisplayName));
        OnPropertyChanged(nameof(CylinderActionHint));
        OnPropertyChanged(nameof(CylinderInterlockBaseReady));
        OnPropertyChanged(nameof(CylinderHomeInterlockActive));
        OnPropertyChanged(nameof(CylinderMoveInterlockActive));
        OnPropertyChanged(nameof(CylinderHomeInterlockText));
        OnPropertyChanged(nameof(CylinderMoveInterlockText));
        OnPropertyChanged(nameof(CylinderInterlockHint));
    }
    partial void OnIoSaveIntervalMinutesChanged(int value)
    {
        if (value < 1)
        {
            IoSaveIntervalMinutes = 1;
        }
    }
    partial void OnSelectedGeneratedIoProgramChanged(GeneratedProgramArtifact? value) => OnPropertyChanged(nameof(SelectedGeneratedIoProgramContent));
    partial void OnSelectedGeneratedAutoProgramChanged(GeneratedProgramArtifact? value) => OnPropertyChanged(nameof(SelectedGeneratedAutoProgramContent));
    partial void OnAutoRefreshEnabledChanged(bool value) => UpdateAutoRefreshState();
    partial void OnUseOpcSubscriptionChanged(bool value) => UpdateAutoRefreshState();
    partial void OnSelectedRecipeNameChanged(string value) => RefreshActiveRecipeParameters();

    partial void OnRefreshIntervalMsChanged(int value)
    {
        if (value <= 100)
        {
            RefreshIntervalMs = 100;
            return;
        }
        _subscriptionTimer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
        UpdateAutoRefreshState();
    }

    partial void OnSelectedDesignerPageChanged(DesignerPage? value)
    {
        if (value is null) return;
        LoadPageToCanvas(value);
    }

    [RelayCommand]
    private void Login(string? roleName)
    {
        var role = roleName switch
        {
            "Operator" => UserRole.Operator,
            "Engineer" => UserRole.Engineer,
            "Administrator" => UserRole.Administrator,
            _ => UserRole.Operator
        };

        var ok = role switch
        {
            UserRole.Operator => true,
            UserRole.Engineer => LoginPassword == "123456",
            UserRole.Administrator => LoginPassword == "admin888",
            _ => false
        };

        if (!ok)
        {
            SystemMessage = "登录失败：密码错误";
            return;
        }

        CurrentUserRole = role;
        LoginPassword = string.Empty;
        SystemMessage = $"已登录为：{CurrentRoleText}";
        AddLog("登录", SystemMessage, "Info");
        AddAudit("登录", CurrentRoleText, "成功", SystemMessage);
    }

    [RelayCommand]
    private void Logout()
    {
        CurrentUserRole = UserRole.Operator;
        LoginPassword = string.Empty;
        SystemMessage = "已退出到操作员权限";
        AddLog("登录", SystemMessage, "Info");
        AddAudit("登录", "操作员", "退出", SystemMessage);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            if (!string.Equals(Connection.Protocol, "OPC UA", StringComparison.OrdinalIgnoreCase))
            {
                SystemMessage = $"当前版本仅支持 OPC UA 通讯，暂不支持 {Connection.Protocol}";
                AddLog("通讯", SystemMessage, "Warning");
                return;
            }

            await _opcUaService.ConnectAsync(Connection);
            SystemMessage = $"连接成功：{Connection.GetEndpointUrl()}";
            OnPropertyChanged(nameof(CommunicationStatus));
            await LoadOpcUaBrowserRootAsync();
            await RefreshTagsAsync();
            await LoadAlarmHistoryAsync();
            UpdateAutoRefreshState();
            AddLog("通讯", SystemMessage, "Info");
        }
        catch (Exception ex)
        {
            SystemMessage = $"连接失败：{ex.Message}";
            AddLog("通讯", SystemMessage, "Error");
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _subscriptionTimer.Stop();
        await _opcUaService.DisconnectAsync();
        OpcUaBrowserNodes.Clear();
        SelectedOpcUaBrowseNode = null;
        SelectedOpcUaNodeValue = "--";
        SelectedOpcUaNodeStatus = "未读取";
        SelectedOpcUaNodeTimestamp = "--";
        OpcUaBrowserStatus = "连接 OPC UA 后，可在这里浏览服务器节点。";
        SystemMessage = "宸叉柇寮€ OPC UA 杩炴帴";
        OnPropertyChanged(nameof(CommunicationStatus));
        AddLog("通讯", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task RefreshTagsAsync()
    {
        if (_isRefreshing) return;
        try
        {
            _isRefreshing = true;
            var values = await _opcUaService.ReadTagsAsync(Tags);
            foreach (var tag in Tags)
            {
                if (values.TryGetValue(tag.Name, out var value))
                {
                    tag.CurrentValue = value;
                    EvaluateTagState(tag);
                    EvaluateEvents(tag);
                }
            }
            UpdateRuntimeVisuals();
            RefreshMonitorView();
            UpdateRuntimeVisuals();
            RefreshAlarmStatistics();
            SimulateFlowProgress();
            await SaveTrendHistoryAsync();
        SystemMessage = "变量刷新完成";
        }
        catch (Exception ex)
        {
            SystemMessage = $"变量刷新失败：{ex.Message}";
            AddLog("监控", SystemMessage, "Error");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task LoadOpcUaBrowserRootAsync()
    {
        try
        {
            OpcUaBrowserNodes.Clear();
            foreach (var node in await _opcUaService.BrowseNodeAsync())
            {
                OpcUaBrowserNodes.Add(node);
            }

            OpcUaBrowserStatus = OpcUaBrowserNodes.Count == 0
                ? "当前服务器没有返回可浏览节点。"
                : $"已加载根节点，共 {OpcUaBrowserNodes.Count} 项。";
        }
        catch (Exception ex)
        {
            OpcUaBrowserStatus = $"节点浏览失败：{ex.Message}";
            AddLog("OPC UA", OpcUaBrowserStatus, "Error");
        }
    }

    [RelayCommand]
    private async Task ExpandOpcUaBrowserNodeAsync(OpcUaBrowseNode? node)
    {
        if (node is null || node.IsPlaceholder || !node.HasChildren || node.IsLoaded)
        {
            return;
        }

        try
        {
            node.Children.Clear();
            foreach (var child in await _opcUaService.BrowseNodeAsync(node.NodeId))
            {
                node.Children.Add(child);
            }

            node.IsLoaded = true;
            OpcUaBrowserStatus = $"已展开 {node.DisplayName}";
        }
        catch (Exception ex)
        {
            node.Children.Clear();
            node.IsLoaded = false;
            OpcUaBrowserStatus = $"展开节点失败：{ex.Message}";
            AddLog("OPC UA", OpcUaBrowserStatus, "Error");
        }
    }

    [RelayCommand]
    private async Task RefreshSelectedOpcUaNodeAsync()
    {
        if (SelectedOpcUaBrowseNode is null || SelectedOpcUaBrowseNode.IsPlaceholder)
        {
            return;
        }

        if (!string.Equals(SelectedOpcUaBrowseNode.NodeClass, "Variable", StringComparison.OrdinalIgnoreCase))
        {
            SelectedOpcUaNodeValue = "--";
        SelectedOpcUaNodeStatus = "当前节点不是变量节点";
            SelectedOpcUaNodeTimestamp = "--";
            SelectedOpcUaBrowseNode.DataType = "--";
            OpcUaBrowserStatus = $"宸查€変腑 {SelectedOpcUaBrowseNode.DisplayName}";
            return;
        }

        try
        {
            var result = await _opcUaService.ReadNodeAsync(SelectedOpcUaBrowseNode.NodeId);
            SelectedOpcUaBrowseNode.DataType = result.DataType;
            SelectedOpcUaBrowseNode.Value = result.Value;
            SelectedOpcUaNodeValue = string.IsNullOrWhiteSpace(result.Value) ? "(绌?" : result.Value;
            SelectedOpcUaNodeStatus = result.StatusCode;
            SelectedOpcUaNodeTimestamp = result.Timestamp;
            OpcUaBrowserStatus = $"已读取节点：{SelectedOpcUaBrowseNode.DisplayName}";
        }
        catch (Exception ex)
        {
            SelectedOpcUaNodeValue = "--";
            SelectedOpcUaNodeStatus = $"读取失败：{ex.Message}";
            SelectedOpcUaNodeTimestamp = "--";
            OpcUaBrowserStatus = SelectedOpcUaNodeStatus;
            AddLog("OPC UA", OpcUaBrowserStatus, "Error");
        }
    }

    [RelayCommand]
    private void AddSelectedOpcUaNodeAsTag()
    {
        if (SelectedOpcUaBrowseNode is null || SelectedOpcUaBrowseNode.IsPlaceholder)
        {
            SystemMessage = "请先选择一个 OPC UA 节点。";
            return;
        }

        if (!string.Equals(SelectedOpcUaBrowseNode.NodeClass, "Variable", StringComparison.OrdinalIgnoreCase))
        {
            SystemMessage = "只有变量节点才能加入变量表。";
            return;
        }

        if (Tags.Any(tag => string.Equals(tag.NodeId, SelectedOpcUaBrowseNode.NodeId, StringComparison.OrdinalIgnoreCase)))
        {
            SystemMessage = "该节点已经在变量表中。";
            return;
        }

        Tags.Add(new TagItem
        {
            Name = string.IsNullOrWhiteSpace(SelectedOpcUaBrowseNode.DisplayName) ? $"Tag_{Tags.Count + 1}" : SelectedOpcUaBrowseNode.DisplayName,
            NodeId = SelectedOpcUaBrowseNode.NodeId,
            DataType = SelectedOpcUaBrowseNode.DataType,
            Category = "OPC UA Browser",
            Group = "Imported",
            Direction = "Input",
            CurrentValue = SelectedOpcUaNodeValue == "(绌?" ? string.Empty : SelectedOpcUaNodeValue,
            Description = "由内置 OPC UA 浏览器导入",
            IsWritable = false
        });

        RefreshMonitorView();
        SystemMessage = $"已加入变量表：{SelectedOpcUaBrowseNode.DisplayName}";
    }

    [RelayCommand]
    private async Task ResetShiftCountersAsync()
    {
        if (!RequestConfirmation("确认操作", "确认执行班次计数清零吗？")) return;
        SetTagValue("Shift_ProductionCount", "0");
        SetTagValue("Shift_GoodCount", "0");
        SetTagValue("Shift_NgCount", "0");
        AddLog("生产", "班次计数已清零", "Info");
        UpdateRuntimeVisuals();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ResetDailyCountersAsync()
    {
        if (!RequestConfirmation("确认操作", "确认执行日累计计数清零吗？")) return;
        SetTagValue("Daily_ProductionCount", "0");
        SetTagValue("Daily_GoodCount", "0");
        SetTagValue("Daily_NgCount", "0");
        AddLog("生产", "日累计计数已清零", "Info");
        UpdateRuntimeVisuals();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ExportProductionReportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV 鏂囦欢|*.csv",
            FileName = $"production-report-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("项目,值");
        sb.AppendLine($"工单号,{CurrentOrderText}");
        sb.AppendLine($"配方,{CurrentRecipeText}");
        sb.AppendLine($"总产量,{ProductionCount}");
        sb.AppendLine($"良品,{GoodCount}");
        sb.AppendLine($"不良,{NgCount}");
        sb.AppendLine($"班次产量,{ShiftProductionCount}");
        sb.AppendLine($"日累计,{DailyProductionCount}");
        sb.AppendLine($"目标,{TargetCount}");
        sb.AppendLine($"Availability,{AvailabilityRate.ToString("F1", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Performance,{PerformanceRate.ToString("F1", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Quality,{QualityRate.ToString("F1", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"OEE,{OeeRate.ToString("F1", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"预计停机分钟,{EstimatedDowntimeMinutes.ToString("F1", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"预计影响产量,{EstimatedProductionLoss}");
        sb.AppendLine();
        sb.AppendLine("报警来源,级别,累计次数,状态,结论");
        foreach (var item in AlarmStatistics)
        {
            sb.AppendLine($"{item.Source},{item.Level},{item.Count},{item.State},{item.Message}");
        }

        await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), Encoding.UTF8);

        var excelPath = Path.ChangeExtension(dialog.FileName, ".xls");
        var html = new StringBuilder();
        html.AppendLine("<html><meta charset='utf-8'><body>");
        html.AppendLine("<table border='1'><tr><th>项目</th><th>值</th></tr>");
        html.AppendLine($"<tr><td>工单号</td><td>{CurrentOrderText}</td></tr>");
        html.AppendLine($"<tr><td>配方</td><td>{CurrentRecipeText}</td></tr>");
        html.AppendLine($"<tr><td>总产量</td><td>{ProductionCount}</td></tr>");
        html.AppendLine($"<tr><td>良品</td><td>{GoodCount}</td></tr>");
        html.AppendLine($"<tr><td>不良</td><td>{NgCount}</td></tr>");
        html.AppendLine($"<tr><td>OEE</td><td>{OeeRate:F1}%</td></tr>");
        html.AppendLine($"<tr><td>预计停机分钟</td><td>{EstimatedDowntimeMinutes:F1}</td></tr>");
        html.AppendLine($"<tr><td>预计影响产量</td><td>{EstimatedProductionLoss}</td></tr>");
        html.AppendLine("</table><br/>");
        html.AppendLine("<table border='1'><tr><th>报警来源</th><th>级别</th><th>累计次数</th><th>状态</th><th>建议</th><th>原因归档</th></tr>");
        foreach (var item in AlarmStatistics)
        {
            html.AppendLine($"<tr><td>{item.Source}</td><td>{item.Level}</td><td>{item.Count}</td><td>{item.State}</td><td>{item.HandlingSuggestion}</td><td>{item.CauseArchive}</td></tr>");
        }
        html.AppendLine("</table></body></html>");
        await File.WriteAllTextAsync(excelPath, html.ToString(), Encoding.UTF8);

        SystemMessage = "报表已导出：CSV + Excel 兼容文件";
        AddLog("报表", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task ImportTagsAsync()
    {
        var dialog = new OpenFileDialog { Filter = "CSV 文件|*.csv|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        var imported = await _csvImportService.ImportTagsAsync(dialog.FileName);
        Tags.Clear();
        foreach (var tag in imported) Tags.Add(tag);
        OnPropertyChanged(nameof(TagCount));
        RefreshMonitorView();
        SystemMessage = $"已导入变量表：{Path.GetFileName(dialog.FileName)}，共 {Tags.Count} 项";
        AddLog("配置", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task ImportXmlTagsAsync()
    {
        var dialog = new OpenFileDialog { Filter = "XML 文件|*.xml|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var imported = await _xmlImportService.ImportTagsAsync(dialog.FileName);
            Tags.Clear();
            foreach (var tag in imported) Tags.Add(tag);
            OnPropertyChanged(nameof(TagCount));
            RefreshMonitorView();
            SystemMessage = $"已导入 XML 变量表：{Path.GetFileName(dialog.FileName)}，共 {Tags.Count} 项";
            AddLog("配置", SystemMessage, "Info");
        }
        catch (Exception ex)
        {
            ShowPopup("XML导入失败", ex.Message, "Warning");
        }
    }

    [RelayCommand]
    private void JumpToAlarmPage()
    {
        Navigate("报警画面");
        SectionJumpRequested?.Invoke("报警画面", null);
    }

    [RelayCommand]
    private void JumpToAuditPage()
    {
        Navigate("操作审计");
        SectionJumpRequested?.Invoke("操作审计", null);
    }

    [RelayCommand]
    private void JumpToAlarmByKeyword(string? keyword)
    {
        JumpAlarmKeyword = keyword ?? string.Empty;
        HighlightAlarm(JumpAlarmKeyword);
        Navigate("当前报警");
        SectionJumpRequested?.Invoke("报警画面", JumpAlarmKeyword);
        HighlightRequested?.Invoke("Alarm", JumpAlarmKeyword);
    }

    [RelayCommand]
    private void JumpToFlowByAlarm(string? alarmKeyword)
    {
        if (string.IsNullOrWhiteSpace(alarmKeyword))
        {
            return;
        }

        var matched = FlowSteps.FirstOrDefault(x => x.RelatedAlarm.Contains(alarmKeyword, StringComparison.OrdinalIgnoreCase));
        if (matched is not null)
        {
            SelectedFlowFilter = matched.FlowName;
            SelectedFlowStepFilter = matched.StepNo.ToString();
            ShowOnlyAbnormalFlow = true;
            HighlightFlow(matched.FlowName, matched.StepNo);
            RefreshFlowView();
            HighlightRequested?.Invoke("Flow", $"{matched.FlowName}|{matched.StepNo}");
        }
        Navigate("程序监控");
        SectionJumpRequested?.Invoke("监视画面", alarmKeyword);
    }

    [RelayCommand]
    private async Task ExportFlowIssueReportAsync()
    {
        var dialog = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = $"flow-issue-report-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dialog.ShowDialog() != true) return;
        var sb = new StringBuilder();
        sb.AppendLine("Category,Name,Metric,Conclusion");
        foreach (var item in FlowIssueSummaries)
        {
            sb.AppendLine($"{item.Category},{item.Name},{item.Metric},{item.Conclusion}");
        }
        sb.AppendLine();
        sb.AppendLine("FlowName,StepNo,StartTime,EndTime,DurationSeconds,Result,RelatedAlarm");
        foreach (var item in FlowSteps.Where(x => x.IsAbnormal))
        {
            sb.AppendLine($"{item.FlowName},{item.StepNo},{item.StartTime:yyyy-MM-dd HH:mm:ss},{item.EndTime:yyyy-MM-dd HH:mm:ss},{item.DurationSeconds:F2},{item.Result},{item.RelatedAlarm}");
        }
        await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), Encoding.UTF8);
        SystemMessage = $"异常流程分析报告已导出：{dialog.FileName}";
        AddLog("流程分析", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task ImportFlowCsvAsync()
    {
        var dialog = new OpenFileDialog { Filter = "CSV 文件|*.csv|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        var items = await _flowLogCsvService.LoadAsync(dialog.FileName);
        if (items.Count == 0)
        {
            ShowPopup("导入失败", "未从 CSV 中读取到有效流程数据。", "Warning");
            return;
        }
        FlowSteps.Clear();
        foreach (var item in items.OrderByDescending(x => x.Time).Take(200)) FlowSteps.Add(item);
        var latest = FlowSteps.FirstOrDefault();
        if (latest is not null)
        {
            CurrentFlowStepNo = latest.StepNo;
            CurrentFlowComment = latest.Comment;
        }
        RefreshFlowView();
        RefreshFlowIssueSummaries();
        OnPropertyChanged(nameof(FlowStepTrendPath));
        OnPropertyChanged(nameof(SelectedFlowSummary));
        SystemMessage = $"已导入流程 CSV：{Path.GetFileName(dialog.FileName)}";
        AddLog("流程分析", SystemMessage, "Info");
    }

    [RelayCommand]
    private void AddCustomTag()
    {
        if (string.IsNullOrWhiteSpace(NewTagName) || string.IsNullOrWhiteSpace(NewTagNodeId))
        {
            SystemMessage = "璇峰～鍐欏彉閲忓悕鍜?NodeId";
            return;
        }

        Tags.Add(new TagItem
        {
            Name = NewTagName,
            NodeId = NewTagNodeId,
            DataType = NewTagDataType,
            Category = NewTagCategory,
            Group = NewTagGroup,
            Direction = NewTagDirection,
            IsAlarm = NewTagIsAlarm,
            IsWritable = NewTagIsWritable,
            Description = "自定义变量"
        });
        NewTagName = string.Empty;
        NewTagNodeId = string.Empty;
        OnPropertyChanged(nameof(TagCount));
        RefreshMonitorView();
        SystemMessage = "已新增自定义变量";
        AddLog("配置", SystemMessage, "Info");
    }

    [RelayCommand]
    private void AddEventBinding()
    {
        if (string.IsNullOrWhiteSpace(NewEventTagName) || string.IsNullOrWhiteSpace(NewEventName))
        {
            SystemMessage = "请填写事件绑定信息";
            return;
        }

        EventBindings.Add(new EventBinding
        {
            TagName = NewEventTagName,
            TriggerCondition = NewEventTriggerCondition,
            EventName = NewEventName,
            ActionTarget = NewEventTarget,
            ActionParameter = NewEventParameter,
            Description = "用户自定义事件"
        });
        SystemMessage = "已新增事件绑定";
        AddLog("配置", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task ManualWriteAsync()
    {
        var tag = Tags.FirstOrDefault(t => t.Name.Equals(ManualWriteTagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null) { SystemMessage = "未找到要写入的变量"; return; }
        if (!tag.IsWritable) { SystemMessage = "该变量不可写"; return; }
        try
        {
            object value = ConvertValue(ManualWriteValue, tag.DataType);
            await _opcUaService.WriteTagAsync(tag, value);
            tag.CurrentValue = ManualWriteValue;
            SystemMessage = $"写入成功：{tag.Name} = {ManualWriteValue}";
            AddLog("手动操作", SystemMessage, "Info");
            await RefreshTagsAsync();
        }
        catch (Exception ex)
        {
            SystemMessage = $"写入失败：{ex.Message}";
            AddLog("手动操作", SystemMessage, "Error");
        }
    }

    [RelayCommand]
    private async Task ToggleDeviceAsync(string? tagName)
    {
        if (!CanOperateDevices)
        {
            SystemMessage = "当前权限不足，无法操作设备";
            return;
        }

        if (string.IsNullOrWhiteSpace(tagName)) return;
        await ToggleBoundBooleanTagAsync(tagName, tagName);
    }

    [RelayCommand]
    private async Task CylinderMoveToHomeAsync(ManualCylinderBlockItem? block) => await SetCylinderPositionAsync(block, false, "气缸原点操作");

    [RelayCommand]
    private async Task CylinderMoveToWorkAsync(ManualCylinderBlockItem? block) => await SetCylinderPositionAsync(block, true, "气缸动点操作");

    [RelayCommand]
    private void ToggleCylinderHomeMask()
    {
        CylinderHomeMaskEnabled = !CylinderHomeMaskEnabled;
        SystemMessage = CylinderHomeMaskEnabled ? "已开启气缸原点屏蔽" : "已关闭气缸原点屏蔽";
    }

    [RelayCommand]
    private void ToggleCylinderWorkMask()
    {
        CylinderWorkMaskEnabled = !CylinderWorkMaskEnabled;
        SystemMessage = CylinderWorkMaskEnabled ? "已开启气缸动点屏蔽" : "已关闭气缸动点屏蔽";
    }

    [RelayCommand]
    private async Task SetDebugModeAsync() => await SetExclusiveModeAsync("Mode_Debug", "调试模式");

    [RelayCommand]
    private async Task SetDryRunModeAsync() => await SetExclusiveModeAsync("Mode_DryRun", "空跑模式");

    [RelayCommand]
    private async Task SetBypassStationModeAsync() => await SetExclusiveModeAsync("Mode_BypassStation", "过站模式");

    [RelayCommand]
    private async Task SetManualModeAsync() => await SetExclusiveModeAsync("Mode_Manual", "人工模式");

    [RelayCommand]
    private async Task SetAutoModeAsync() => await SetExclusiveModeAsync("Mode_Auto", "自动模式");

    [RelayCommand]
    private async Task StartDeviceAsync()
    {
        if (GetBoolTag("Device_Start"))
        {
            ShowPopup("提示", "设备已处于启动状态。", "Info");
            return;
        }
        if (ActiveAlarmCount > 0)
        {
            ShowPopup("操作条件不满足", "当前存在活动报警，不能启动设备。", "Interlock");
            return;
        }
        if (!IsManualMode && !IsAutoMode)
        {
            ShowPopup("操作条件不满足", "请先选择人工模式或自动模式，再启动设备。", "Warning");
            return;
        }

        await ToggleBoundBooleanTagAsync("Device_Start", "设备启动");
    }

    [RelayCommand]
    private async Task StopDeviceAsync()
    {
        if (!GetBoolTag("Device_Start"))
        {
            ShowPopup("提示", "设备当前已停止。", "Info");
            return;
        }

        await ToggleBoundBooleanTagAsync("Device_Start", "设备停止");
    }

    [RelayCommand]
    private async Task ResetAlarmFromHomeAsync()
    {
        if (!CanAdmin)
        {
            ShowPopup("权限不足", "仅管理员可在主页执行报警复位。", "Error");
            return;
        }

        await ResetAllAlarmsAsync();
    }

    [RelayCommand]
    private async Task ResetMotorFaultAsync()
    {
        if (!GetBoolTag("Motor1_Fault"))
        {
            ShowPopup("操作条件不满足", "当前电机无故障，无需执行故障复位。", "Warning");
            return;
        }
        if (!RequestConfirmation("确认复位", "确认执行电机故障复位吗？")) return;
        await PulseBooleanTagAsync("Motor1_Reset", "电机故障复位输出");
    }

    [RelayCommand]
    private async Task PauseRobotAsync()
    {
        if (!GetBoolTag("Robot_Run"))
        {
            ShowPopup("操作条件不满足", "机械手当前未运行，不能执行暂停操作。", "Warning");
            return;
        }
        await PulseBooleanTagAsync("Robot_Pause", "机械手暂停输出");
    }

    [RelayCommand]
    private async Task ResetRobotAsync()
    {
        if (GetBoolTag("Robot_Run") && !AllowRobotResetWhenRunning)
        {
            ShowPopup("操作条件不满足", "机械手运行中，请先停止后再复位。", "Warning");
            return;
        }
        if (!RequestConfirmation("确认复位", "确认执行机械手复位吗？")) return;
        await PulseBooleanTagAsync("Robot_Reset", "机械手复位输出");
    }

    [RelayCommand]
    private async Task ToggleAxisEnableAsync()
    {
        if (GetBoolTag("Axis1_Alarm"))
        {
            ShowPopup("操作条件不满足", "轴当前存在报警，请先复位报警后再进行使能。", "Warning");
            return;
        }
        await ToggleBoundBooleanTagAsync("Axis1_Enable", "轴使能");
    }

    [RelayCommand]
    private async Task AxisAlarmResetAsync()
    {
        if (!GetBoolTag("Axis1_Alarm"))
        {
            ShowPopup("操作条件不满足", "轴当前无报警，无需执行报警复位。", "Warning");
            return;
        }
        if (!RequestConfirmation("确认复位", "确认执行轴报警复位吗？")) return;
        await PulseBooleanTagAsync("Axis1_AlarmReset", "轴报警复位");
    }

    [RelayCommand]
    private async Task MoveAxisHomeAsync()
    {
        if (!GetBoolTag("Axis1_Enable"))
        {
            ShowPopup("操作条件不满足", "轴未使能，不能执行回零操作。", "Warning");
            return;
        }
        if (GetBoolTag("Axis1_Alarm") && !AllowAxisMoveWhenAlarm)
        {
            ShowPopup("操作条件不满足", "轴存在报警，不能执行回零操作。", "Warning");
            return;
        }
        await WriteAxisPositionAsync(0.0, "轴已回零");
    }

    [RelayCommand]
    private async Task JogAxisPositiveAsync()
    {
        if (!GetBoolTag("Axis1_Enable")) { ShowPopup("操作条件不满足", "轴未使能，不能执行 Jog+。", "Warning"); return; }
        if (GetBoolTag("Axis1_Alarm") && !AllowAxisMoveWhenAlarm) { ShowPopup("操作条件不满足", "轴存在报警，不能执行 Jog+。", "Warning"); return; }
        if (!double.TryParse(AxisJogDistance, out var jog)) { ShowPopup("参数错误", "Jog 距离格式错误，请输入有效数字。", "Warning"); return; }
        var current = GetAxisCurrentPosition();
        await WriteAxisPositionAsync(current + jog, $"轴 Jog+ {jog}");
    }

    [RelayCommand]
    private async Task JogAxisNegativeAsync()
    {
        if (!GetBoolTag("Axis1_Enable")) { ShowPopup("操作条件不满足", "轴未使能，不能执行 Jog-。", "Warning"); return; }
        if (GetBoolTag("Axis1_Alarm") && !AllowAxisMoveWhenAlarm) { ShowPopup("操作条件不满足", "轴存在报警，不能执行 Jog-。", "Warning"); return; }
        if (!double.TryParse(AxisJogDistance, out var jog)) { ShowPopup("参数错误", "Jog 距离格式错误，请输入有效数字。", "Warning"); return; }
        var current = GetAxisCurrentPosition();
        await WriteAxisPositionAsync(current - jog, $"轴 Jog- {jog}");
    }

    [RelayCommand]
    private async Task MoveAxisToTargetAsync()
    {
        if (!GetBoolTag("Axis1_Enable")) { ShowPopup("操作条件不满足", "轴未使能，不能执行定位移动。", "Warning"); return; }
        if (GetBoolTag("Axis1_Alarm") && !AllowAxisMoveWhenAlarm) { ShowPopup("操作条件不满足", "轴存在报警，不能执行定位移动。", "Warning"); return; }
        if (!double.TryParse(AxisTargetPosition, out var target)) { ShowPopup("参数错误", "目标位置格式错误，请输入有效数字。", "Warning"); return; }
        await WriteAxisPositionAsync(target, $"轴移动到 {target}");
    }

    [RelayCommand]
    private async Task SaveParametersAsync()
    {
        if (!CanEditParameters) { SystemMessage = "当前权限不足，无法修改参数"; return; }
        var illegal = Parameters.FirstOrDefault(p => !CanEditParameter(p));
        if (illegal is not null) { SystemMessage = $"存在超权限参数：{illegal.Name}"; return; }
        var path = Path.Combine(GetProjectRoot(), "config", "parameters.json");
        await _parameterService.SaveAsync(path, Parameters);
        SystemMessage = $"参数已保存：{path}";
        AddLog("参数", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task LoadParametersAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "parameters.json");
        var items = await _parameterService.LoadAsync(path);
        if (items.Count == 0)
        {
            RefreshParameterPermissions();
            SystemMessage = "未找到参数文件，已保留当前示例参数";
            return;
        }

        Parameters.Clear();
        foreach (var item in items) Parameters.Add(item);
        RefreshParameterPermissions();
        SystemMessage = "参数加载完成";
        AddLog("参数", SystemMessage, "Info");
    }

    [RelayCommand]
    private void AcknowledgeAllAlarms()
    {
        if (!CanOperateDevices) { SystemMessage = "当前权限不足，无法确认报警"; return; }
        foreach (var alarm in CurrentAlarms)
        {
            alarm.Acknowledged = true;
            alarm.AcknowledgedBy = LoginUser;
            alarm.State = alarm.Active ? "Acknowledged" : "Cleared";
            if (string.IsNullOrWhiteSpace(alarm.HandlingSuggestion)) alarm.HandlingSuggestion = BuildHandlingSuggestion(alarm.Source, alarm.Level);
            if (string.IsNullOrWhiteSpace(alarm.CauseArchive)) alarm.CauseArchive = BuildCauseArchive(alarm.Source);
        }
        RefreshAlarmStatistics();
        SystemMessage = "当前报警已全部确认";
        AddLog("报警", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task ResetAllAlarmsAsync()
    {
        if (!CanAdmin) { SystemMessage = "仅管理员可复位报警"; return; }

        foreach (var alarm in CurrentAlarms.ToList())
        {
            alarm.Active = false;
            alarm.Acknowledged = true;
            alarm.ClearTime = DateTime.Now;
            alarm.State = "Reset";
            AlarmHistory.Insert(0, new AlarmRecord
            {
                Time = DateTime.Now,
                Level = alarm.Level,
                Source = alarm.Source,
                Message = $"已复位：{alarm.Message}",
                Active = false,
                Acknowledged = true,
                ClearTime = DateTime.Now,
                State = "Reset",
                Count = alarm.Count
            });
        }

        CurrentAlarms.Clear();
        _activeAlarmMap.Clear();
        OnPropertyChanged(nameof(AlarmCount));
        RefreshAlarmStatistics();
        await SaveAlarmHistoryAsync();
        SystemMessage = "报警已全部复位";
        AddLog("报警", SystemMessage, "Warning");
    }

    [RelayCommand]
    private async Task SaveAlarmHistoryAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "alarm-history.json");
        await _alarmService.SaveHistoryAsync(path, AlarmHistory);
        SystemMessage = $"报警历史已保存：{path}";
    }

    [RelayCommand]
    private async Task LoadAlarmHistoryAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "alarm-history.json");
        var items = await _alarmService.LoadHistoryAsync(path);
        if (items.Count == 0) return;
        AlarmHistory.Clear();
        foreach (var item in items) AlarmHistory.Add(item);
        RefreshAlarmStatistics();
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "appsettings.json");
        var config = new AppConfig
        {
            Connection = Connection,
            Tags = Tags.ToList(),
            EventBindings = EventBindings.ToList(),
            IoGeneration = new IoGenerationSettings
            {
                PlcType = SelectedIoPlcTemplate,
                OperationNumber = IoOperationNumber
            },
            IoTableRows = IoTableRows.ToList()
        };
        await _configurationService.SaveAsync(path, config);
        SystemMessage = $"配置已保存：{path}";
        AddLog("配置", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task LoadConfigAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "appsettings.json");
        var config = await _configurationService.LoadAsync(path);
        if (config is null) { SystemMessage = "未找到配置文件"; return; }
        Connection = config.Connection;
        if (string.IsNullOrWhiteSpace(Connection.Protocol))
        {
            Connection.Protocol = "OPC UA";
        }
        Tags.Clear(); foreach (var tag in config.Tags) Tags.Add(tag);
        EventBindings.Clear(); foreach (var binding in config.EventBindings) EventBindings.Add(binding);
        SelectedIoPlcTemplate = string.IsNullOrWhiteSpace(config.IoGeneration?.PlcType) ? "汇川中型PLC" : config.IoGeneration.PlcType;
        IoOperationNumber = string.IsNullOrWhiteSpace(config.IoGeneration?.OperationNumber) ? "OP10" : config.IoGeneration.OperationNumber;
        IoTableRows.Clear();
        foreach (var row in config.IoTableRows ?? new List<IoTableRow>()) IoTableRows.Add(row);
        RefreshIoGenerationSummary();
        OnPropertyChanged(nameof(TagCount));
        RefreshMonitorView();
        RefreshAlarmStatistics();
        SystemMessage = "配置加载完成";
        AddLog("配置", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task ImportIoTableAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 IO 表",
            Filter = "IO 表 (*.csv;*.txt)|*.csv;*.txt|CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var importResult = await _ioTableImportService.ImportAsync(dialog.FileName);
            IoTableRows.Clear();
            foreach (var row in importResult.Rows) IoTableRows.Add(row);
            _currentIoSourceFilePath = importResult.SourceFilePath;
            _currentIoSourceEncodingCodePage = importResult.EncodingCodePage;
            _currentIoSourceHeaders = importResult.Headers;
            _importedIoRowsSnapshot.Clear();
            _importedIoRowsSnapshot.AddRange(CloneIoRows(importResult.Rows));
            _lastIoSavedSnapshot.Clear();
            _lastIoSavedFilePath = string.Empty;
            _lastIoHistoryFilePath = string.Empty;
            _lastIoSaveAt = DateTime.MinValue;
            GeneratedIoPrograms.Clear();
            SelectedGeneratedIoProgram = null;
            GeneratedIoOutputDirectory = string.Empty;
            IsRuntimeMode = false;
            SelectedTabIndex = ResolveTabIndex("设计器");
            CurrentDesignerSubSection = "手动程序生成";
            CurrentSection = "手动程序生成";
            RefreshIoGenerationSummary();
            OnPropertyChanged(nameof(CanSaveIoTable));
            SystemMessage = $"IO 表导入完成：{Path.GetFileName(dialog.FileName)}";
            AddLog("IO 生成", $"{SystemMessage}，共 {importResult.Rows.Count} 行。", "Info");
        }
        catch (Exception ex)
        {
            SystemMessage = $"IO 表导入失败：{ex.Message}";
            AddLog("IO 生成", SystemMessage, "Error");
            ShowPopup("导入失败", ex.Message, "Error");
        }
    }

    [RelayCommand]
    private void ClearIoTable()
    {
        IoTableRows.Clear();
        GeneratedIoPrograms.Clear();
        SelectedGeneratedIoProgram = null;
        GeneratedIoOutputDirectory = string.Empty;
        _currentIoSourceFilePath = string.Empty;
        _currentIoSourceEncodingCodePage = 65001;
        _currentIoSourceHeaders = new();
        _importedIoRowsSnapshot.Clear();
        _lastIoSavedSnapshot.Clear();
        _lastIoSavedFilePath = string.Empty;
        _lastIoHistoryFilePath = string.Empty;
        _lastIoSaveAt = DateTime.MinValue;
        RefreshIoGenerationSummary();
        OnPropertyChanged(nameof(CanSaveIoTable));
        SystemMessage = "IO 表已清空";
        AddLog("IO 生成", SystemMessage, "Warning");
    }

    [RelayCommand]
    private async Task SaveIoTableToSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentIoSourceFilePath))
        {
            ShowPopup("保存失败", "当前没有可保存的源文件目录，请先导入 IO 表。", "Warning");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_currentIoSourceFilePath) ?? GetApplicationRoot();
            var extension = Path.GetExtension(_currentIoSourceFilePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".csv";
            }

            var intervalMinutes = Math.Max(1, IoSaveIntervalMinutes);
            var shouldReuseSameFile = intervalMinutes < 5
                && !string.IsNullOrWhiteSpace(_lastIoSavedFilePath)
                && File.Exists(_lastIoSavedFilePath)
                && string.Equals(Path.GetDirectoryName(_lastIoSavedFilePath), directory, StringComparison.OrdinalIgnoreCase)
                && DateTime.Now - _lastIoSaveAt <= TimeSpan.FromMinutes(intervalMinutes);

            var filePath = shouldReuseSameFile
                ? _lastIoSavedFilePath
                : Path.Combine(directory, $"IO表{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
            var historyPath = Path.Combine(directory, "IO表_修改履历.txt");
            var baselineRows = shouldReuseSameFile && _lastIoSavedSnapshot.Count > 0
                ? _lastIoSavedSnapshot
                : _importedIoRowsSnapshot;
            var currentRows = CloneIoRows(IoTableRows);
            var changeLines = BuildIoChangeLog(baselineRows, currentRows);

            await _ioTableImportService.SaveAsync(filePath, IoTableRows, _currentIoSourceHeaders, _currentIoSourceEncodingCodePage);
            if (changeLines.Count > 0)
            {
                await File.AppendAllTextAsync(historyPath, BuildIoHistoryEntry(filePath, changeLines), Encoding.UTF8);
            }

            _lastIoSavedSnapshot.Clear();
            _lastIoSavedSnapshot.AddRange(currentRows);
            _lastIoSavedFilePath = filePath;
            _lastIoHistoryFilePath = historyPath;
            _lastIoSaveAt = DateTime.Now;

            SystemMessage = shouldReuseSameFile
                ? $"IO 表已更新：{Path.GetFileName(filePath)}"
                : $"IO 表已另存为：{Path.GetFileName(filePath)}";
            AddLog("IO 生成", SystemMessage, "Info");
        }
        catch (Exception ex)
        {
            SystemMessage = $"IO 表保存失败：{ex.Message}";
            AddLog("IO 生成", SystemMessage, "Error");
            ShowPopup("保存失败", ex.Message, "Error");
        }
    }

    private static List<IoTableRow> CloneIoRows(IEnumerable<IoTableRow> rows)
    {
        return rows.Select(row => new IoTableRow
        {
            InputAddress = row.InputAddress,
            InputComment = row.InputComment,
            OutputAddress = row.OutputAddress,
            OutputComment = row.OutputComment
        }).ToList();
    }

    private static List<string> BuildIoChangeLog(IReadOnlyList<IoTableRow> before, IReadOnlyList<IoTableRow> after)
    {
        var changes = new List<string>();
        var max = Math.Max(before.Count, after.Count);

        for (var index = 0; index < max; index++)
        {
            var oldRow = index < before.Count ? before[index] : null;
            var newRow = index < after.Count ? after[index] : null;
            var rowNo = index + 1;

            if (oldRow is null && newRow is not null)
            {
                if (HasIoRowContent(newRow))
                {
                    changes.Add($"第 {rowNo} 行新增：输入地址 {FormatIoValue(newRow.InputAddress)}，输入注释 {FormatIoValue(newRow.InputComment)}，输出地址 {FormatIoValue(newRow.OutputAddress)}，输出注释 {FormatIoValue(newRow.OutputComment)}");
                }

                continue;
            }

            if (oldRow is not null && newRow is null)
            {
                if (HasIoRowContent(oldRow))
                {
                    changes.Add($"第 {rowNo} 行删除：输入地址 {FormatIoValue(oldRow.InputAddress)}，输入注释 {FormatIoValue(oldRow.InputComment)}，输出地址 {FormatIoValue(oldRow.OutputAddress)}，输出注释 {FormatIoValue(oldRow.OutputComment)}");
                }

                continue;
            }

            if (oldRow is null || newRow is null)
            {
                continue;
            }

            AppendIoFieldChange(changes, rowNo, "输入地址", oldRow.InputAddress, newRow.InputAddress);
            AppendIoFieldChange(changes, rowNo, "输入注释", oldRow.InputComment, newRow.InputComment, newRow.InputAddress, oldRow.InputAddress);
            AppendIoFieldChange(changes, rowNo, "输出地址", oldRow.OutputAddress, newRow.OutputAddress);
            AppendIoFieldChange(changes, rowNo, "输出注释", oldRow.OutputComment, newRow.OutputComment, newRow.OutputAddress, oldRow.OutputAddress);
        }

        return changes;
    }

    private static void AppendIoFieldChange(ICollection<string> changes, int rowNo, string fieldName, string beforeValue, string afterValue, string? currentAddress = null, string? previousAddress = null)
    {
        if (string.Equals(beforeValue, afterValue, StringComparison.Ordinal))
        {
            return;
        }

        var addressNote = BuildIoAddressNote(fieldName, currentAddress, previousAddress);
        changes.Add($"第 {rowNo} 行 {fieldName}{addressNote}：{FormatIoValue(beforeValue)} -> {FormatIoValue(afterValue)}");
    }

    private static string BuildIoAddressNote(string fieldName, string? currentAddress, string? previousAddress)
    {
        if (!fieldName.Contains("注释", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var address = !string.IsNullOrWhiteSpace(currentAddress) ? currentAddress : previousAddress;
        return string.IsNullOrWhiteSpace(address) ? string.Empty : $"（地址 {address.Trim()}）";
    }

    private static bool HasIoRowContent(IoTableRow row)
    {
        return !string.IsNullOrWhiteSpace(row.InputAddress)
            || !string.IsNullOrWhiteSpace(row.InputComment)
            || !string.IsNullOrWhiteSpace(row.OutputAddress)
            || !string.IsNullOrWhiteSpace(row.OutputComment);
    }

    private static string FormatIoValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "（空）" : value.Trim();
    }

    private static string BuildIoHistoryEntry(string filePath, IReadOnlyCollection<string> changeLines)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 保存文件：{Path.GetFileName(filePath)}");

        if (changeLines.Count > 0)
        {
            foreach (var line in changeLines)
            {
                builder.AppendLine($"- {line}");
            }
        }

        builder.AppendLine();
        return builder.ToString();
    }

    [RelayCommand]
    private async Task GenerateIoProgramsAsync()
    {
        try
        {
            if (CanSaveIoTable)
            {
                await SaveIoTableToSourceAsync();
            }

            var result = await _ioProgramGenerationService.GenerateAsync(
                IoTableRows,
                new IoGenerationSettings
                {
                    PlcType = SelectedIoPlcTemplate,
                    OperationNumber = IoOperationNumber
                },
                GetApplicationRoot());

            GeneratedIoPrograms.Clear();
            foreach (var artifact in result.Artifacts) GeneratedIoPrograms.Add(artifact);
            SelectedGeneratedIoProgram = GeneratedIoPrograms.FirstOrDefault();
            GeneratedIoOutputDirectory = result.OutputDirectory;
            RebuildManualCylinderBlocksFromIo();
            RefreshIoGenerationSummary(result);
            SystemMessage = $"IO 程序已生成：{result.OutputDirectory}";
            AddLog("IO 生成", $"{SystemMessage}（输入 {result.InputCount} / 输出 {result.OutputCount}）", "Info");
        }
        catch (Exception ex)
        {
            SystemMessage = $"手动程序生成失败：{ex.Message}";
            AddLog("IO 生成", SystemMessage, "Error");
            ShowPopup("生成失败", ex.Message, "Error");
        }
    }

    [RelayCommand]
    private void ResetAutoProgramFlow()
    {
        SeedAutoProgramFlow();
        GeneratedAutoPrograms.Clear();
        SelectedGeneratedAutoProgram = null;
        GeneratedAutoOutputDirectory = string.Empty;
        SystemMessage = "自动流程已重置为标准骨架";
    }

    [RelayCommand]
    private void AddAutoProgramStep()
    {
        var nextStepNo = AutoProgramFlowNodes.Count == 0 ? 10 : AutoProgramFlowNodes.Max(x => x.StepNo) + 10;
        AutoProgramFlowNodes.Add(new AutoProgramFlowNode
        {
            StepNo = nextStepNo,
            Title = $"新步骤 {nextStepNo:000}",
            Action = "补充动作说明",
            NextStep = "END",
            Left = 70,
            Top = 80 + AutoProgramFlowNodes.Count * 130,
            Fill = "#E0F2FE"
        });
        RebuildAutoFlowLayout();
        SystemMessage = $"已新增自动流程节点 STEP {nextStepNo:000}";
    }

    [RelayCommand]
    private async Task GenerateAutoProgramsAsync()
    {
        try
        {
            var projectRoot = GetApplicationRoot();
            var outputDirectory = Path.Combine(projectRoot, "Generated", "AutoProgram");
            Directory.CreateDirectory(outputDirectory);

            var orderedNodes = AutoProgramFlowNodes.OrderBy(x => x.StepNo).ToList();
            var stationNo = ResolveStationNo(AutoProgramStation);
            var controlDb = $"DB{ResolveOperationBaseNumber(IoOperationNumber)}_Control";
            var autoTemplate = ReadGenerationTemplate("Auto.txt");
            var initTemplate = ReadGenerationTemplate("Init.txt");
            var autoProgram = BuildAutoTemplateProgram(autoTemplate, controlDb, stationNo, orderedNodes);
            var initProgram = BuildInitTemplateProgram(initTemplate, controlDb, stationNo, orderedNodes);
            var chartBuilder = new StringBuilder();
            chartBuilder.AppendLine($"流程名称：{AutoProgramName}");
            chartBuilder.AppendLine($"工位：{AutoProgramStation}");
            chartBuilder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            chartBuilder.AppendLine();
            foreach (var node in orderedNodes)
            {
                chartBuilder.AppendLine($"{node.StepCode}  {node.Title}");
                chartBuilder.AppendLine($"动作：{node.Action}");
                chartBuilder.AppendLine($"流向：{node.NextStep}");
                chartBuilder.AppendLine();
            }

            var artifacts = new[]
            {
                CreateGeneratedArtifact(outputDirectory, $"{IoOperationNumber}_AutoRun_{AutoProgramStation}.txt", autoProgram),
                CreateGeneratedArtifact(outputDirectory, $"{IoOperationNumber}_Init_{AutoProgramStation}.txt", initProgram),
                CreateGeneratedArtifact(outputDirectory, $"{AutoProgramStation}_FlowChart.txt", chartBuilder.ToString())
            };

            GeneratedAutoPrograms.Clear();
            foreach (var artifact in artifacts)
            {
                GeneratedAutoPrograms.Add(artifact);
            }

            SelectedGeneratedAutoProgram = GeneratedAutoPrograms.FirstOrDefault();
            GeneratedAutoOutputDirectory = outputDirectory;
            RefreshAutoProgramSummary();
            SystemMessage = $"自动程序已生成：{outputDirectory}";
            AddLog("自动程序", $"{AutoProgramName} 已生成 {artifacts.Length} 个文件", "Info");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SystemMessage = $"自动程序生成失败：{ex.Message}";
            AddLog("自动程序", SystemMessage, "Error");
            ShowPopup("生成失败", ex.Message, "Error");
        }
    }

    [RelayCommand]
    private void OpenGeneratedIoFolder()
    {
        try
        {
            _ioProgramGenerationService.OpenOutputDirectory(GeneratedIoOutputDirectory);
        }
        catch (Exception ex)
        {
            SystemMessage = $"打开生成目录失败：{ex.Message}";
            AddLog("IO 生成", SystemMessage, "Error");
            ShowPopup("打开失败", ex.Message, "Error");
        }
    }

    [RelayCommand]
    private void OpenGeneratedAutoFolder()
    {
        try
        {
            _ioProgramGenerationService.OpenOutputDirectory(GeneratedAutoOutputDirectory);
        }
        catch (Exception ex)
        {
            SystemMessage = $"打开自动程序目录失败：{ex.Message}";
            AddLog("自动程序", SystemMessage, "Error");
            ShowPopup("打开失败", ex.Message, "Error");
        }
    }

    [RelayCommand]
    private void OpenGeneratedIoFile(GeneratedProgramArtifact? artifact)
    {
        if (artifact is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = artifact.OutputPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SystemMessage = $"打开程序文件失败：{ex.Message}";
            AddLog("IO 生成", SystemMessage, "Error");
        }
    }

    [RelayCommand]
    private void Navigate(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        var target = section.Trim();

        switch (target)
        {
            case "监控":
            case "监视画面":
            case "输入输出监控":
            case "程序监控":
            case "通讯状态监控":
            case "详细生产数据":
                CurrentMonitorSubSection = target switch
                {
                    "监控" => "输入输出监控",
                    "监视画面" => "输入输出监控",
                    _ => target
                };
                CurrentSection = CurrentMonitorSubSection;
                break;
            case "手动操作":
            case "手动画面":
            case "气缸":
            case "轴":
            case "机械手":
            case "电机":
            case "挡停":
                CurrentManualSubSection = target switch
                {
                    "手动操作" => "气缸",
                    "手动画面" => "气缸",
                    _ => target
                };
                CurrentSection = CurrentManualSubSection;
                break;
            case "参数设定":
            case "系统参数设定":
            case "轴参数设定":
            case "气缸参数设定":
            case "真空参数设定":
            case "传感器参数设定":
                CurrentParameterSubSection = target == "参数设定" ? "系统参数设定" : target;
                CurrentSection = CurrentParameterSubSection;
                break;
            case "报警画面":
            case "当前报警":
            case "历史报警":
            case "日志":
            case "报警统计":
                CurrentAlarmSubSection = target == "报警画面" ? "当前报警" : target;
                CurrentSection = CurrentAlarmSubSection;
                break;
            case "设计器":
            case "画布设计":
            case "手动程序生成":
            case "自动程序生成":
                CurrentDesignerSubSection = target == "设计器" ? "画布设计" : target;
                CurrentSection = CurrentDesignerSubSection;
                break;
            default:
                CurrentSection = target;
                break;
        }

        SystemMessage = $"已切换到：{CurrentSection}";
    }

    [RelayCommand]
    private void ApplyRuntimeTemplate(string? templateName)
    {
        var template = string.IsNullOrWhiteSpace(templateName) ? SelectedRuntimeTemplate : templateName;
        if (SelectedDesignerPage is null) return;
        DesignerElements.Clear();
        switch (template)
        {
            case "主界面":
                DesignerElements.Add(CreateDesignerElement("PageButton", 40, 20, "去报警页", navigationTarget: "报警画面"));
                DesignerElements.Add(CreateDesignerElement("Motor", 40, 100, "电机1", "Y_RunLamp"));
                DesignerElements.Add(CreateDesignerElement("Cylinder", 280, 100, "气缸1", "Cylinder_Extend"));
                DesignerElements.Add(CreateDesignerElement("Robot", 520, 100, "机械手1", "Robot_Run"));
                DesignerElements.Add(CreateDesignerElement("Stopper", 760, 100, "挡停1", "Stopper_Up"));
                DesignerElements.Add(CreateDesignerElement("Axis", 1000, 100, "轴模块1", "Axis1_Pos"));
                break;
            case "监控画面":
                DesignerElements.Add(CreateDesignerElement("Indicator", 40, 80, "运行灯", "Y_RunLamp"));
                DesignerElements.Add(CreateDesignerElement("ValueDisplay", 240, 80, "轴位置", "Axis1_Pos"));
                DesignerElements.Add(CreateDesignerElement("AlarmBanner", 40, 180, "当前报警", "Alarm_EStop"));
                break;
            case "手动画面":
                DesignerElements.Add(CreateDesignerElement("Button", 40, 80, "启停电机", "Y_RunLamp"));
                DesignerElements.Add(CreateDesignerElement("Cylinder", 240, 80, "气缸动作", "Cylinder_Extend"));
                DesignerElements.Add(CreateDesignerElement("Stopper", 480, 80, "挡停动作", "Stopper_Up"));
                DesignerElements.Add(CreateDesignerElement("Robot", 720, 80, "机械手动作", "Robot_Run"));
                break;
            case "参数设定":
                DesignerElements.Add(CreateDesignerElement("Label", 40, 60, "系统参数"));
                DesignerElements.Add(CreateDesignerElement("ValueDisplay", 40, 120, "轴位置", "Axis1_Pos"));
                DesignerElements.Add(CreateDesignerElement("Label", 320, 60, "工艺参数"));
                DesignerElements.Add(CreateDesignerElement("ValueDisplay", 320, 120, "运行灯状态", "Y_RunLamp"));
                break;
            case "报警画面":
                DesignerElements.Add(CreateDesignerElement("AlarmBanner", 40, 80, "当前报警", "Alarm_EStop"));
                DesignerElements.Add(CreateDesignerElement("PageButton", 40, 20, "返回主界面", navigationTarget: "主界面"));
                break;
        }
        SyncCanvasToPage();
        SelectedDesignerElement = DesignerElements.FirstOrDefault();
        SystemMessage = $"已应用运行页模板：{template}";
    }

    [RelayCommand]
    private void AddDesignerElement(string? elementType)
    {
        if (IsRuntimeMode) { SystemMessage = "运行态下禁止编辑设计器"; return; }
        var type = string.IsNullOrWhiteSpace(elementType) ? SelectedToolboxItem : elementType;
        var count = DesignerElements.Count + 1;
        var element = CreateDesignerElement(type, 40 + (count % 5) * 30, 40 + (count % 5) * 30);
        DesignerElements.Add(element);
        SelectedDesignerElement = element;
        SyncCanvasToPage();
        CurrentDesignerSubSection = "画布设计";
        CurrentSection = "画布设计";
        SystemMessage = $"已添加模块：{type}";
        AddLog("设计器", SystemMessage, "Info");
    }

    [RelayCommand]
    private void AddDesignerElementAtDrop(string? payload)
    {
        if (IsRuntimeMode) { SystemMessage = "运行态下禁止拖拽新增控件"; return; }
        if (string.IsNullOrWhiteSpace(payload)) return;
        var parts = payload.Split('|'); if (parts.Length < 3) return;
        var type = parts[0];
        if (!double.TryParse(parts[1], out var x)) x = 40;
        if (!double.TryParse(parts[2], out var y)) y = 40;
        x = Snap(x); y = Snap(y);
        var element = CreateDesignerElement(type, x, y);
        DesignerElements.Add(element);
        SelectedDesignerElement = element;
        SyncCanvasToPage();
        SystemMessage = $"已拖拽添加模块：{type}";
        AddLog("设计器", SystemMessage, "Info");
    }

    [RelayCommand] private void StartToolboxDrag(string? tool) { if (IsRuntimeMode) return; DragToolboxItem = tool ?? string.Empty; }
    [RelayCommand] private void RemoveSelectedDesignerElement() { if (IsRuntimeMode) { SystemMessage = "运行态下禁止删除控件"; return; } if (SelectedDesignerElement is null) { SystemMessage = "请先选择要删除的控件"; return; } var name = SelectedDesignerElement.Name; DesignerElements.Remove(SelectedDesignerElement); SelectedDesignerElement = null; SyncCanvasToPage(); SystemMessage = $"已删除控件：{name}"; AddLog("设计器", SystemMessage, "Warning"); }
    [RelayCommand] private void CopySelectedDesignerElement() { if (SelectedDesignerElement is null) return; _clipboardElement = CloneElement(SelectedDesignerElement); OnPropertyChanged(nameof(HasClipboard)); SystemMessage = $"已复制控件：{SelectedDesignerElement.Name}"; }
    [RelayCommand] private void PasteDesignerElement() { if (IsRuntimeMode) { SystemMessage = "运行态下禁止粘贴控件"; return; } if (_clipboardElement is null) return; var clone = CloneElement(_clipboardElement); clone.Id = Guid.NewGuid().ToString("N"); clone.Name = _clipboardElement.Name + "_Paste"; clone.Left = Snap(clone.Left + 20); clone.Top = Snap(clone.Top + 20); DesignerElements.Add(clone); SelectedDesignerElement = clone; SyncCanvasToPage(); SystemMessage = $"已粘贴控件：{clone.Name}"; }

    [RelayCommand]
    private void SelectDesignerElement(DesignerElement? element)
    {
        if (element is null) return;
        SelectedDesignerElement = element;
        SystemMessage = $"已选中控件：{element.Name}";
        if (IsRuntimeMode) _ = ExecuteRuntimeElementActionAsync(element);
    }

    [RelayCommand]
    private async Task SaveDesignerLayoutAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "designer-layout.json");
        var page = new DesignerPage { Name = DesignerPageName, CanvasWidth = DesignerCanvasWidth, CanvasHeight = DesignerCanvasHeight, Elements = DesignerElements.ToList() };
        await _designerLayoutService.SavePageAsync(path, page);
        SystemMessage = $"设计器布局已保存：{path}";
        AddLog("设计器", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task LoadDesignerLayoutAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "designer-layout.json");
        var page = await _designerLayoutService.LoadPageAsync(path);
        if (page is null) { SystemMessage = "未找到设计器布局文件"; return; }
        DesignerPageName = page.Name;
        DesignerCanvasWidth = page.CanvasWidth;
        DesignerCanvasHeight = page.CanvasHeight;
        DesignerElements.Clear();
        foreach (var element in page.Elements) DesignerElements.Add(element);
        SelectedDesignerElement = DesignerElements.FirstOrDefault();
        SystemMessage = "设计器布局加载完成";
        AddLog("设计器", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task SaveDesignerProjectAsync()
    {
        SyncCanvasToPage();
        var path = Path.Combine(GetProjectRoot(), "config", "designer-project.json");
        var project = new DesignerProject
        {
            ProjectName = DesignerProjectName,
            Pages = DesignerPages.Select(p => new DesignerPage
            {
                Name = p.Name,
                CanvasWidth = p.CanvasWidth,
                CanvasHeight = p.CanvasHeight,
                Elements = p.Elements.Select(CloneElement).ToList()
            }).ToList()
        };
        await _designerProjectService.SaveProjectAsync(path, project);
        SystemMessage = $"设计器工程已保存：{path}";
        AddLog("设计器", SystemMessage, "Info");
    }

    [RelayCommand]
    private async Task LoadDesignerProjectAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "designer-project.json");
        var project = await _designerProjectService.LoadProjectAsync(path);
        if (project is null) { SystemMessage = "未找到设计器工程文件"; return; }
        DesignerProjectName = project.ProjectName;
        DesignerPages.Clear();
        foreach (var page in project.Pages)
        {
            DesignerPages.Add(new DesignerPage
            {
                Name = page.Name,
                CanvasWidth = page.CanvasWidth,
                CanvasHeight = page.CanvasHeight,
                Elements = page.Elements.Select(CloneElement).ToList()
            });
        }
        SelectedDesignerPage = DesignerPages.FirstOrDefault();
        SystemMessage = "设计器工程加载完成";
        AddLog("设计器", SystemMessage, "Info");
    }

    [RelayCommand] private void AddNewPage() { if (IsRuntimeMode) { SystemMessage = "运行态下禁止新建页面"; return; } SyncCanvasToPage(); var page = new DesignerPage { Name = $"页面{DesignerPages.Count + 1}", CanvasWidth = 1280, CanvasHeight = 720, Elements = new() }; DesignerPages.Add(page); SelectedDesignerPage = page; SystemMessage = $"已新建页面：{page.Name}"; AddLog("设计器", SystemMessage, "Info"); }
    [RelayCommand] private void RemoveCurrentPage() { if (IsRuntimeMode) { SystemMessage = "运行态下禁止删除页面"; return; } if (SelectedDesignerPage is null) return; var page = SelectedDesignerPage; DesignerPages.Remove(page); SelectedDesignerPage = DesignerPages.FirstOrDefault(); if (SelectedDesignerPage is null) { DesignerElements.Clear(); DesignerPageName = "主界面"; DesignerCanvasWidth = 1280; DesignerCanvasHeight = 720; } SystemMessage = $"已删除页面：{page.Name}"; AddLog("设计器", SystemMessage, "Warning"); }
    [RelayCommand] private void MoveSelectedElement(string? direction) { if (SelectedDesignerElement is null || IsRuntimeMode) return; const double step = 5; switch (direction) { case "Left": SelectedDesignerElement.Left = Snap(Math.Max(0, SelectedDesignerElement.Left - step)); break; case "Right": SelectedDesignerElement.Left = Snap(SelectedDesignerElement.Left + step); break; case "Up": SelectedDesignerElement.Top = Snap(Math.Max(0, SelectedDesignerElement.Top - step)); break; case "Down": SelectedDesignerElement.Top = Snap(SelectedDesignerElement.Top + step); break; } SyncCanvasToPage(); }
    [RelayCommand] private void AlignSelectedToGrid() { if (SelectedDesignerElement is null || IsRuntimeMode) return; SelectedDesignerElement.Left = Snap(SelectedDesignerElement.Left); SelectedDesignerElement.Top = Snap(SelectedDesignerElement.Top); SelectedDesignerElement.Width = Snap(SelectedDesignerElement.Width); SelectedDesignerElement.Height = Snap(SelectedDesignerElement.Height); SyncCanvasToPage(); }

    [RelayCommand]
    private async Task ToggleRuntimeModeAsync()
    {
        IsRuntimeMode = !IsRuntimeMode;
        if (IsRuntimeMode) await RefreshTagsAsync();
    }

    [RelayCommand] private void NavigateToPage(string? pageName) { if (string.IsNullOrWhiteSpace(pageName)) return; var page = DesignerPages.FirstOrDefault(p => p.Name.Equals(pageName, StringComparison.OrdinalIgnoreCase)); if (page is null) { SystemMessage = $"未找到页面：{pageName}"; return; } SelectedDesignerPage = page; CurrentSection = $"页面切换 -> {page.Name}"; }

    private async Task ExecuteRuntimeElementActionAsync(DesignerElement element)
    {
        var action = string.IsNullOrWhiteSpace(element.CommandBinding)
            ? GetDefaultDesignerAction(element.ElementType)
            : element.CommandBinding;

        switch (action)
        {
            case "页面跳转":
                if (!string.IsNullOrWhiteSpace(element.NavigationTarget))
                {
                    NavigateToPage(element.NavigationTarget);
                }
                break;
            case "变量翻转":
                await ToggleBoundBooleanTagAsync(element.TagBinding, element.Text);
                break;
            case "置位":
                await PulseOrWriteBoundTagAsync(element.TagBinding, true, false, element.Text);
                break;
            case "复位":
                await PulseOrWriteBoundTagAsync(element.TagBinding, false, false, element.Text);
                break;
            case "脉冲":
                await PulseOrWriteBoundTagAsync(element.TagBinding, true, true, element.Text);
                break;
            case "气缸回原":
                await SetCylinderPositionAsync(null, false, element.Text);
                break;
            case "气缸动点":
                await SetCylinderPositionAsync(null, true, element.Text);
                break;
        }
    }

    private static string GetDefaultDesignerAction(string elementType) => elementType switch
    {
        "PageButton" => "页面跳转",
        "Button" or "Motor" or "Cylinder" or "Stopper" or "Robot" => "变量翻转",
        _ => string.Empty
    };

    private async Task ToggleBoundBooleanTagAsync(string tagName, string sourceText)
    {
        if (!CanOperateDevices) { ShowPopup("权限不足", "当前权限不足，无法操作设备。", "Error"); return; }
        if (tagName.Equals("Cylinder_Extend", StringComparison.OrdinalIgnoreCase) && GetBoolTag("Alarm_EStop"))
        {
            ShowPopup("联锁禁止", "急停报警未恢复，不能操作气缸。", "Interlock");
            return;
        }
        if (tagName.Equals("Cylinder_Extend", StringComparison.OrdinalIgnoreCase) && GetBoolTag("Y_RunLamp") && !AllowManualCylinderWhenAuto)
        {
            ShowPopup("联锁禁止", "设备自动运行中，不能手动切换气缸。", "Interlock");
            return;
        }
        if (tagName.Equals("Cylinder_Extend", StringComparison.OrdinalIgnoreCase) && !GetBoolTag("Cylinder_BwdLS") && !GetBoolTag("Cylinder_FwdLS"))
        {
            ShowPopup("联锁禁止", "气缸当前处于中间状态，禁止再次切换。", "Interlock");
            return;
        }
        if (tagName.Equals("Stopper_Up", StringComparison.OrdinalIgnoreCase) && GetBoolTag("Y_RunLamp") && !AllowManualStopperWhenAuto)
        {
            ShowPopup("联锁禁止", "设备自动运行中，不能手动切换挡停。", "Interlock");
            return;
        }
        if (tagName.Equals("Robot_Run", StringComparison.OrdinalIgnoreCase) && GetBoolTag("Axis1_Alarm"))
        {
            ShowPopup("联锁禁止", "轴存在报警，不能启动机械手。", "Interlock");
            return;
        }
        if (tagName.Equals("Robot_Run", StringComparison.OrdinalIgnoreCase) && !GetBoolTag("Axis1_Enable"))
        {
            ShowPopup("联锁禁止", "轴未使能，不能启动机械手。", "Interlock");
            return;
        }
        var tag = Tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null) { ShowPopup("操作失败", $"未找到绑定变量：{tagName}", "Error"); return; }
        if (!tag.IsWritable) { ShowPopup("操作失败", $"变量不可写：{tag.Name}", "Error"); return; }
        try
        {
            var current = string.Equals(tag.CurrentValue, "True", StringComparison.OrdinalIgnoreCase);
            var next = !current;
            await _opcUaService.WriteTagAsync(tag, next);
            tag.CurrentValue = next.ToString();
            EvaluateTagState(tag);
            SystemMessage = $"运行态操作：{sourceText} -> {tag.Name} = {next}";
            AddLog("运行态", SystemMessage, "Info");
            AddAudit("设备操作", tag.Name, "成功", SystemMessage);
            UpdateRuntimeVisuals();
        }
        catch (Exception ex)
        {
            SystemMessage = $"运行态操作失败：{ex.Message}";
            AddLog("运行态", SystemMessage, "Error");
        }
    }

    private async Task PulseBooleanTagAsync(string tagName, string sourceText)
    {
        if (!CanOperateDevices) { SystemMessage = "当前权限不足，无法操作设备"; return; }
        var tag = Tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null) { SystemMessage = $"未找到绑定变量：{tagName}"; return; }
        if (!tag.IsWritable) { SystemMessage = $"变量不可写：{tag.Name}"; return; }
        try
        {
            await _opcUaService.WriteTagAsync(tag, true);
            tag.CurrentValue = "True";
            AddLog("运行态", $"{sourceText} -> {tag.Name} = True", "Info");
            AddAudit("脉冲操作", tag.Name, "成功", sourceText);
            await _opcUaService.WriteTagAsync(tag, false);
            tag.CurrentValue = "False";
            SystemMessage = $"已执行：{sourceText}";
            UpdateRuntimeVisuals();
        }
        catch (Exception ex)
        {
            SystemMessage = $"脉冲输出失败：{ex.Message}";
            AddLog("运行态", SystemMessage, "Error");
        }
    }

    private async Task PulseOrWriteBoundTagAsync(string tagName, bool value, bool pulse, string sourceText)
    {
        if (!CanOperateDevices)
        {
            ShowPopup("权限不足", "当前权限不足，无法操作设备。", "Error");
            return;
        }

        if (string.IsNullOrWhiteSpace(tagName))
        {
            ShowPopup("操作失败", "当前控件未绑定变量。", "Warning");
            return;
        }

        var tag = Tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            ShowPopup("操作失败", $"未找到绑定变量：{tagName}", "Error");
            return;
        }

        if (!tag.IsWritable)
        {
            ShowPopup("操作失败", $"变量不可写：{tag.Name}", "Warning");
            return;
        }

        try
        {
            await _opcUaService.WriteTagAsync(tag, value);
            tag.CurrentValue = value.ToString();
            AddLog("运行态", $"{sourceText} -> {tag.Name} = {value}", "Info");
            AddAudit("设备操作", tag.Name, "成功", sourceText);

            if (pulse)
            {
                await Task.Delay(120);
                await _opcUaService.WriteTagAsync(tag, false);
                tag.CurrentValue = "False";
            }

            UpdateRuntimeVisuals();
        }
        catch (Exception ex)
        {
            SystemMessage = $"运行态操作失败：{ex.Message}";
            AddLog("运行态", SystemMessage, "Error");
            AddAudit("设备操作", tagName, "失败", SystemMessage);
        }
    }

    private async Task SetCylinderPositionAsync(ManualCylinderBlockItem? block, bool extend, string sourceText)
    {
        if (!CanOperateDevices)
        {
            SystemMessage = "当前权限不足，无法操作气缸";
            return;
        }

        {
            var configuredCommandTag = block is not null
                ? FindTagByNameOrNodeId(extend ? block.WorkCommandTagName : block.HomeCommandTagName)
                : !string.IsNullOrWhiteSpace(extend ? CylinderWorkCommandTagName : CylinderHomeCommandTagName)
                    ? FindTagByNameOrNodeId(extend ? CylinderWorkCommandTagName : CylinderHomeCommandTagName)
                    : null;
            var importedCommandTag = configuredCommandTag ?? FindImportedCylinderTag(extend ? ".Cmd.ManuToWork" : ".Cmd.ManuToHome");
            var tag = importedCommandTag ?? Tags.FirstOrDefault(t => t.Name.Equals("Cylinder_Extend", StringComparison.OrdinalIgnoreCase));
            if (tag is null)
            {
                ShowPopup("操作失败", "未找到气缸输出变量：Cylinder_Extend / Cmd.ManuToHome / Cmd.ManuToWork", "Error");
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                if (importedCommandTag is not null)
                {
                    await _opcUaService.WriteTagAsync(tag, true);
                    tag.CurrentValue = "True";
                    await Task.Delay(120);
                    await _opcUaService.WriteTagAsync(tag, false);
                    tag.CurrentValue = "False";
                    await RefreshTagsAsync();
                }
                else
                {
                    await _opcUaService.WriteTagAsync(tag, extend);
                    tag.CurrentValue = extend.ToString();
                    SetTagValue("Cylinder_FwdLS", extend ? "True" : "False");
                    SetTagValue("Cylinder_BwdLS", extend ? "False" : "True");
                }
                stopwatch.Stop();
                var configuredDelay = extend ? CylinderWorkDelaySetting : CylinderHomeDelaySetting;
                var durationText = double.TryParse(configuredDelay, NumberStyles.Float, CultureInfo.InvariantCulture, out var configuredSeconds)
                    ? $"{Math.Max(configuredSeconds, stopwatch.Elapsed.TotalSeconds):0.000} s"
                    : $"{stopwatch.Elapsed.TotalSeconds:0.000} s";
                CylinderCurrentActionTimeDisplay = durationText;
                CylinderLastActionTimeDisplay = durationText;
                CylinderActionCount += 1;
                EvaluateTagState(tag);
                SystemMessage = $"{sourceText}完成";
                AddLog("气缸控制", $"{sourceText} -> {tag.Name} = {(importedCommandTag is not null ? "Pulse" : extend.ToString())}", "Info");
                AddAudit("气缸控制", tag.Name, "成功", sourceText);
                UpdateRuntimeVisuals();
            }
            catch (Exception ex)
            {
                SystemMessage = "气缸操作失败：" + ex.Message;
                AddLog("气缸控制", SystemMessage, "Error");
            }

            return;
        }
    }

    private async Task SetExclusiveModeAsync(string targetTagName, string modeName)
    {
        if (!CanOperateDevices)
        {
            ShowPopup("权限不足", "当前权限不足，无法切换运行模式。", "Error");
            return;
        }

        var modeTags = new[] { "Mode_Debug", "Mode_DryRun", "Mode_BypassStation", "Mode_Manual", "Mode_Auto" };
        foreach (var modeTagName in modeTags)
        {
            var modeTag = Tags.FirstOrDefault(t => t.Name == modeTagName);
            if (modeTag is null || !modeTag.IsWritable) continue;
            var value = modeTagName == targetTagName;
            try
            {
                await _opcUaService.WriteTagAsync(modeTag, value);
                modeTag.CurrentValue = value.ToString();
            }
            catch
            {
                modeTag.CurrentValue = value.ToString();
            }
        }

        AddLog("模式切换", $"已切换到 {modeName}", "Info");
        AddAudit("模式切换", targetTagName, "成功", modeName);
        UpdateRuntimeVisuals();
    }

    private async Task WriteAxisPositionAsync(double position, string successMessage)
    {
        var tag = Tags.FirstOrDefault(t => t.Name == "Axis1_Pos");
        if (tag is null || !tag.IsWritable) { SystemMessage = "轴位置变量不可写"; return; }
        if (!CanOperateDevices) { SystemMessage = "当前权限不足，无法操作轴"; return; }
        try
        {
            await _opcUaService.WriteTagAsync(tag, position);
            tag.CurrentValue = position.ToString("0.###");
            EvaluateTagState(tag);
            SystemMessage = successMessage;
            AddLog("轴控制", successMessage, "Info");
            AddAudit("轴控制", "Axis1_Pos", "成功", successMessage);
            UpdateRuntimeVisuals();
        }
        catch (Exception ex)
        {
            SystemMessage = $"轴操作失败：{ex.Message}";
            AddLog("轴控制", SystemMessage, "Error");
        }
    }

    private double GetAxisCurrentPosition()
    {
        var tag = Tags.FirstOrDefault(t => t.Name == "Axis1_Pos");
        if (tag is null) return 0;
        return double.TryParse(tag.CurrentValue, out var value) ? value : 0;
    }

    private async Task AutoRefreshTickAsync()
    {
        if (!AutoRefreshEnabled || !_opcUaService.IsConnected || !IsRuntimeMode || UseOpcSubscription) return;
        await RefreshTagsAsync();
    }

    private async void UpdateAutoRefreshState()
    {
        _subscriptionTimer.Stop();
        await _opcUaService.UnsubscribeAllAsync();
        if (!_opcUaService.IsConnected || !IsRuntimeMode || !AutoRefreshEnabled) return;
        if (UseOpcSubscription) await _opcUaService.SubscribeTagsAsync(Tags, RefreshIntervalMs);
        else { _subscriptionTimer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs); _subscriptionTimer.Start(); }
    }

    private void OpcUaService_TagValueChanged(string tagName, string value)
    {
        var tag = Tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null) return;
        tag.CurrentValue = value;
        EvaluateTagState(tag);
        EvaluateEvents(tag);
        UpdateRuntimeVisuals();
        RefreshMonitorView();
        RefreshAlarmStatistics();
    }

    private bool FilterParameterItem(object item)
    {
        if (item is not ParameterItem parameter)
        {
            return false;
        }

        return CurrentParameterSubSection switch
        {
            "系统参数设定" => parameter.Category is "系统参数" or "联锁规则",
            "轴参数设定" => parameter.Category == "轴参数",
            "气缸参数设定" => parameter.Category == "气缸参数",
            "真空参数设定" => parameter.Category == "真空参数",
            "传感器参数设定" => parameter.Category == "传感器参数",
            _ => true
        };
    }

    private void SeedParameters()
    {
        Parameters.Add(new ParameterItem { Category = "系统参数", Name = "设备节拍", Value = "3.5", Unit = "s", Description = "设备标准节拍", MinRole = UserRole.Engineer });
        Parameters.Add(new ParameterItem { Category = "轴参数", Name = "轴速度", Value = "250", Unit = "mm/s", Description = "轴运行速度", MinRole = UserRole.Engineer });
        Parameters.Add(new ParameterItem { Category = "气缸参数", Name = "气缸延时", Value = "0.2", Unit = "s", Description = "气缸动作延时", MinRole = UserRole.Engineer });
        Parameters.Add(new ParameterItem { Category = "真空参数", Name = "真空检测超时", Value = "1.0", Unit = "s", Description = "真空建立超时时间", MinRole = UserRole.Administrator });
        Parameters.Add(new ParameterItem { Category = "传感器参数", Name = "滤波时间", Value = "50", Unit = "ms", Description = "传感器滤波时间", MinRole = UserRole.Engineer });
        Parameters.Add(new ParameterItem { Category = "联锁规则", Name = "自动运行允许手动气缸", Value = "false", Unit = "bool", Description = "决定自动运行时是否允许手动切换气缸", MinRole = UserRole.Administrator });
        Parameters.Add(new ParameterItem { Category = "联锁规则", Name = "自动运行允许手动挡停", Value = "false", Unit = "bool", Description = "决定自动运行时是否允许手动切换挡停", MinRole = UserRole.Administrator });
        Parameters.Add(new ParameterItem { Category = "联锁规则", Name = "机械手运行时允许复位", Value = "false", Unit = "bool", Description = "决定机械手运行中是否允许执行复位", MinRole = UserRole.Administrator });
        Parameters.Add(new ParameterItem { Category = "联锁规则", Name = "轴报警时允许运动", Value = "false", Unit = "bool", Description = "决定轴报警状态下是否允许 Jog/定位/回零", MinRole = UserRole.Administrator });
    }

    private void BuildNavigation()
    {
        NavigationItems.Add(new NavigationItemViewModel("主界面"));
        NavigationItems.Add(new NavigationItemViewModel("监控", "输入输出监控", "程序监控", "通讯状态监控", "详细生产数据"));
        NavigationItems.Add(new NavigationItemViewModel("手动操作", "气缸", "轴", "机械手", "电机", "挡停"));
        NavigationItems.Add(new NavigationItemViewModel("参数设定", "系统参数设定", "轴参数设定", "气缸参数设定", "真空参数设定", "传感器参数设定"));
        NavigationItems.Add(new NavigationItemViewModel("报警画面", "当前报警", "历史报警", "日志", "报警统计"));
        NavigationItems.Add(new NavigationItemViewModel("登录"));
        NavigationItems.Add(new NavigationItemViewModel("设计器", "画布设计", "手动程序生成", "自动程序生成"));
    }

    private static int ResolveTabIndex(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return 0;
        }

        return section.Trim() switch
        {
            "主界面" or "运行总览" => 0,
            "监控" or "监视画面" or "输入输出监控" or "程序监控" or "通讯状态监控" or "详细生产数据" => 1,
            "配方管理" => 2,
            "手动操作" or "手动画面" or "气缸" or "轴" or "机械手" or "电机" or "挡停" => 3,
            "参数设定" or "系统参数设定" or "轴参数设定" or "气缸参数设定" or "真空参数设定" or "传感器参数设定" => 4,
            "报警画面" or "当前报警" or "历史报警" or "日志" or "报警统计" => 5,
            "登录" or "登录权限" => 6,
            "操作审计" => 7,
            "设计器" or "画布设计" or "手动程序生成" or "自动程序生成" => 8,
            _ => 0
        };
    }

    private static string GetSectionNameByTabIndex(int tabIndex) => tabIndex switch
    {
        0 => "运行总览",
        1 => "监视画面",
        2 => "配方管理",
        3 => "手动画面",
        4 => "参数设定",
        5 => "报警画面",
        6 => "登录权限",
        7 => "操作审计",
        8 => "设计器",
        _ => "运行总览"
    };

    private static string ResolveNavigationGroup(string? section)
    {
        return section switch
        {
            "主界面" or "运行总览" => "主界面",
            "监控" or "监视画面" or "输入输出监控" or "程序监控" or "通讯状态监控" or "详细生产数据" => "监控",
            "配方管理" => "配方管理",
            "手动操作" or "手动画面" or "气缸" or "轴" or "机械手" or "电机" or "挡停" => "手动操作",
            "参数设定" or "系统参数设定" or "轴参数设定" or "气缸参数设定" or "真空参数设定" or "传感器参数设定" => "参数设定",
            "报警画面" or "当前报警" or "历史报警" or "日志" or "报警统计" => "报警画面",
            "登录" or "登录权限" => "登录",
            "操作审计" => "操作审计",
            "设计器" or "画布设计" or "手动程序生成" or "自动程序生成" => "设计器",
            _ => string.Empty
        };
    }

    private void SeedDemoData()
    {
        AddTag(new TagItem { Name = "X_Start", NodeId = "ns=2;s=Channel1.Device1.X_Start", DataType = "Boolean", Category = "IO", Group = "Input", Direction = "Input", CurrentValue = "False", IsWritable = false });
        AddTag(new TagItem { Name = "Y_RunLamp", NodeId = "ns=2;s=Channel1.Device1.Y_RunLamp", DataType = "Boolean", Category = "IO", Group = "Output", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Axis1_Pos", NodeId = "ns=2;s=Channel1.Device1.Axis1_Pos", DataType = "Double", Category = "Axis", Group = "Motion", Direction = "Output", CurrentValue = "0.0", IsWritable = true });
        AddTag(new TagItem { Name = "Alarm_EStop", NodeId = "ns=2;s=Channel1.Device1.Alarm_EStop", DataType = "Boolean", Category = "Alarm", Group = "Alarm", Direction = "Input", CurrentValue = "False", IsAlarm = true, IsWritable = false });
        AddTag(new TagItem { Name = "Cylinder_Extend", NodeId = "ns=2;s=Channel1.Device1.Cylinder_Extend", DataType = "Boolean", Category = "Cylinder", Group = "Actuator", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Stopper_Up", NodeId = "ns=2;s=Channel1.Device1.Stopper_Up", DataType = "Boolean", Category = "Stopper", Group = "Actuator", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Robot_Run", NodeId = "ns=2;s=Channel1.Device1.Robot_Run", DataType = "Boolean", Category = "Robot", Group = "Actuator", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Motor1_Fault", NodeId = "ns=2;s=Channel1.Device1.Motor1_Fault", DataType = "Boolean", Category = "Alarm", Group = "Motor", Direction = "Input", CurrentValue = "False", IsAlarm = true, IsWritable = false });
        AddTag(new TagItem { Name = "Motor1_Reset", NodeId = "ns=2;s=Channel1.Device1.Motor1_Reset", DataType = "Boolean", Category = "Motor", Group = "Command", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Cylinder_FwdLS", NodeId = "ns=2;s=Channel1.Device1.Cylinder_FwdLS", DataType = "Boolean", Category = "Cylinder", Group = "Status", Direction = "Input", CurrentValue = "False", IsWritable = false });
        AddTag(new TagItem { Name = "Cylinder_BwdLS", NodeId = "ns=2;s=Channel1.Device1.Cylinder_BwdLS", DataType = "Boolean", Category = "Cylinder", Group = "Status", Direction = "Input", CurrentValue = "True", IsWritable = false });
        AddTag(new TagItem { Name = "Axis1_Enable", NodeId = "ns=2;s=Channel1.Device1.Axis1_Enable", DataType = "Boolean", Category = "Axis", Group = "Command", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Axis1_Alarm", NodeId = "ns=2;s=Channel1.Device1.Axis1_Alarm", DataType = "Boolean", Category = "Alarm", Group = "Axis", Direction = "Input", CurrentValue = "False", IsAlarm = true, IsWritable = false });
        AddTag(new TagItem { Name = "Axis1_AlarmReset", NodeId = "ns=2;s=Channel1.Device1.Axis1_AlarmReset", DataType = "Boolean", Category = "Axis", Group = "Command", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Robot_Pause", NodeId = "ns=2;s=Channel1.Device1.Robot_Pause", DataType = "Boolean", Category = "Robot", Group = "Command", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Robot_Reset", NodeId = "ns=2;s=Channel1.Device1.Robot_Reset", DataType = "Boolean", Category = "Robot", Group = "Command", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Mode_Debug", NodeId = "ns=2;s=Channel1.Device1.Mode_Debug", DataType = "Boolean", Category = "Mode", Group = "RunMode", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Mode_DryRun", NodeId = "ns=2;s=Channel1.Device1.Mode_DryRun", DataType = "Boolean", Category = "Mode", Group = "RunMode", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Mode_BypassStation", NodeId = "ns=2;s=Channel1.Device1.Mode_BypassStation", DataType = "Boolean", Category = "Mode", Group = "RunMode", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Mode_Manual", NodeId = "ns=2;s=Channel1.Device1.Mode_Manual", DataType = "Boolean", Category = "Mode", Group = "RunMode", Direction = "Output", CurrentValue = "True", IsWritable = true });
        AddTag(new TagItem { Name = "Mode_Auto", NodeId = "ns=2;s=Channel1.Device1.Mode_Auto", DataType = "Boolean", Category = "Mode", Group = "RunMode", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Device_Start", NodeId = "ns=2;s=Channel1.Device1.Device_Start", DataType = "Boolean", Category = "System", Group = "RunControl", Direction = "Output", CurrentValue = "False", IsWritable = true });
        AddTag(new TagItem { Name = "Production_Count", NodeId = "ns=2;s=Channel1.Device1.Production_Count", DataType = "Int32", Category = "Production", Group = "Summary", Direction = "Input", CurrentValue = "1280", IsWritable = false });
        AddTag(new TagItem { Name = "Production_GoodCount", NodeId = "ns=2;s=Channel1.Device1.Production_GoodCount", DataType = "Int32", Category = "Production", Group = "Summary", Direction = "Input", CurrentValue = "1246", IsWritable = false });
        AddTag(new TagItem { Name = "Production_NgCount", NodeId = "ns=2;s=Channel1.Device1.Production_NgCount", DataType = "Int32", Category = "Production", Group = "Summary", Direction = "Input", CurrentValue = "34", IsWritable = false });
        AddTag(new TagItem { Name = "Shift_ProductionCount", NodeId = "ns=2;s=Channel1.Device1.Shift_ProductionCount", DataType = "Int32", Category = "Production", Group = "Shift", Direction = "Input", CurrentValue = "460", IsWritable = false });
        AddTag(new TagItem { Name = "Shift_GoodCount", NodeId = "ns=2;s=Channel1.Device1.Shift_GoodCount", DataType = "Int32", Category = "Production", Group = "Shift", Direction = "Input", CurrentValue = "450", IsWritable = false });
        AddTag(new TagItem { Name = "Shift_NgCount", NodeId = "ns=2;s=Channel1.Device1.Shift_NgCount", DataType = "Int32", Category = "Production", Group = "Shift", Direction = "Input", CurrentValue = "10", IsWritable = false });
        AddTag(new TagItem { Name = "Daily_ProductionCount", NodeId = "ns=2;s=Channel1.Device1.Daily_ProductionCount", DataType = "Int32", Category = "Production", Group = "Daily", Direction = "Input", CurrentValue = "1280", IsWritable = false });
        AddTag(new TagItem { Name = "Daily_GoodCount", NodeId = "ns=2;s=Channel1.Device1.Daily_GoodCount", DataType = "Int32", Category = "Production", Group = "Daily", Direction = "Input", CurrentValue = "1246", IsWritable = false });
        AddTag(new TagItem { Name = "Daily_NgCount", NodeId = "ns=2;s=Channel1.Device1.Daily_NgCount", DataType = "Int32", Category = "Production", Group = "Daily", Direction = "Input", CurrentValue = "34", IsWritable = false });
        AddTag(new TagItem { Name = "Production_TargetCount", NodeId = "ns=2;s=Channel1.Device1.Production_TargetCount", DataType = "Int32", Category = "Production", Group = "Summary", Direction = "Input", CurrentValue = "1500", IsWritable = false });
        AddTag(new TagItem { Name = "Production_Availability", NodeId = "ns=2;s=Channel1.Device1.Production_Availability", DataType = "Double", Category = "Production", Group = "OEE", Direction = "Input", CurrentValue = "92.5", IsWritable = false });
        AddTag(new TagItem { Name = "Production_Performance", NodeId = "ns=2;s=Channel1.Device1.Production_Performance", DataType = "Double", Category = "Production", Group = "OEE", Direction = "Input", CurrentValue = "88.2", IsWritable = false });
        AddTag(new TagItem { Name = "Production_Quality", NodeId = "ns=2;s=Channel1.Device1.Production_Quality", DataType = "Double", Category = "Production", Group = "OEE", Direction = "Input", CurrentValue = "97.3", IsWritable = false });
        AddTag(new TagItem { Name = "Cycle_Time", NodeId = "ns=2;s=Channel1.Device1.Cycle_Time", DataType = "Double", Category = "Production", Group = "Detail", Direction = "Input", CurrentValue = "3.2", IsWritable = false });
        AddTag(new TagItem { Name = "Ideal_Cycle_Time", NodeId = "ns=2;s=Channel1.Device1.Ideal_Cycle_Time", DataType = "Double", Category = "Production", Group = "Detail", Direction = "Input", CurrentValue = "2.8", IsWritable = false });
        AddTag(new TagItem { Name = "Throughput_Hourly", NodeId = "ns=2;s=Channel1.Device1.Throughput_Hourly", DataType = "Int32", Category = "Production", Group = "Detail", Direction = "Input", CurrentValue = "380", IsWritable = false });
        AddTag(new TagItem { Name = "WorkOrder_No", NodeId = "ns=2;s=Channel1.Device1.WorkOrder_No", DataType = "String", Category = "Production", Group = "Order", Direction = "Input", CurrentValue = "WO-20260404-01", IsWritable = false });
        AddTag(new TagItem { Name = "Recipe_Name", NodeId = "ns=2;s=Channel1.Device1.Recipe_Name", DataType = "String", Category = "Production", Group = "Order", Direction = "Input", CurrentValue = "产品A", IsWritable = false });
        AddTag(new TagItem { Name = "Machine_RunTimeMin", NodeId = "ns=2;s=Channel1.Device1.Machine_RunTimeMin", DataType = "Int32", Category = "Production", Group = "Detail", Direction = "Input", CurrentValue = "420", IsWritable = false });
        AddTag(new TagItem { Name = "Machine_StopTimeMin", NodeId = "ns=2;s=Channel1.Device1.Machine_StopTimeMin", DataType = "Int32", Category = "Production", Group = "Detail", Direction = "Input", CurrentValue = "34", IsWritable = false });
        AddTag(new TagItem { Name = "Alarm_AirLow", NodeId = "ns=2;s=Channel1.Device1.Alarm_AirLow", DataType = "Boolean", Category = "Alarm", Group = "Utility", Direction = "Input", CurrentValue = "True", IsAlarm = true, IsWritable = false });
        AddTag(new TagItem { Name = "Alarm_ServoOverload", NodeId = "ns=2;s=Channel1.Device1.Alarm_ServoOverload", DataType = "Boolean", Category = "Alarm", Group = "Axis", Direction = "Input", CurrentValue = "False", IsAlarm = true, IsWritable = false });
        AddTag(new TagItem { Name = "Alarm_VacuumTimeout", NodeId = "ns=2;s=Channel1.Device1.Alarm_VacuumTimeout", DataType = "Boolean", Category = "Alarm", Group = "Process", Direction = "Input", CurrentValue = "False", IsAlarm = true, IsWritable = false });

        EventBindings.Add(new EventBinding { TagName = "Alarm_EStop", TriggerCondition = "True", EventName = "急停报警", ActionTarget = "当前报警", ActionParameter = "急停触发", Description = "E-Stop 触发时写入报警" });
        EventBindings.Add(new EventBinding { TagName = "Motor1_Fault", TriggerCondition = "True", EventName = "电机故障", ActionTarget = "当前报警", ActionParameter = "电机1故障", Description = "电机故障报警" });
        EventBindings.Add(new EventBinding { TagName = "Axis1_Alarm", TriggerCondition = "True", EventName = "轴报警", ActionTarget = "当前报警", ActionParameter = "轴报警", Description = "轴故障报警" });
        EventBindings.Add(new EventBinding { TagName = "Alarm_AirLow", TriggerCondition = "True", EventName = "气压报警", ActionTarget = "当前报警", ActionParameter = "气源不足", Description = "气压不足报警" });
        EventBindings.Add(new EventBinding { TagName = "Alarm_ServoOverload", TriggerCondition = "True", EventName = "伺服过载", ActionTarget = "当前报警", ActionParameter = "轴过载", Description = "伺服过载报警" });
        EventBindings.Add(new EventBinding { TagName = "Alarm_VacuumTimeout", TriggerCondition = "True", EventName = "真空超时", ActionTarget = "当前报警", ActionParameter = "真空建立失败", Description = "真空报警" });

        CurrentAlarms.Add(new AlarmRecord { Time = DateTime.Now, Level = "Warning", Source = "Alarm_AirLow", Message = "示例报警：气压低", Active = true, Acknowledged = false, State = "Active", Count = 4 });
        CurrentAlarms.Add(new AlarmRecord { Time = DateTime.Now.AddMinutes(-3), Level = "Alarm", Source = "Motor1_Fault", Message = "电机故障 - 电机1故障", Active = true, Acknowledged = false, State = "Active", Count = 2 });
        _activeAlarmMap["Alarm_AirLow|气源不足"] = CurrentAlarms[0];
        _activeAlarmMap["Motor1_Fault|电机1故障"] = CurrentAlarms[1];
        AlarmHistory.Add(new AlarmRecord { Time = DateTime.Now.AddMinutes(-60), Level = "Error", Source = "Alarm_EStop", Message = "急停触发", Active = false, Acknowledged = true, ClearTime = DateTime.Now.AddMinutes(-58), State = "Cleared", Count = 1 });
        AlarmHistory.Add(new AlarmRecord { Time = DateTime.Now.AddMinutes(-40), Level = "Warning", Source = "Alarm_AirLow", Message = "气压报警 - 气源不足", Active = false, Acknowledged = true, ClearTime = DateTime.Now.AddMinutes(-35), State = "Cleared", Count = 6 });
        AlarmHistory.Add(new AlarmRecord { Time = DateTime.Now.AddMinutes(-25), Level = "Alarm", Source = "Motor1_Fault", Message = "电机故障 - 电机1故障", Active = false, Acknowledged = true, ClearTime = DateTime.Now.AddMinutes(-20), State = "Cleared", Count = 3 });
        AlarmHistory.Add(new AlarmRecord { Time = DateTime.Now.AddMinutes(-15), Level = "Alarm", Source = "Axis1_Alarm", Message = "轴报警 - 轴报警", Active = false, Acknowledged = true, ClearTime = DateTime.Now.AddMinutes(-10), State = "Cleared", Count = 2 });
        Logs.Add(new AlarmRecord { Time = DateTime.Now, Level = "Info", Source = "系统", Message = "程序启动", Active = false, Acknowledged = true, State = "Logged", Count = 1 });
        Logs.Add(new AlarmRecord { Time = DateTime.Now.AddMinutes(-2), Level = "Info", Source = "生产", Message = "当前班次累计 460 件，目标 1500 件", Active = false, Acknowledged = true, State = "Logged", Count = 1 });
        OnPropertyChanged(nameof(TagCount));
        OnPropertyChanged(nameof(AlarmCount));
    }

    private void SeedDesignerData()
    {
        var mainPage = new DesignerPage { Name = "主界面", CanvasWidth = 1280, CanvasHeight = 720, Elements = new() { CreateDesignerElement("PageButton", 40, 20, "去报警页", navigationTarget: "报警画面"), CreateDesignerElement("Button", 40, 80, "启动按钮", "Y_RunLamp"), CreateDesignerElement("Indicator", 220, 80, "运行灯", "Y_RunLamp"), CreateDesignerElement("ValueDisplay", 400, 80, "轴位置", "Axis1_Pos"), CreateDesignerElement("Motor", 40, 180, "电机1", "Y_RunLamp"), CreateDesignerElement("Cylinder", 260, 180, "气缸1", "Cylinder_Extend"), CreateDesignerElement("Axis", 480, 180, "轴模块1", "Axis1_Pos"), CreateDesignerElement("Robot", 720, 180, "机械手1", "Robot_Run"), CreateDesignerElement("Stopper", 960, 180, "挡停1", "Stopper_Up") } };
        var alarmPage = new DesignerPage { Name = "报警画面", CanvasWidth = 1280, CanvasHeight = 720, Elements = new() { CreateDesignerElement("PageButton", 40, 20, "返回主界面", navigationTarget: "主界面"), CreateDesignerElement("AlarmBanner", 40, 80, "当前报警", "Alarm_EStop") } };
        DesignerPages.Add(mainPage); DesignerPages.Add(alarmPage); SelectedDesignerPage = mainPage;
    }

    private void LoadPageToCanvas(DesignerPage page)
    {
        DesignerPageName = page.Name;
        DesignerCanvasWidth = page.CanvasWidth;
        DesignerCanvasHeight = page.CanvasHeight;
        DesignerElements.Clear();
        foreach (var element in page.Elements.Select(CloneElement)) DesignerElements.Add(element);
        SelectedDesignerElement = DesignerElements.FirstOrDefault();
        if (CurrentDesignerSubSection == "画布设计")
        {
            CurrentSection = "画布设计";
        }
    }

    private void SyncCanvasToPage()
    {
        if (SelectedDesignerPage is null) return;
        SelectedDesignerPage.Name = DesignerPageName;
        SelectedDesignerPage.CanvasWidth = DesignerCanvasWidth;
        SelectedDesignerPage.CanvasHeight = DesignerCanvasHeight;
        SelectedDesignerPage.Elements = DesignerElements.Select(CloneElement).ToList();
    }

    private void UpdateRuntimeVisuals()
    {
        RefreshManualCylinderBlockStates();
        foreach (var element in DesignerElements)
        {
            var tag = Tags.FirstOrDefault(t => t.Name.Equals(element.TagBinding, StringComparison.OrdinalIgnoreCase));
            if (tag is null) continue;
            switch (element.ElementType)
            {
                case "Indicator": element.Background = tag.CurrentValue.Equals("True", StringComparison.OrdinalIgnoreCase) ? "#22C55E" : "#475569"; break;
                case "ValueDisplay": element.Text = $"{tag.Name}:{tag.CurrentValue}"; break;
                case "AlarmBanner": element.Background = ActiveAlarmCount > 0 ? "#DC2626" : "#0F766E"; element.Text = ActiveAlarmCount > 0 ? $"报警中 {ActiveAlarmCount} 条" : "系统无活动报警"; break;
                case "Motor":
                    var motorRunning = GetBoolTag("Y_RunLamp");
                    var motorFault = GetBoolTag("Motor1_Fault");
                    element.Background = motorFault ? "#B91C1C" : motorRunning ? "#2563EB" : "#475569";
                    element.Text = motorFault ? "电机故障" : motorRunning ? "电机运行" : "电机停止";
                    break;
                case "Cylinder":
                    var fwd = GetBoolTag("Cylinder_FwdLS");
                    var bwd = GetBoolTag("Cylinder_BwdLS");
                    element.Background = fwd ? "#059669" : bwd ? "#334155" : "#D97706";
                    element.Text = fwd ? "前到位" : bwd ? "后到位" : "切换中";
                    break;
                case "Axis":
                    var axisEnable = GetBoolTag("Axis1_Enable");
                    var axisAlarm = GetBoolTag("Axis1_Alarm");
                    element.Background = axisAlarm ? "#B91C1C" : axisEnable ? "#7C3AED" : "#475569";
                    element.Text = axisAlarm ? $"轴报警 Pos:{GetTagValue("Axis1_Pos")}" : axisEnable ? $"轴使能 Pos:{GetTagValue("Axis1_Pos")}" : $"轴未使能 Pos:{GetTagValue("Axis1_Pos")}";
                    break;
                case "Robot":
                    var robotRun = GetBoolTag("Robot_Run");
                    var robotPause = GetBoolTag("Robot_Pause");
                    element.Background = robotPause ? "#F59E0B" : robotRun ? "#DB2777" : "#475569";
                    element.Text = robotPause ? "机械手暂停" : robotRun ? "机械手运行" : "机械手待机";
                    break;
                case "Stopper":
                    element.Background = tag.CurrentValue.Equals("True", StringComparison.OrdinalIgnoreCase) ? "#D97706" : "#475569";
                    element.Text = tag.CurrentValue.Equals("True", StringComparison.OrdinalIgnoreCase) ? "挡停升起" : "挡停下降";
                    break;
            }
        }

        OnPropertyChanged(nameof(ActiveAlarmCount));
        OnPropertyChanged(nameof(UnacknowledgedAlarmCount));
        OnPropertyChanged(nameof(ProductionCount));
        OnPropertyChanged(nameof(GoodCount));
        OnPropertyChanged(nameof(NgCount));
        OnPropertyChanged(nameof(ShiftProductionCount));
        OnPropertyChanged(nameof(ShiftGoodCount));
        OnPropertyChanged(nameof(ShiftNgCount));
        OnPropertyChanged(nameof(DailyProductionCount));
        OnPropertyChanged(nameof(DailyGoodCount));
        OnPropertyChanged(nameof(DailyNgCount));
        OnPropertyChanged(nameof(TargetCount));
        OnPropertyChanged(nameof(AvailabilityRate));
        OnPropertyChanged(nameof(PerformanceRate));
        OnPropertyChanged(nameof(QualityRate));
        OnPropertyChanged(nameof(OeeRate));
        OnPropertyChanged(nameof(DeviceStatusText));
        OnPropertyChanged(nameof(ShiftStatusText));
        OnPropertyChanged(nameof(CurrentRecipeText));
        OnPropertyChanged(nameof(CurrentOrderText));
        OnPropertyChanged(nameof(MotorStatusText));
        OnPropertyChanged(nameof(CylinderStatusText));
        OnPropertyChanged(nameof(CylinderForwardActive));
        OnPropertyChanged(nameof(CylinderBackwardActive));
        OnPropertyChanged(nameof(CylinderOutputActive));
        OnPropertyChanged(nameof(CylinderOutputText));
        OnPropertyChanged(nameof(CylinderCurrentStateText));
        OnPropertyChanged(nameof(CylinderDisplayName));
        OnPropertyChanged(nameof(CylinderActionHint));
        OnPropertyChanged(nameof(CylinderInterlockBaseReady));
        OnPropertyChanged(nameof(CylinderHomeInterlockActive));
        OnPropertyChanged(nameof(CylinderMoveInterlockActive));
        OnPropertyChanged(nameof(CylinderHomeInterlockText));
        OnPropertyChanged(nameof(CylinderMoveInterlockText));
        OnPropertyChanged(nameof(CylinderInterlockHint));
        OnPropertyChanged(nameof(AxisStatusText));
        OnPropertyChanged(nameof(RobotStatusText));
        OnPropertyChanged(nameof(IsDebugMode));
        OnPropertyChanged(nameof(IsDryRunMode));
        OnPropertyChanged(nameof(IsBypassStationMode));
        OnPropertyChanged(nameof(IsManualMode));
        OnPropertyChanged(nameof(IsAutoMode));
        OnPropertyChanged(nameof(RunModeSummary));
        OnPropertyChanged(nameof(StartStopSummary));
        OnPropertyChanged(nameof(StartModeReady));
        OnPropertyChanged(nameof(StartAlarmReady));
        OnPropertyChanged(nameof(StartInterlockReady));
        OnPropertyChanged(nameof(ProductionTrendSummary));
        OnPropertyChanged(nameof(OeeTrendSummary));
        OnPropertyChanged(nameof(AlarmTrendSummary));
        OnPropertyChanged(nameof(FocusAlarmHint));
    }

    private void EvaluateEvents(TagItem tag)
    {
        var bindings = EventBindings.Where(e => e.TagName.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var binding in bindings)
        {
            var triggered = binding.TriggerCondition.Equals("ValueChanged", StringComparison.OrdinalIgnoreCase)
                            || binding.TriggerCondition.Equals(tag.CurrentValue, StringComparison.OrdinalIgnoreCase)
                            || (binding.TriggerCondition.Equals("True", StringComparison.OrdinalIgnoreCase) && tag.CurrentValue.Equals("True", StringComparison.OrdinalIgnoreCase));
            if (!triggered) continue;
            if (tag.IsAlarm) RaiseOrUpdateAlarm(tag.Name, binding.EventName, binding.ActionParameter);
            else Logs.Insert(0, new AlarmRecord { Time = DateTime.Now, Level = "Info", Source = tag.Name, Message = $"事件 {binding.EventName} -> {binding.ActionTarget} {binding.ActionParameter}", Active = false, Acknowledged = true, State = "Logged", Count = 1 });
        }
    }

    private void EvaluateTagState(TagItem tag)
    {
        if (!tag.IsAlarm) return;
        if (string.Equals(tag.CurrentValue, "True", StringComparison.OrdinalIgnoreCase)) RaiseOrUpdateAlarm(tag.Name, tag.Name, "报警触发");
        else ClearAlarm(tag.Name);
    }

    private void RaiseOrUpdateAlarm(string source, string eventName, string detail)
    {
        var key = $"{source}|{detail}";
        if (_activeAlarmMap.TryGetValue(key, out var existing))
        {
            existing.Count += 1;
            existing.Time = DateTime.Now;
            existing.State = existing.Acknowledged ? "Acknowledged" : "Active";
            RefreshAlarmStatistics();
            return;
        }

        var level = source.Contains("EStop", StringComparison.OrdinalIgnoreCase) ? "Alarm"
            : source.Contains("Fault", StringComparison.OrdinalIgnoreCase) ? "Alarm"
            : source.Contains("Axis", StringComparison.OrdinalIgnoreCase) ? "Alarm"
            : "Warning";

        var alarm = new AlarmRecord
        {
            Time = DateTime.Now,
            Level = level,
            Source = source,
            Message = string.IsNullOrWhiteSpace(detail) ? eventName : $"{eventName} - {detail}",
            Active = true,
            Acknowledged = false,
            State = "Active",
            Count = 1,
            HandlingSuggestion = BuildHandlingSuggestion(source, level),
            CauseArchive = BuildCauseArchive(source)
        };
        CurrentAlarms.Insert(0, alarm);
        AlarmHistory.Insert(0, new AlarmRecord { Time = alarm.Time, Level = alarm.Level, Source = alarm.Source, Message = alarm.Message, Active = true, Acknowledged = false, State = "Raised", Count = alarm.Count, HandlingSuggestion = alarm.HandlingSuggestion, CauseArchive = alarm.CauseArchive });
        _activeAlarmMap[key] = alarm;
        Logs.Insert(0, new AlarmRecord { Time = DateTime.Now, Level = "Warning", Source = source, Message = $"报警触发：{alarm.Message}", Active = false, Acknowledged = true, State = "Logged", Count = 1 });
        OnPropertyChanged(nameof(AlarmCount));
        RefreshAlarmStatistics();
    }

    private void ClearAlarm(string source)
    {
        var keys = _activeAlarmMap.Keys.Where(k => k.StartsWith(source + "|", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keys)
        {
            if (!_activeAlarmMap.TryGetValue(key, out var alarm)) continue;
            alarm.Active = false;
            alarm.ClearTime = DateTime.Now;
            alarm.State = alarm.Acknowledged ? "Cleared" : "Cleared-UnAck";
            CurrentAlarms.Remove(alarm);
            AlarmHistory.Insert(0, new AlarmRecord { Time = alarm.Time, Level = alarm.Level, Source = alarm.Source, Message = $"恢复：{alarm.Message}", Active = false, Acknowledged = alarm.Acknowledged, AcknowledgedBy = alarm.AcknowledgedBy, ClearTime = DateTime.Now, State = "Cleared", Count = alarm.Count, HandlingSuggestion = alarm.HandlingSuggestion, CauseArchive = alarm.CauseArchive });
            Logs.Insert(0, new AlarmRecord { Time = DateTime.Now, Level = "Info", Source = source, Message = $"报警恢复：{alarm.Message}", Active = false, Acknowledged = true, State = "Logged", Count = 1 });
            _activeAlarmMap.Remove(key);
        }
        OnPropertyChanged(nameof(AlarmCount));
        RefreshAlarmStatistics();
    }

    private void RefreshParameterPermissions()
    {
        foreach (var parameter in Parameters)
        {
            var canEdit = CanEditParameter(parameter);
            parameter.IsReadOnly = !canEdit;
            parameter.PermissionHint = canEdit ? "可编辑" : $"{parameter.MinRole} 及以上可编辑";
        }
    }

    private void SeedRecipes()
    {
        Recipes.Clear();

        Recipes.Add(CreateRecipeFromCurrentParameters("产品A", "A-001", "V1.0", "标准工艺", true));

        var highSpeedRecipe = CreateRecipeFromCurrentParameters("产品B", "B-002", "V1.1", "高速工艺", false);
        UpdateRecipeParameterValue(highSpeedRecipe, "设备节拍", "3.0");
        UpdateRecipeParameterValue(highSpeedRecipe, "轴速度", "320");
        UpdateRecipeParameterValue(highSpeedRecipe, "气缸延时", "0.15");
        Recipes.Add(highSpeedRecipe);

        var trialRecipe = CreateRecipeFromCurrentParameters("产品C", "C-003", "V2.0", "试产工艺", false);
        UpdateRecipeParameterValue(trialRecipe, "设备节拍", "4.2");
        UpdateRecipeParameterValue(trialRecipe, "真空检测超时", "1.5");
        UpdateRecipeParameterValue(trialRecipe, "滤波时间", "80");
        Recipes.Add(trialRecipe);

        SelectedRecipeName = Recipes.FirstOrDefault(x => x.IsActive)?.Name ?? "产品A";
        RefreshActiveRecipeParameters();
    }

    [RelayCommand]
    private async Task SaveRecipesAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "recipes.json");
        await _recipeService.SaveAsync(path, Recipes);
        AddLog("配方", $"配方已保存：{path}", "Info");
    }

    [RelayCommand]
    private async Task LoadRecipesAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "recipes.json");
        var items = await _recipeService.LoadAsync(path);
        if (items.Count == 0)
        {
            AddLog("配方", "未找到配方文件，保留当前示例配方", "Info");
            return;
        }
        Recipes.Clear();
        foreach (var item in items)
        {
            if (item.Parameters is null)
            {
                item.Parameters = new ObservableCollection<ParameterItem>();
            }
            Recipes.Add(item);
        }
        SelectedRecipeName = Recipes.FirstOrDefault(x => x.IsActive)?.Name ?? Recipes.First().Name;
        RefreshActiveRecipeParameters();
        AddLog("配方", "配方加载完成", "Info");
    }

    [RelayCommand]
    private void ApplyRecipe(string? recipeName)
    {
        if (string.IsNullOrWhiteSpace(recipeName)) return;
        var recipe = Recipes.FirstOrDefault(x => x.Name.Equals(recipeName, StringComparison.OrdinalIgnoreCase));
        if (recipe is null) return;

        foreach (var item in Recipes) item.IsActive = item == recipe;
        SelectedRecipeName = recipe.Name;
        ApplyRecipeParameters(recipe);
        SetTagValue("Recipe_Name", recipe.Name);
        recipe.UpdatedAt = DateTime.Now;
        recipe.UpdatedBy = LoginUser;
        RefreshActiveRecipeParameters();
        AddLog("配方", $"已切换配方：{recipe.Name}", "Info");
        AddAudit("配方切换", recipe.Name, "成功", $"加载 {recipe.Parameters.Count} 项工艺参数");
        UpdateRuntimeVisuals();
    }

    [RelayCommand]
    private void CreateRecipe()
    {
        var baseName = $"新配方{Recipes.Count + 1}";
        var name = baseName;
        var index = 1;
        while (Recipes.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            name = $"{baseName}_{index}";
        }

        var recipe = CreateRecipeFromCurrentParameters(name, $"NEW-{DateTime.Now:HHmmss}", "V1.0", "从当前参数新建", false);
        Recipes.Add(recipe);
        SelectedRecipeName = recipe.Name;
        RefreshActiveRecipeParameters();
        AddLog("配方", $"已新建配方：{recipe.Name}", "Info");
        AddAudit("配方新建", recipe.Name, "成功", "基于当前参数创建");
    }

    [RelayCommand]
    private void DuplicateRecipe()
    {
        var source = Recipes.FirstOrDefault(x => x.Name == SelectedRecipeName);
        if (source is null)
        {
            SystemMessage = "请先选择要复制的配方";
            return;
        }

        var name = $"{source.Name}_Copy";
        var index = 1;
        while (Recipes.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            name = $"{source.Name}_Copy{index}";
        }

        var clone = CloneRecipe(source);
        clone.Name = name;
        clone.IsActive = false;
        clone.UpdatedAt = DateTime.Now;
        clone.UpdatedBy = LoginUser;
        Recipes.Add(clone);
        SelectedRecipeName = clone.Name;
        RefreshActiveRecipeParameters();
        AddLog("配方", $"已复制配方：{source.Name} -> {clone.Name}", "Info");
        AddAudit("配方复制", clone.Name, "成功", $"来源：{source.Name}");
    }

    [RelayCommand]
    private void DeleteRecipe()
    {
        var recipe = Recipes.FirstOrDefault(x => x.Name == SelectedRecipeName);
        if (recipe is null)
        {
            SystemMessage = "请先选择要删除的配方";
            return;
        }

        if (Recipes.Count <= 1)
        {
            ShowPopup("操作禁止", "至少保留一个配方，当前不能删除最后一个配方。", "Warning");
            return;
        }

        if (!RequestConfirmation("删除配方", $"确认删除配方【{recipe.Name}】吗？"))
        {
            return;
        }

        Recipes.Remove(recipe);
        var next = Recipes.First();
        next.IsActive = true;
        SelectedRecipeName = next.Name;
        RefreshActiveRecipeParameters();
        AddLog("配方", $"已删除配方：{recipe.Name}", "Warning");
        AddAudit("配方删除", recipe.Name, "成功", "已从配方列表移除");
    }

    [RelayCommand]
    private void CaptureCurrentParametersToRecipe()
    {
        var recipe = Recipes.FirstOrDefault(x => x.Name == SelectedRecipeName);
        if (recipe is null)
        {
            SystemMessage = "请先选择要保存的配方";
            return;
        }

        recipe.Parameters = CloneParameters(Parameters);
        recipe.UpdatedAt = DateTime.Now;
        recipe.UpdatedBy = LoginUser;
        RefreshActiveRecipeParameters();
        AddLog("配方", $"已用当前参数覆盖配方：{recipe.Name}", "Info");
        AddAudit("配方保存", recipe.Name, "成功", $"保存 {recipe.Parameters.Count} 项参数快照");
    }

    private void SeedTrendSamples()
    {
        TrendSamples.Clear();
        var now = DateTime.Now;
        TrendSamples.Add(new TrendSample { Time = now.AddMinutes(-25), Category = "OEE", Value = 76.2, Source = "System" });
        TrendSamples.Add(new TrendSample { Time = now.AddMinutes(-20), Category = "OEE", Value = 78.1, Source = "System" });
        TrendSamples.Add(new TrendSample { Time = now.AddMinutes(-15), Category = "Production", Value = 980, Source = "Production_Count" });
        TrendSamples.Add(new TrendSample { Time = now.AddMinutes(-10), Category = "Production", Value = 1110, Source = "Production_Count" });
        TrendSamples.Add(new TrendSample { Time = now.AddMinutes(-5), Category = "Alarm", Value = 3, Source = "ActiveAlarmCount" });
        TrendSamples.Add(new TrendSample { Time = now, Category = "Alarm", Value = ActiveAlarmCount, Source = "ActiveAlarmCount" });
    }

    [RelayCommand]
    private async Task LoadTrendHistoryAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "trend-history.csv");
        var items = await _trendHistoryService.LoadAsync(path);
        if (items.Count == 0)
        {
            AddLog("趋势", "未找到历史趋势文件，保留当前示例历史数据", "Info");
            return;
        }
        TrendSamples.Clear();
        foreach (var item in items.OrderByDescending(x => x.Time).Take(200)) TrendSamples.Add(item);
        AddLog("趋势", "历史趋势加载完成", "Info");
    }

    private void SeedFlowSteps()
    {
        FlowSteps.Clear();
        var now = DateTime.Now;
        FlowSteps.Add(new FlowStepRecord { FlowId = "F1", FlowName = "主线1", Time = now.AddSeconds(-30), StartTime = now.AddSeconds(-33), EndTime = now.AddSeconds(-30), DurationSeconds = 3.0, StepNo = 10, Icon = "●", Title = "上料到位", Comment = "工件进入取料位", Result = "完成", ShiftKey = "白班", ArchiveDate = now.ToString("yyyy-MM-dd") });
        FlowSteps.Add(new FlowStepRecord { FlowId = "F1", FlowName = "主线1", Time = now.AddSeconds(-24), StartTime = now.AddSeconds(-27), EndTime = now.AddSeconds(-24), DurationSeconds = 3.0, StepNo = 20, Icon = "●", Title = "气缸夹紧", Comment = "夹紧缸动作完成", Result = "完成", ShiftKey = "白班", ArchiveDate = now.ToString("yyyy-MM-dd") });
        FlowSteps.Add(new FlowStepRecord { FlowId = "F2", FlowName = "主线2", Time = now.AddSeconds(-18), StartTime = now.AddSeconds(-22), EndTime = now.AddSeconds(-18), DurationSeconds = 4.0, StepNo = 30, Icon = "●", Title = "轴移动定位", Comment = "轴到达装配工位", Result = "完成", ShiftKey = "白班", ArchiveDate = now.ToString("yyyy-MM-dd") });
        FlowSteps.Add(new FlowStepRecord { FlowId = "F3", FlowName = "主线3", Time = now.AddSeconds(-12), StartTime = now.AddSeconds(-17), EndTime = now.AddSeconds(-12), DurationSeconds = 5.0, StepNo = 40, Icon = "●", Title = "机械手取放", Comment = "等待真空确认信号", Result = "运行中", ShiftKey = "白班", ArchiveDate = now.ToString("yyyy-MM-dd") });
        CurrentFlowStepNo = 40;
        CurrentFlowComment = "等待真空确认信号，自动流程正在执行 STEP040";
    }

    private void SimulateFlowProgress()
    {
        if (!GetBoolTag("Device_Start"))
        {
            CurrentFlowComment = "设备停止，自动流程待机";
            return;
        }

        var now = DateTime.Now;
        var flows = new[]
        {
            new { Id = "F1", Name = "主线1", BaseStep = 10 },
            new { Id = "F2", Name = "主线2", BaseStep = 20 },
            new { Id = "F3", Name = "主线3", BaseStep = 30 }
        };

        foreach (var flow in flows)
        {
            var latest = FlowSteps.FirstOrDefault(f => f.FlowId == flow.Id);
            var nextStep = latest is null ? flow.BaseStep : latest.StepNo + 10;
            if (nextStep > 60) nextStep = 10;
            var comment = nextStep switch
            {
                10 => $"{flow.Name} 上料检测完成，等待夹紧动作",
                20 => $"{flow.Name} 夹紧完成，准备轴定位",
                30 => $"{flow.Name} 轴定位完成，等待机械手动作",
                40 => $"{flow.Name} 机械手取放中，等待真空确认",
                50 => $"{flow.Name} 装配执行中，等待过站条件",
                60 => $"{flow.Name} 下料完成，准备进入下一循环",
                _ => $"{flow.Name} 自动流程运行中"
            };

            var startTime = latest?.EndTime ?? now.AddSeconds(-3);
            var relatedAlarm = ResolveRelatedAlarm(flow.Name, nextStep);
            var abnormal = !string.IsNullOrWhiteSpace(relatedAlarm);
            var duration = Math.Round((now - startTime).TotalSeconds, 2);
            var timeout = nextStep >= 40 ? 4.0 : 3.0;
            if (duration > timeout && !abnormal)
            {
                abnormal = true;
                relatedAlarm = $"STEP{nextStep:000} 卡步超时 {duration:F2}s";
                AddLog("流程超时", relatedAlarm, "Warning");
            }

            var step = new FlowStepRecord
            {
                FlowId = flow.Id,
                FlowName = flow.Name,
                Time = now,
                StartTime = startTime,
                EndTime = now,
                DurationSeconds = duration,
                StepNo = nextStep,
                Icon = abnormal ? "!" : nextStep == 40 ? ">" : "●",
                Title = $"{flow.Name} STEP{nextStep:000}",
                Comment = comment,
                Result = abnormal ? "异常监视" : "运行中",
                RelatedAlarm = relatedAlarm ?? string.Empty,
                IsAbnormal = abnormal,
                ShiftKey = "白班",
                ArchiveDate = now.ToString("yyyy-MM-dd")
            };

            FlowSteps.Insert(0, step);
            if (IsAutoMode)
            {
                _ = SaveFlowStepCsvAsync(step);
                _ = SaveFlowStepArchiveAsync(step);
            }
        }

        while (FlowSteps.Count > 120)
        {
            FlowSteps.RemoveAt(FlowSteps.Count - 1);
        }

        var head = FlowSteps.FirstOrDefault();
        if (head is not null)
        {
            CurrentFlowStepNo = head.StepNo;
            CurrentFlowComment = head.Comment;
        }

        OnPropertyChanged(nameof(CurrentFlowStepText));
        OnPropertyChanged(nameof(FlowStepTrendPath));
        OnPropertyChanged(nameof(SelectedFlowSummary));
        RefreshFlowView();
        RefreshFlowIssueSummaries();
    }

    private async Task SaveFlowStepCsvAsync(FlowStepRecord step)
    {
        var path = Path.Combine(GetProjectRoot(), "config", "flow-steps.csv");
        await _flowLogCsvService.AppendAsync(path, step);
    }

    private async Task SaveFlowStepArchiveAsync(FlowStepRecord step)
    {
        var fileName = $"flow-{step.FlowName}-{step.ArchiveDate}-{step.ShiftKey}.csv";
        var path = Path.Combine(GetProjectRoot(), "config", "flow-archive", fileName);
        await _flowLogCsvService.AppendAsync(path, step);
    }

    private async Task SaveTrendHistoryAsync()
    {
        var path = Path.Combine(GetProjectRoot(), "config", "trend-history.csv");
        var now = DateTime.Now;
        var samples = new List<TrendSample>
        {
            new() { Time = now, Category = "OEE", Value = OeeRate, Source = "OEE" },
            new() { Time = now, Category = "Production", Value = ProductionCount, Source = "Production_Count" },
            new() { Time = now, Category = "Alarm", Value = ActiveAlarmCount, Source = "ActiveAlarmCount" }
        };
        foreach (var sample in samples)
        {
            TrendSamples.Insert(0, sample);
        }
        while (TrendSamples.Count > 300)
        {
            TrendSamples.RemoveAt(TrendSamples.Count - 1);
        }
        await _trendHistoryService.AppendAsync(path, samples);
    }

    private void HighlightAlarm(string keyword)
    {
        foreach (var item in CurrentAlarms) item.IsHighlighted = false;
        foreach (var item in AlarmHistory) item.IsHighlighted = false;
        if (string.IsNullOrWhiteSpace(keyword)) return;
        foreach (var item in CurrentAlarms.Where(x => x.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) || x.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase))) item.IsHighlighted = true;
        foreach (var item in AlarmHistory.Where(x => x.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) || x.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase))) item.IsHighlighted = true;
    }

    private void HighlightFlow(string flowName, int stepNo)
    {
        foreach (var item in FlowSteps) item.IsHighlighted = false;
        foreach (var item in FlowSteps.Where(x => x.FlowName.Equals(flowName, StringComparison.OrdinalIgnoreCase) && x.StepNo == stepNo)) item.IsHighlighted = true;
    }

    private void RefreshFlowView()
    {
        var now = DateTime.Now;
        FlowStepsView.Filter = item =>
        {
            if (item is not FlowStepRecord step) return false;
            if (SelectedFlowFilter != "全部" && !step.FlowName.Equals(SelectedFlowFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (SelectedFlowTimeRange == "本班次" && step.Time < now.AddHours(-8)) return false;
            if (SelectedFlowTimeRange == "今日" && step.Time.Date != now.Date) return false;
            if (SelectedFlowTimeRange == "近7天" && step.Time < now.AddDays(-7)) return false;
            if (SelectedFlowStepFilter != "全部" && step.StepNo.ToString() != SelectedFlowStepFilter) return false;
            if (ShowOnlyAbnormalFlow && !step.IsAbnormal) return false;
            return true;
        };
        FlowStepsView.Refresh();
    }

    private void RefreshFlowIssueSummaries()
    {
        FlowIssueSummaries.Clear();
        var source = FlowSteps.AsEnumerable();
        if (!source.Any())
        {
            OnPropertyChanged(nameof(FlowRankingSummary));
            OnPropertyChanged(nameof(FlowIssueTrendPath));
            return;
        }

        var topAbnormalFlow = source.Where(x => x.IsAbnormal)
            .GroupBy(x => x.FlowName)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (topAbnormalFlow is not null)
        {
            FlowIssueSummaries.Add(new FlowIssueSummary
            {
                Category = "异常最多流程",
                Name = topAbnormalFlow.Key,
                Metric = $"{topAbnormalFlow.Count()} 次",
                Conclusion = $"{topAbnormalFlow.Key} 异常次数最高，需要优先排查"
            });
        }

        var topAbnormalStep = source.Where(x => x.IsAbnormal)
            .GroupBy(x => x.StepNo)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (topAbnormalStep is not null)
        {
            FlowIssueSummaries.Add(new FlowIssueSummary
            {
                Category = "最易异常步骤",
                Name = $"STEP {topAbnormalStep.Key:000}",
                Metric = $"{topAbnormalStep.Count()} 次",
                Conclusion = "该步骤最容易出现异常，可重点分析联锁与等待条件"
            });
        }

        var longestStep = source.GroupBy(x => x.StepNo)
            .Select(g => new { StepNo = g.Key, Avg = g.Average(x => x.DurationSeconds) })
            .OrderByDescending(x => x.Avg)
            .FirstOrDefault();
        if (longestStep is not null)
        {
            FlowIssueSummaries.Add(new FlowIssueSummary
            {
                Category = "平均耗时最长",
                Name = $"STEP {longestStep.StepNo:000}",
                Metric = $"{longestStep.Avg:F2} s",
                Conclusion = "该步骤平均耗时最长，建议检查机构节拍、等待信号与工艺时序"
            });
        }

        OnPropertyChanged(nameof(FlowRankingSummary));
        OnPropertyChanged(nameof(FlowIssueTrendPath));
    }

    private string ResolveRelatedAlarm(string flowName, int stepNo)
    {
        var alarms = CurrentAlarms.Concat(AlarmHistory).ToList();
        if (!alarms.Any()) return string.Empty;

        if (flowName == "主线1" && stepNo >= 40)
        {
            return alarms.FirstOrDefault(a => a.Source.Contains("Vacuum", StringComparison.OrdinalIgnoreCase) || a.Message.Contains("真空", StringComparison.OrdinalIgnoreCase))?.Message ?? string.Empty;
        }
        if (flowName == "主线2" && stepNo >= 30)
        {
            return alarms.FirstOrDefault(a => a.Source.Contains("Axis", StringComparison.OrdinalIgnoreCase) || a.Message.Contains("轴", StringComparison.OrdinalIgnoreCase))?.Message ?? string.Empty;
        }
        if (flowName == "主线3" && stepNo >= 20)
        {
            return alarms.FirstOrDefault(a => a.Source.Contains("Air", StringComparison.OrdinalIgnoreCase) || a.Message.Contains("气压", StringComparison.OrdinalIgnoreCase) || a.Source.Contains("Motor", StringComparison.OrdinalIgnoreCase))?.Message ?? string.Empty;
        }

        return string.Empty;
    }

    private void RefreshMonitorView()
    {
        MonitorTagsView.Filter = item =>
        {
            if (item is not TagItem tag) return false;
            if (IsMonitorIoPageVisible)
            {
                return IsPlcIoTag(tag);
            }
            if (SelectedMonitorCategory == "全部") return true;
            return tag.Category.Equals(SelectedMonitorCategory, StringComparison.OrdinalIgnoreCase)
                || tag.Group.Equals(SelectedMonitorCategory, StringComparison.OrdinalIgnoreCase);
        };
        MonitorTagsView.Refresh();
    }

    private static bool IsPlcIoTag(TagItem tag)
    {
        var name = (tag.Name ?? string.Empty).Trim().ToUpperInvariant();
        var nodeId = (tag.NodeId ?? string.Empty).Trim().ToUpperInvariant();
        var category = (tag.Category ?? string.Empty).Trim().ToUpperInvariant();
        var group = (tag.Group ?? string.Empty).Trim().ToUpperInvariant();

        if (category == "IO" || group == "INPUT" || group == "OUTPUT")
        {
            return true;
        }

        var ioPrefixes = new[] { "X_", "Y_", "IX", "IB", "IW", "ID", "QX", "QB", "QW", "QD", "I", "Q", "X", "Y" };
        if (ioPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
        }

        return nodeId.Contains(".X", StringComparison.Ordinal)
            || nodeId.Contains(".Y", StringComparison.Ordinal)
            || nodeId.Contains(".I", StringComparison.Ordinal)
            || nodeId.Contains(".Q", StringComparison.Ordinal);
    }

    private void RefreshAlarmStatistics()
    {
        var merged = AlarmHistory
            .Concat(CurrentAlarms)
            .GroupBy(a => new { a.Source, a.Level })
            .Select(g => new AlarmRecord
            {
                Time = g.Max(x => x.Time),
                Level = g.Key.Level,
                Source = g.Key.Source,
                Message = $"{g.Key.Source} 累计 {g.Sum(x => Math.Max(1, x.Count))} 次",
                Active = g.Any(x => x.Active),
                Acknowledged = g.All(x => x.Acknowledged),
                State = g.Any(x => x.Active) ? "重点关注" : "已恢复",
                Count = g.Sum(x => Math.Max(1, x.Count))
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Active)
            .ToList();

        AlarmStatistics.Clear();
        foreach (var item in merged) AlarmStatistics.Add(item);

        var now = DateTime.Now;
        AlarmStatisticsView.Filter = item =>
        {
            if (item is not AlarmRecord alarm) return false;
            if (SelectedAlarmLevel != "全部" && !alarm.Level.Equals(SelectedAlarmLevel, StringComparison.OrdinalIgnoreCase)) return false;
            if (SelectedAlarmTimeRange == "本班次" && alarm.Time < now.AddHours(-8)) return false;
            if (SelectedAlarmTimeRange == "今日" && alarm.Time.Date != now.Date) return false;
            if (SelectedAlarmTimeRange == "近7天" && alarm.Time < now.AddDays(-7)) return false;
            if (ShowOnlyFocusAlarms && alarm.Count < 3 && !alarm.Active) return false;
            return true;
        };
        AlarmStatisticsView.Refresh();
        OnPropertyChanged(nameof(FocusAlarmHint));
    }

    private void RefreshActiveRecipeParameters()
    {
        ActiveRecipeParameters.Clear();
        var recipe = Recipes.FirstOrDefault(x => x.Name == SelectedRecipeName) ?? Recipes.FirstOrDefault(x => x.IsActive);
        if (recipe?.Parameters is null) return;
        foreach (var parameter in recipe.Parameters)
        {
            ActiveRecipeParameters.Add(CloneParameter(parameter));
        }
    }

    private void ApplyRecipeParameters(RecipeItem recipe)
    {
        foreach (var snapshot in recipe.Parameters)
        {
            var target = Parameters.FirstOrDefault(x => x.Name.Equals(snapshot.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null) continue;
            target.Value = snapshot.Value;
        }
    }

    private RecipeItem CreateRecipeFromCurrentParameters(string name, string productCode, string version, string description, bool isActive)
    {
        return new RecipeItem
        {
            Name = name,
            ProductCode = productCode,
            Version = version,
            Description = description,
            IsActive = isActive,
            UpdatedAt = DateTime.Now,
            UpdatedBy = LoginUser,
            Parameters = CloneParameters(Parameters)
        };
    }

    private RecipeItem CloneRecipe(RecipeItem source)
    {
        return new RecipeItem
        {
            Name = source.Name,
            ProductCode = source.ProductCode,
            Version = source.Version,
            Description = source.Description,
            IsActive = source.IsActive,
            UpdatedAt = source.UpdatedAt,
            UpdatedBy = source.UpdatedBy,
            Parameters = CloneParameters(source.Parameters)
        };
    }

    private ObservableCollection<ParameterItem> CloneParameters(IEnumerable<ParameterItem> source)
    {
        return new ObservableCollection<ParameterItem>(source.Select(CloneParameter));
    }

    private ParameterItem CloneParameter(ParameterItem source)
    {
        return new ParameterItem
        {
            Category = source.Category,
            Name = source.Name,
            Value = source.Value,
            Unit = source.Unit,
            Description = source.Description,
            MinRole = source.MinRole,
            IsReadOnly = source.IsReadOnly,
            PermissionHint = source.PermissionHint
        };
    }

    private void UpdateRecipeParameterValue(RecipeItem recipe, string parameterName, string value)
    {
        var parameter = recipe.Parameters.FirstOrDefault(x => x.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        if (parameter is not null)
        {
            parameter.Value = value;
        }
    }

    private bool CanEditParameter(ParameterItem parameter) => CurrentUserRole >= parameter.MinRole;
    private bool GetBoolTag(string tagName) => string.Equals(GetTagValue(tagName), "True", StringComparison.OrdinalIgnoreCase);
    private bool GetCylinderBool(string configuredTagName, string primarySuffix = "", string? secondarySuffix = null, string fallbackTagName = "", bool fallbackValue = false)
    {
        if (!string.IsNullOrWhiteSpace(configuredTagName))
        {
            var configuredTag = FindTagByNameOrNodeId(configuredTagName);
            if (configuredTag is not null)
            {
                return string.Equals(configuredTag.CurrentValue, "True", StringComparison.OrdinalIgnoreCase);
            }
        }

        var primaryValue = GetImportedCylinderTagValue(primarySuffix);
        if (!string.IsNullOrWhiteSpace(primaryValue) && !string.Equals(primaryValue, "--", StringComparison.Ordinal))
        {
            return string.Equals(primaryValue, "True", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(secondarySuffix))
        {
            var secondaryValue = GetImportedCylinderTagValue(secondarySuffix);
            if (!string.IsNullOrWhiteSpace(secondaryValue) && !string.Equals(secondaryValue, "--", StringComparison.Ordinal))
            {
                return string.Equals(secondaryValue, "True", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackTagName))
        {
            return GetBoolTag(fallbackTagName);
        }

        return fallbackValue;
    }

    private bool GetBoolParameter(string parameterName, bool fallback = false)
    {
        var item = Parameters.FirstOrDefault(p => p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        return item is null ? fallback : bool.TryParse(item.Value, out var result) ? result : fallback;
    }
    private int GetIntTag(string tagName, int fallback = 0) => int.TryParse(GetTagValue(tagName), out var value) ? value : fallback;
    private double GetDoubleTag(string tagName, double fallback = 0) => double.TryParse(GetTagValue(tagName), out var value) ? value : fallback;
    private string GetTagValue(string tagName) => FindTagByNameOrNodeId(tagName)?.CurrentValue ?? "--";
    private void SetTagValue(string tagName, string value) { var tag = FindTagByNameOrNodeId(tagName); if (tag is not null) tag.CurrentValue = value; }
    private TagItem? FindTagByNameOrNodeId(string tagNameOrNodeId) =>
        Tags.FirstOrDefault(t => t.Name.Equals(tagNameOrNodeId, StringComparison.OrdinalIgnoreCase))
        ?? Tags.FirstOrDefault(t => t.NodeId.Equals(tagNameOrNodeId, StringComparison.OrdinalIgnoreCase));
    private string GetImportedCylinderTagValue(string suffix) => FindImportedCylinderTag(suffix)?.CurrentValue ?? "--";
    private TagItem? FindImportedCylinderTag(string suffix)
    {
        var prefix = ResolveImportedCylinderPrefix();
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        var fullNodeId = prefix + suffix;
        return Tags.FirstOrDefault(tag => tag.NodeId.Equals(fullNodeId, StringComparison.OrdinalIgnoreCase))
            ?? Tags.FirstOrDefault(tag => tag.Name.Equals(fullNodeId.Replace("Application.", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveImportedCylinderPrefix()
    {
        var inHomeTag = Tags.FirstOrDefault(tag =>
            tag.NodeId.Contains(".CylCtrl[", StringComparison.OrdinalIgnoreCase)
            && tag.NodeId.EndsWith(".Status.InHome", StringComparison.OrdinalIgnoreCase));

        if (inHomeTag is not null)
        {
            return inHomeTag.NodeId[..^".Status.InHome".Length];
        }

        var devStatusTag = Tags.FirstOrDefault(tag =>
            tag.NodeId.Contains(".CylCtrl[", StringComparison.OrdinalIgnoreCase)
            && tag.NodeId.EndsWith(".DevStatus.Sensor_Home", StringComparison.OrdinalIgnoreCase));

        return devStatusTag is null ? string.Empty : devStatusTag.NodeId[..^".DevStatus.Sensor_Home".Length];
    }

    private string GetImportedCylinderDisplayName()
    {
        var prefix = ResolveImportedCylinderPrefix();
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return "夹紧气缸 CY01";
        }

        var commentTag = FindImportedCylinderTag(".Parm.Comment");
        if (commentTag is not null && !string.IsNullOrWhiteSpace(commentTag.CurrentValue) && !string.Equals(commentTag.CurrentValue, "--", StringComparison.Ordinal))
        {
            return commentTag.CurrentValue;
        }

        var indexMarker = prefix.LastIndexOf('[');
        var normalized = prefix.Replace("Application.", string.Empty, StringComparison.OrdinalIgnoreCase);
        return indexMarker >= 0 ? $"气缸 {normalized[indexMarker..]}" : normalized;
    }

    private void AddTag(TagItem tag) => Tags.Add(tag);
    private double CalculateAvailability() { var run = GetDoubleTag("Machine_RunTimeMin", 420); var stop = GetDoubleTag("Machine_StopTimeMin", 34); var total = run + stop; return total <= 0 ? 0 : Math.Round(run / total * 100, 1); }
    private double CalculatePerformance() { var ideal = GetDoubleTag("Ideal_Cycle_Time", 2.8); var actual = GetDoubleTag("Cycle_Time", 3.2); if (ideal <= 0 || actual <= 0) return 0; return Math.Round(Math.Min(100, ideal / actual * 100), 1); }
    private double CalculateQuality() { var total = ProductionCount; return total <= 0 ? 0 : Math.Round((double)GoodCount / total * 100, 1); }
    private static string BuildSparklinePath(IEnumerable<double> values)
    {
        var points = values.ToList();
        if (points.Count == 0) return "";
        var max = points.Max();
        var min = points.Min();
        var range = Math.Max(1, max - min);
        var stepX = points.Count == 1 ? 100 : 100.0 / (points.Count - 1);
        var coordinates = points.Select((v, i) =>
        {
            var x = i * stepX;
            var y = 40 - ((v - min) / range * 32 + 4);
            return $"{x.ToString("F1", CultureInfo.InvariantCulture)},{y.ToString("F1", CultureInfo.InvariantCulture)}";
        }).ToArray();
        return "M " + string.Join(" L ", coordinates);
    }

    private string BuildFlowRankingSummary()
    {
        var source = FlowSteps.AsEnumerable();
        if (!source.Any()) return "暂无流程排行数据";

        var topAvgCycle = source.GroupBy(x => x.FlowName)
            .Select(g => new { Name = g.Key, Avg = g.Average(x => x.DurationSeconds) })
            .OrderByDescending(x => x.Avg)
            .FirstOrDefault();

        var topAlarmStep = source.Where(x => x.IsAbnormal)
            .GroupBy(x => x.StepNo)
            .Select(g => new { Step = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        var longestSingle = source.OrderByDescending(x => x.DurationSeconds).FirstOrDefault();

        return $"平均节拍最长：{topAvgCycle?.Name ?? "--"} {topAvgCycle?.Avg:F2}s | 最多报警步骤：STEP {topAlarmStep?.Step ?? 0:000} {topAlarmStep?.Count ?? 0}次 | 最长卡步：{longestSingle?.FlowName ?? "--"} STEP {longestSingle?.StepNo ?? 0:000} {longestSingle?.DurationSeconds ?? 0:F2}s";
    }

    private string ResolveCylinderCurrentStateText()
    {
        if (IsAutoMode && !CylinderBackwardActive)
        {
            return "设备手动后气缸未复原，请把气缸复原后再切换到自动模式";
        }

        if (CylinderForwardActive && CylinderBackwardActive)
        {
            return "动作位和初始位感应器都点亮，检查感应器状态";
        }

        if (!CylinderForwardActive && !CylinderBackwardActive)
        {
            return "动作位和初始位感应器都不亮，检查感应器状态";
        }

        if (!CylinderOutputActive && CylinderForwardActive && !CylinderBackwardActive)
        {
            return "气缸前往初始位超时，检查动作位传感器状态";
        }

        if (CylinderOutputActive && CylinderBackwardActive && !CylinderForwardActive)
        {
            return "气缸前往动作位超时，检查初始位传感器状态";
        }

        if (!CylinderHomeInterlockActive && !CylinderBackwardActive)
        {
            return "初始位互锁条件未满足，检查互锁机构状态";
        }

        if (!CylinderMoveInterlockActive && !CylinderForwardActive)
        {
            return "动作位互锁条件未满足，检查互锁机构状态";
        }

        return CylinderOutputActive ? "气缸正在前往动作位" : "气缸已在初始位待命";
    }

    private static string BuildHandlingSuggestion(string source, string level) => source switch
    {
        var s when s.Contains("EStop", StringComparison.OrdinalIgnoreCase) => "优先检查安全回路、急停按钮与安全继电器",
        var s when s.Contains("Motor", StringComparison.OrdinalIgnoreCase) => "检查电机过载、接触器、驱动器与机械卡滞",
        var s when s.Contains("Axis", StringComparison.OrdinalIgnoreCase) => "检查伺服报警代码、编码器、负载与运动参数",
        var s when s.Contains("Air", StringComparison.OrdinalIgnoreCase) => "检查气源压力、过滤器、气管泄漏与阀组",
        var s when s.Contains("Vacuum", StringComparison.OrdinalIgnoreCase) => "检查真空发生器、吸盘漏气、真空开关与工件状态",
        _ => level switch
        {
            "Alarm" => "优先安排停机排查并记录根因",
            "Error" => "安排设备点检并确认恢复条件",
            "Warning" => "纳入巡检重点，观察是否重复发生",
            _ => "保留记录，持续观察"
        }
    };

    private static string BuildCauseArchive(string source) => source switch
    {
        var s when s.Contains("EStop", StringComparison.OrdinalIgnoreCase) => "安全保护动作/人工触发/回路异常",
        var s when s.Contains("Motor", StringComparison.OrdinalIgnoreCase) => "过载/堵转/接线松动/机构干涉",
        var s when s.Contains("Axis", StringComparison.OrdinalIgnoreCase) => "伺服参数异常/机械阻力/原点偏移",
        var s when s.Contains("Air", StringComparison.OrdinalIgnoreCase) => "供气不足/泄漏/过滤器堵塞",
        var s when s.Contains("Vacuum", StringComparison.OrdinalIgnoreCase) => "真空建立失败/吸附不良/工件偏移",
        _ => "待补充归档原因"
    };

    private void Parameters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ParameterItem>()) item.PropertyChanged += (_, _) => RefreshParameterPermissions();
        }
        RefreshParameterPermissions();
        ParametersView.Refresh();
    }

    private void AddLog(string source, string message, string level)
    {
        Logs.Insert(0, new AlarmRecord { Time = DateTime.Now, Source = source, Message = message, Level = level, Active = false, Acknowledged = true, State = "Logged", Count = 1 });
        OnPropertyChanged(nameof(FocusAlarmHint));
    }

    private void AddAudit(string action, string target, string result, string detail)
    {
        OperationAudits.Insert(0, new OperationAuditRecord
        {
            Time = DateTime.Now,
            User = LoginUser,
            Action = action,
            Target = target,
            Result = result,
            Detail = detail
        });
    }

    private void ShowPopup(string title, string message, string level = "Info")
    {
        SystemMessage = message;
        AddLog("弹窗", $"{title}: {message}", level == "Error" ? "Error" : "Warning");
        AddAudit("弹窗", title, level, message);
        PopupRequested?.Invoke(title, message, level);
    }

    public void UpdateStartHoldProgress(double progress)
    {
        StartHoldProgress = progress;
    }

    private bool RequestConfirmation(string title, string message)
    {
        var result = ConfirmationRequested?.Invoke(title, message) ?? true;
        AddAudit("确认框", title, result ? "确认" : "取消", message);
        return result;
    }

    private static object ConvertValue(string rawValue, string dataType) => dataType.ToLowerInvariant() switch
    {
        "boolean" or "bool" => bool.Parse(rawValue),
        "int16" or "short" => short.Parse(rawValue),
        "int32" or "int" => int.Parse(rawValue),
        "int64" or "long" => long.Parse(rawValue),
        "float" or "single" => float.Parse(rawValue),
        "double" => double.Parse(rawValue),
        _ => rawValue
    };

    private DesignerElement CreateDesignerElement(string type, double left, double top, string? text = null, string? tagBinding = null, string? navigationTarget = null) => new()
    {
        Name = $"{type}_{DesignerElements.Count + 1}",
        ElementType = type,
        Left = Snap(left),
        Top = Snap(top),
        Width = GetDefaultWidth(type),
        Height = GetDefaultHeight(type),
        Text = text ?? GetDefaultText(type),
        TagBinding = tagBinding ?? Tags.FirstOrDefault()?.Name ?? string.Empty,
        CommandBinding = type is "Button" or "Motor" or "Cylinder" or "Stopper" or "Robot" ? "ToggleBool" : string.Empty,
        NavigationTarget = navigationTarget ?? string.Empty,
        Background = GetDefaultBackground(type),
        BorderBrush = "#64748B",
        Foreground = "#FFFFFF",
        FontSize = type is "AlarmBanner" ? 18 : 14,
        SnapToGrid = true
    };

    private DesignerElement CloneElement(DesignerElement e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        ElementType = e.ElementType,
        Left = e.Left,
        Top = e.Top,
        Width = e.Width,
        Height = e.Height,
        Text = e.Text,
        Background = e.Background,
        Foreground = e.Foreground,
        BorderBrush = e.BorderBrush,
        FontSize = e.FontSize,
        TagBinding = e.TagBinding,
        CommandBinding = e.CommandBinding,
        NavigationTarget = e.NavigationTarget,
        SnapToGrid = e.SnapToGrid
    };

    private static double GetDefaultWidth(string type) => type switch { "Label" => 160, "ValueDisplay" => 200, "AlarmBanner" => 280, "Motor" => 180, "Cylinder" => 180, "Axis" => 210, "Robot" => 190, "Stopper" => 170, "PageButton" => 140, _ => 120 };
    private static double GetDefaultHeight(string type) => type switch { "AlarmBanner" => 60, "Motor" => 100, "Cylinder" => 90, "Axis" => 100, "Robot" => 100, "Stopper" => 80, _ => 40 };
    private static string GetDefaultBackground(string type) => type switch { "Button" => "#2563EB", "Indicator" => "#475569", "Label" => "#1E293B", "ValueDisplay" => "#0F766E", "AlarmBanner" => "#F59E0B", "Motor" => "#3B82F6", "Cylinder" => "#10B981", "Axis" => "#8B5CF6", "Robot" => "#EC4899", "Stopper" => "#F59E0B", "PageButton" => "#6366F1", _ => "#64748B" };
    private static string GetDefaultText(string type) => type switch
    {
        "Button" => "按钮",
        "Indicator" => "指示灯",
        "Label" => "文本标签",
        "ValueDisplay" => "数值显示",
        "AlarmBanner" => "报警条",
        "Motor" => "电机模块",
        "Cylinder" => "气缸模块",
        "Axis" => "轴模块",
        "Robot" => "机械手模块",
        "Stopper" => "挡停模块",
        "PageButton" => "页面跳转",
        _ => "控件"
    };

    private void SeedAutoProgramFlow()
    {
        AutoProgramFlowNodes.Clear();
        AutoProgramFlowNodes.Add(new AutoProgramFlowNode { StepNo = 10, Title = "上料检测", Action = "检测载具到位与产品存在信号", NextStep = "20", Left = 70, Top = 40, Fill = "#DBEAFE" });
        AutoProgramFlowNodes.Add(new AutoProgramFlowNode { StepNo = 20, Title = "夹紧定位", Action = "下压夹爪并确认夹紧完成", NextStep = "30", Left = 70, Top = 170, Fill = "#DCFCE7" });
        AutoProgramFlowNodes.Add(new AutoProgramFlowNode { StepNo = 30, Title = "机械手取放", Action = "机械手取料并放入加工位", NextStep = "40", Left = 70, Top = 300, Fill = "#FEF3C7" });
        AutoProgramFlowNodes.Add(new AutoProgramFlowNode { StepNo = 40, Title = "装配执行", Action = "执行装配动作并监视超时", NextStep = "50", Left = 70, Top = 430, Fill = "#FCE7F3" });
        AutoProgramFlowNodes.Add(new AutoProgramFlowNode { StepNo = 50, Title = "结果判定", Action = "判定 OK/NG 并写入结果", NextStep = "60", Left = 70, Top = 560, Fill = "#E0E7FF", IsDecision = true });
        AutoProgramFlowNodes.Add(new AutoProgramFlowNode { StepNo = 60, Title = "下料复位", Action = "下料并将流程复位到待机", NextStep = "END", Left = 70, Top = 690, Fill = "#F1F5F9" });
        RefreshAutoProgramSummary();
    }

    private void RebuildAutoFlowLayout()
    {
        var ordered = AutoProgramFlowNodes.OrderBy(x => x.StepNo).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Left = ordered[index].IsDecision ? 110 : 70;
            ordered[index].Top = 40 + index * 130;
        }

        RefreshAutoProgramSummary();
    }

    private GeneratedProgramArtifact CreateGeneratedArtifact(string outputDirectory, string fileName, string content)
    {
        var fullPath = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        return new GeneratedProgramArtifact
        {
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            OutputPath = fullPath,
            Content = content
        };
    }

    private string ReadGenerationTemplate(string templateFileName)
    {
        var templateDirectory = Path.Combine(GetApplicationRoot(), "Templates", SelectedIoPlcTemplate);
        var templatePath = Path.Combine(templateDirectory, templateFileName);
        return File.Exists(templatePath) ? File.ReadAllText(templatePath, Encoding.UTF8) : string.Empty;
    }

    private string BuildAutoTemplateProgram(string template, string controlDb, int stationNo, IReadOnlyList<AutoProgramFlowNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var startStep = nodes.FirstOrDefault()?.StepNo ?? 10;
        var stepCases = new StringBuilder();
        foreach (var node in nodes)
        {
            stepCases.AppendLine($"\t{node.StepNo}:");
            stepCases.AppendLine($"\t\tAuto[{stationNo}].Comment:=\"{node.Title} - {node.Action}\";");
            stepCases.AppendLine($"\t\tAuto[{stationNo}].Step:={ResolveNextStepValue(node.NextStep, node.StepNo)};");
        }

        var result = template
            .Replace("{StationNo}", stationNo.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{GeneratedAt}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal)
            .Replace("{ControlDb}", controlDb, StringComparison.Ordinal)
            .Replace("Auto[{StationNo}].Step:=10;", $"Auto[{stationNo}].Step:={startStep};", StringComparison.Ordinal)
            .Replace("\t10:\r\n\t\tAuto[{StationNo}].Comment:=\"缁涘绶熷銉ㄥ濮濄儵顎冮柊宥囩枂\";\r\n\t\tAuto[{StationNo}].Step:=1000;", stepCases.ToString().TrimEnd(), StringComparison.Ordinal)
            .Replace("Auto[{StationNo}].Comment:=\"自动流程启动\";", $"Auto[{stationNo}].Comment:=\"{AutoProgramName} 启动\";", StringComparison.Ordinal);

        return result;
    }

    private string BuildInitTemplateProgram(string template, string controlDb, int stationNo, IReadOnlyList<AutoProgramFlowNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var summaryComment = nodes.Count == 0 ? "等待初始化条件" : $"初始化 {AutoProgramName}，共 {nodes.Count} 步";
        var result = template
            .Replace("{StationNo}", stationNo.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{GeneratedAt}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal)
            .Replace("{ControlDb}", controlDb, StringComparison.Ordinal)
            .Replace("Init[{StationNo}].Comment:=\"初始化开始\";", $"Init[{stationNo}].Comment:=\"{AutoProgramName} 初始化开始\";", StringComparison.Ordinal)
            .Replace("Init[{StationNo}].Comment:=\"初始化摘要\";", $"Init[{stationNo}].Comment:=\"{summaryComment}\";", StringComparison.Ordinal)
            .Replace("Init[{StationNo}].Comment:=\"初始化完成\";", $"Init[{stationNo}].Comment:=\"{AutoProgramName} 初始化完成\";", StringComparison.Ordinal);

        return result;
    }

    private static string ResolveNextStepValue(string? nextStep, int currentStep)
    {
        if (string.IsNullOrWhiteSpace(nextStep))
        {
            return "10";
        }

        var trimmed = nextStep.Trim();
        if (trimmed.Equals("END", StringComparison.OrdinalIgnoreCase))
        {
            return "10";
        }

        return int.TryParse(trimmed, out var stepNo) ? stepNo.ToString(CultureInfo.InvariantCulture) : (currentStep + 10).ToString(CultureInfo.InvariantCulture);
    }

    private static int ResolveOperationBaseNumber(string? operationNumber)
    {
        if (string.IsNullOrWhiteSpace(operationNumber))
        {
            return 1000;
        }

        var digits = new string(operationNumber.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number * 100 : 1000;
    }

    private static int ResolveStationNo(string? stationText)
    {
        if (string.IsNullOrWhiteSpace(stationText))
        {
            return 1;
        }

        var digits = new string(stationText.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var stationNo) && stationNo > 0 ? stationNo : 1;
    }

    private void RefreshAutoProgramSummary()
    {
        OnPropertyChanged(nameof(AutoProgramHeadline));
        OnPropertyChanged(nameof(AutoProgramSummary));
        OnPropertyChanged(nameof(HasGeneratedAutoPrograms));
        OnPropertyChanged(nameof(SelectedGeneratedAutoProgramContent));
    }

    private void RefreshIoGenerationSummary(IoGenerationResult? result = null)
    {
        var inputCount = result?.InputCount ?? IoTableRows.Count(r => !string.IsNullOrWhiteSpace(r.InputAddress));
        var outputCount = result?.OutputCount ?? IoTableRows.Count(r => !string.IsNullOrWhiteSpace(r.OutputAddress));
        IoImportSummary = IoTableRows.Count == 0
            ? "尚未导入 IO 表"
            : $"已导入 {IoTableRows.Count} 行，输入 {inputCount} 点，输出 {outputCount} 点";
        OnPropertyChanged(nameof(IoGenerationHeadline));
        OnPropertyChanged(nameof(IoGenerationCountSummary));
        OnPropertyChanged(nameof(HasGeneratedIoPrograms));
        OnPropertyChanged(nameof(SelectedGeneratedIoProgramContent));
    }

    private void EnsureDefaultManualCylinderBlock()
    {
        ManualCylinderBlocks.Clear();
        if (ManualCylinderBlocks.Count > 0)
        {
            return;
        }

        ManualCylinderBlocks.Add(CreateManualCylinderBlock(1, "夹紧气缸 CY01"));
        RefreshManualCylinderBlockStates();
    }

    private void ManualCylinderBlocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ManualCylinderBlockItem>())
            {
                item.PropertyChanged += ManualCylinderBlockItem_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ManualCylinderBlockItem>())
            {
                item.PropertyChanged -= ManualCylinderBlockItem_PropertyChanged;
            }
        }

        ManualCylinderBlocksView.Refresh();
        OnPropertyChanged(nameof(ManualCylinderBlockCards));
    }

    private void ManualCylinderBlockItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManualCylinderBlockItem.DisplayOrder))
        {
            ManualCylinderBlocksView.Refresh();
        }

        if (e.PropertyName == nameof(ManualCylinderBlockItem.DisplayOrder)
            || e.PropertyName == nameof(ManualCylinderBlockItem.DisplayName)
            || e.PropertyName == nameof(ManualCylinderBlockItem.CylinderIndex))
        {
            OnPropertyChanged(nameof(ManualCylinderBlockCards));
        }
    }

    private void RebuildManualCylinderBlocksFromIo()
    {
        var cylinders = ExtractCylinderDefinitionsFromIo()
            .GroupBy(item => item.CylinderIndex)
            .Select(group => group.First())
            .ToList();
        ManualCylinderBlocks.Clear();

        if (cylinders.Count == 0)
        {
            EnsureDefaultManualCylinderBlock();
            return;
        }

        foreach (var cylinder in cylinders.OrderBy(item => item.CylinderIndex))
        {
            ManualCylinderBlocks.Add(cylinder);
            EnsureCylinderTagsForBlock(cylinder);
        }

        RefreshManualCylinderBlockStates();
    }

    private IEnumerable<ManualCylinderBlockItem> ExtractCylinderDefinitionsFromIo()
    {
        var groups = new Dictionary<int, ManualCylinderBlockItem>();

        foreach (var row in IoTableRows)
        {
            CollectCylinderDefinition(groups, row.InputComment);
            CollectCylinderDefinition(groups, row.OutputComment);
        }

        return groups.Values;
    }

    private void CollectCylinderDefinition(IDictionary<int, ManualCylinderBlockItem> groups, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        var match = Regex.Match(comment, "(CY\\d{1,3})", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return;
        }

        var indexText = new string(match.Value.Where(char.IsDigit).ToArray());
        if (!int.TryParse(indexText, out var index) || index <= 0)
        {
            return;
        }

        if (!groups.TryGetValue(index, out var block))
        {
            block = CreateManualCylinderBlock(index, ExtractCylinderDisplayName(comment));
            groups[index] = block;
        }
        else if (string.IsNullOrWhiteSpace(block.DisplayName) || block.DisplayName.StartsWith("气缸 ", StringComparison.Ordinal))
        {
            block.DisplayName = ExtractCylinderDisplayName(comment);
        }
    }

    private static ManualCylinderBlockItem CreateManualCylinderBlock(int index, string displayName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(displayName) ? $"气缸 CY{index:00}" : displayName;
        var root = $"Application.DB8050_DriveControl.CylCtrl[{index}]";
        return new ManualCylinderBlockItem
        {
            CylinderIndex = index,
            DisplayOrder = index,
            DisplayName = normalizedName,
            HomeCommandTagName = $"{root}.Cmd.ManuToHome",
            WorkCommandTagName = $"{root}.Cmd.ManuToWork",
            HomeSensorTagName = $"{root}.Status.InHome",
            WorkSensorTagName = $"{root}.Status.InWork",
            HomeInterlockTagName = $"{root}.Parm.IC_Home",
            WorkInterlockTagName = $"{root}.Parm.IC_Work"
        };
    }

    private static string ExtractCylinderDisplayName(string comment)
    {
        var text = (comment ?? string.Empty).Trim();
        var underscoreIndex = text.LastIndexOf('_');
        return underscoreIndex > 0 ? text[..underscoreIndex] : text;
    }

    private void EnsureCylinderTagsForBlock(ManualCylinderBlockItem block)
    {
        EnsurePlaceholderTag(block.HomeCommandTagName, true);
        EnsurePlaceholderTag(block.WorkCommandTagName, true);
        EnsurePlaceholderTag(block.HomeSensorTagName, false);
        EnsurePlaceholderTag(block.WorkSensorTagName, false);
        EnsurePlaceholderTag(block.HomeInterlockTagName, false);
        EnsurePlaceholderTag(block.WorkInterlockTagName, false);
    }

    private void EnsurePlaceholderTag(string nodeId, bool writable)
    {
        if (FindTagByNameOrNodeId(nodeId) is not null)
        {
            return;
        }

        Tags.Add(new TagItem
        {
            Name = nodeId.Replace("Application.", string.Empty, StringComparison.OrdinalIgnoreCase),
            NodeId = nodeId,
            DataType = "Boolean",
            Category = "Cylinder",
            Group = "Imported",
            Direction = writable ? "Output" : "Input",
            CurrentValue = "False",
            Description = "由 IO 气缸块自动补齐的固定变量路径",
            IsWritable = writable
        });
    }

    private void RefreshManualCylinderBlockStates()
    {
        foreach (var block in ManualCylinderBlocks)
        {
            block.HomeActive = GetBoolTag(block.HomeSensorTagName);
            block.WorkActive = GetBoolTag(block.WorkSensorTagName);
            block.HomeInterlockActive = GetBoolTag(block.HomeInterlockTagName) || !FindTagExists(block.HomeInterlockTagName);
            block.WorkInterlockActive = GetBoolTag(block.WorkInterlockTagName) || !FindTagExists(block.WorkInterlockTagName);
            block.OutputActive = GetBoolTag(block.WorkCommandTagName);
            block.StatusText = block.WorkActive ? "前到位" : block.HomeActive ? "后到位" : "切换中";
            block.CurrentStateText = block.WorkActive && block.HomeActive
                ? "动作位和初始位感应器都点亮，检查感应器状态"
                : !block.WorkActive && !block.HomeActive
                    ? "动作位和初始位感应器都不亮，检查感应器状态"
                    : block.OutputActive ? "气缸正在前往动作位" : "气缸已在初始位待命";
            block.InterlockHint = !block.HomeInterlockActive || !block.WorkInterlockActive
                ? "互锁条件未满足"
                : "互锁正常";
        }
    }

    private bool FindTagExists(string tagNameOrNodeId) => FindTagByNameOrNodeId(tagNameOrNodeId) is not null;

    private string GetApplicationRoot()
    {
        var currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "PlcOpcUaHmi.csproj")))
        {
            return currentDirectory;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlcOpcUaHmi.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private string GetProjectRoot() => GetApplicationRoot();
    private double Snap(double value) => EnableGridSnap ? Math.Round(value / GridSize) * GridSize : value;
    private void DesignerElements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncCanvasToPage();
    private void DesignerPages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { if (DesignerPages.Count > 0 && SelectedDesignerPage is null) SelectedDesignerPage = DesignerPages[0]; }
}

