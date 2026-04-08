using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class IoProgramGenerationService
{
    public async Task<IoGenerationResult> GenerateAsync(
        IEnumerable<IoTableRow> rows,
        IoGenerationSettings settings,
        string projectRoot)
    {
        var inputSignals = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.InputAddress))
            .Select(r => new IoSignal(r.InputAddress.Trim(), r.InputComment.Trim(), "DI"))
            .ToList();
        var outputSignals = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.OutputAddress))
            .Select(r => new IoSignal(r.OutputAddress.Trim(), r.OutputComment.Trim(), "DO"))
            .ToList();

        if (inputSignals.Count == 0 && outputSignals.Count == 0)
        {
            throw new InvalidOperationException("当前没有可生成的 IO 数据，请先导入 IO 表。");
        }

        var templateDirectory = GetTemplateDirectory(projectRoot, settings.PlcType);
        var outputDirectory = Path.Combine(projectRoot, "Generated", "Program");
        Directory.CreateDirectory(outputDirectory);

        var dbName = NormalizeOperationNumber(settings.OperationNumber).Replace("OP", "DB", StringComparison.OrdinalIgnoreCase);
        var controlDb = BuildControlDbName(settings.OperationNumber);
        var driveDb = BuildDriveDbName(settings.OperationNumber);

        var artifacts = new List<GeneratedProgramArtifact>
        {
            CreateArtifact(outputDirectory, $"{dbName}_IO", BuildVarIoProgram(inputSignals, outputSignals, templateDirectory)),
            CreateArtifact(outputDirectory, "DI_ACT_Comment", BuildCommentProgram(inputSignals, true, templateDirectory)),
            CreateArtifact(outputDirectory, "DO_ACT_Comment", BuildCommentProgram(outputSignals, false, templateDirectory))
        };

        artifacts.AddRange(BuildObjectProgramArtifacts(inputSignals, outputSignals, templateDirectory, outputDirectory, controlDb, driveDb, NormalizeOperationNumber(settings.OperationNumber)));

        foreach (var artifact in artifacts)
        {
            await File.WriteAllTextAsync(artifact.OutputPath, artifact.Content, Encoding.Unicode);
        }

        return new IoGenerationResult
        {
            OutputDirectory = outputDirectory,
            Artifacts = artifacts,
            InputCount = inputSignals.Count,
            OutputCount = outputSignals.Count
        };
    }

    public void OpenOutputDirectory(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            throw new DirectoryNotFoundException("尚未生成 IO 程序目录。");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = outputDirectory,
            UseShellExecute = true
        });
    }

    private static string GetTemplateDirectory(string projectRoot, string plcType)
    {
        var templateName = string.IsNullOrWhiteSpace(plcType) ? "汇川中型PLC" : plcType.Trim();
        var path = Path.Combine(projectRoot, "Templates", templateName);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"未找到模板目录：{path}");
        }

        return path;
    }

    private static GeneratedProgramArtifact CreateArtifact(string outputDirectory, string fileBaseName, string content)
    {
        return new GeneratedProgramArtifact
        {
            DisplayName = fileBaseName,
            FileName = $"{fileBaseName}.txt",
            OutputPath = Path.Combine(outputDirectory, $"{fileBaseName}.txt"),
            Content = content
        };
    }

    private static IEnumerable<GeneratedProgramArtifact> BuildObjectProgramArtifacts(
        IReadOnlyList<IoSignal> inputs,
        IReadOnlyList<IoSignal> outputs,
        string templateDirectory,
        string outputDirectory,
        string controlDb,
        string driveDb,
        string operationNumber)
    {
        var artifacts = new List<GeneratedProgramArtifact>();

        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Cylinder", BuildCylinderProgram(inputs, outputs, templateDirectory, controlDb, driveDb));
        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Vacuum", BuildVacuumProgram(inputs, outputs, templateDirectory, controlDb, driveDb));
        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Sensor", BuildSensorProgram(inputs, templateDirectory, driveDb));
        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Motor", BuildMotorProgram(inputs, outputs, templateDirectory, controlDb, driveDb));
        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Axis", BuildAxisProgram(inputs, outputs, templateDirectory, controlDb, driveDb));
        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Rotdisk", BuildRotdiskProgram(inputs, outputs, templateDirectory, controlDb, driveDb));
        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Epson", BuildEpsonProgram(inputs, outputs, templateDirectory, controlDb, driveDb));
        AddArtifactIfAny(artifacts, outputDirectory, "ACT_Kuka", BuildKukaProgram(inputs, outputs, templateDirectory, controlDb, driveDb));
        return artifacts;
    }

    private static void AddArtifactIfAny(ICollection<GeneratedProgramArtifact> artifacts, string outputDirectory, string fileBaseName, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        artifacts.Add(CreateArtifact(outputDirectory, fileBaseName, content));
    }

    private static string BuildVarIoProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory)
    {
        var template = ReadTemplate(templateDirectory, "Var_IO.txt");
        var declarations = new List<string>();
        declarations.AddRange(inputs.Select(signal => $"\t{BuildDeclarationName(signal)}\tAT\t%{NormalizeAddress(signal.Address)}:BOOL;"));
        declarations.AddRange(outputs.Select(signal => $"\t{BuildDeclarationName(signal)}\tAT\t%{NormalizeAddress(signal.Address)}:BOOL;"));
        return template.Replace("{DECLARATIONS}", string.Join(Environment.NewLine, declarations)).Trim();
    }

    private static string BuildCommentProgram(IReadOnlyList<IoSignal> signals, bool isInput, string templateDirectory)
    {
        var template = ReadTemplate(templateDirectory, isInput ? "InputComment.txt" : "OutputComment.txt");
        var cases = new List<string>();

        if (signals.Count > 0)
        {
            var pages = signals
                .Select((signal, index) => new { signal, index })
                .GroupBy(x => x.index / 16)
                .ToList();

            foreach (var page in pages)
            {
                cases.Add($"\t\t\t{page.Key + 1}:");
                foreach (var entry in page)
                {
                    var io = NormalizeAddress(entry.signal.Address);
                    var comment = string.IsNullOrWhiteSpace(entry.signal.Comment) ? "未配置 IO" : entry.signal.Comment;
                    cases.Add($"\t\t\t\tMonitor[{entry.index % 16}].Comment\t:= \"\t{io}  {comment}\t\";");
                }

                for (var fillerIndex = page.Count(); fillerIndex < 16; fillerIndex++)
                {
                    cases.Add($"\t\t\t\tMonitor[{fillerIndex}].Comment\t:= \"\t未配置 IO\t\";");
                }

                var firstWord = GetWordAddress(page.First().signal.Address);
                var statusPrefix = isInput ? "IW" : "QW";
                cases.Add($"\t\t\t\tStatus:=\t%{statusPrefix}{firstWord / 2};");
            }
        }

        var defaultCase = new List<string> { "\t\t\tELSE" };
        for (var i = 0; i < 16; i++)
        {
            defaultCase.Add($"\t\t\t\tMonitor[{i}].Comment\t:= \"\t未配置 IO\t\";");
        }

        return template
            .Replace("{COMMENT_CASES}", string.Join(Environment.NewLine, cases))
            .Replace("{DEFAULT_CASE}", string.Join(Environment.NewLine, defaultCase))
            .Trim();
    }

    private static string BuildCylinderProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory, string controlDb, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "CylinderProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, outputs, ObjectType.Cylinder),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["Enable"] = "TRUE",
                ["Name"] = Quote(item.Name),
                ["ControlDb"] = controlDb,
                ["DriveDb"] = driveDb,
                ["Sensor_Work"] = ResolveSignalReference(item.Inputs, "工作位", "伸出", "前位", "接料位"),
                ["Sensor_Home"] = ResolveSignalReference(item.Inputs, "原位", "回位", "缩回", "后位"),
                ["IC_Home"] = "TRUE",
                ["IC_Work"] = "TRUE",
                ["Model"] = "0",
                ["Valve_Work"] = ResolveSignalReference(item.Outputs, "工作位", "伸出", "前进", "接料位"),
                ["Valve_Home"] = ResolveSignalReference(item.Outputs, "原位", "回位", "缩回", "复位")
            }));
    }

    private static string BuildVacuumProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory, string controlDb, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "VacuumProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, outputs, ObjectType.Vacuum),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["Enable"] = "TRUE",
                ["Name"] = Quote(item.Name),
                ["ControlDb"] = controlDb,
                ["DriveDb"] = driveDb,
                ["Sensor"] = ResolveSignalReference(item.Inputs, "真空", "压力", "检测", "开关"),
                ["IC_Home"] = "TRUE",
                ["IC_Work"] = "TRUE",
                ["Valve_Work"] = ResolveSignalReference(item.Outputs, "吸真空", "真空", "吸附"),
                ["Valve_Home"] = ResolveSignalReference(item.Outputs, "破真空", "吹气", "释放")
            }));
    }

    private static string BuildSensorProgram(IReadOnlyList<IoSignal> inputs, string templateDirectory, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "SensorProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, Array.Empty<IoSignal>(), ObjectType.Sensor),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["DriveDb"] = driveDb,
                ["Sensor"] = BuildSignalReference(item.Inputs.FirstOrDefault()),
                ["Name"] = Quote(item.Name)
            }));
    }

    private static string BuildMotorProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory, string controlDb, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "MotorProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, outputs, ObjectType.Motor),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["Name"] = Quote(item.Name),
                ["Alarm"] = ResolveSignalReference(item.Inputs, "报警", "故障", "alarm", "fault"),
                ["ControlDb"] = controlDb,
                ["DriveDb"] = driveDb,
                ["Mode"] = "1",
                ["MotorFor"] = ResolveSignalReference(item.Outputs, "正转", "前进", "运行", "启动"),
                ["MotorBack"] = ResolveSignalReference(item.Outputs, "反转", "后退", "反向"),
                ["Reset"] = ResolveSignalReference(item.Outputs, "复位", "reset")
            }));
    }

    private static string BuildAxisProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory, string controlDb, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "AxisProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, outputs, ObjectType.Axis),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["Name"] = Quote(item.Name),
                ["ControlDb"] = controlDb,
                ["DriveDb"] = driveDb,
                ["IntermediateStop"] = "TRUE",
                ["IC_Home"] = "TRUE",
                ["IC_ABS"] = "TRUE",
                ["IC_JOG"] = "TRUE"
            }));
    }

    private static string BuildRotdiskProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory, string controlDb, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "RotdiskProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, outputs, ObjectType.Rotdisk),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["Name"] = Quote(item.Name),
                ["ControlDb"] = controlDb,
                ["DriveDb"] = driveDb,
                ["Sensor"] = ResolveSignalReference(item.Inputs, "到位", "检测", "sensor"),
                ["IC"] = "TRUE",
                ["DelayTime"] = "T#500MS",
                ["MotorOut"] = ResolveSignalReference(item.Outputs, "转动", "运行", "启动")
            }));
    }

    private static string BuildEpsonProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory, string controlDb, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "EpsonRobProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, outputs, ObjectType.EpsonRobot),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["DriveDb"] = driveDb,
                ["ControlDb"] = controlDb,
                ["Name"] = Quote(item.Name),
                ["IP"] = "192.168.0.10",
                ["Port"] = "5000"
            }));
    }

    private static string BuildKukaProgram(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, string templateDirectory, string controlDb, string driveDb)
    {
        var template = ReadTemplate(templateDirectory, "KukaRobProgram.txt");
        return BuildObjectProgram(
            BuildObjects(inputs, outputs, ObjectType.KukaRobot),
            item => ReplaceTokens(template, new Dictionary<string, string>
            {
                ["Index"] = item.Index.ToString(),
                ["DriveDb"] = driveDb,
                ["ControlDb"] = controlDb,
                ["Name"] = Quote(item.Name),
                ["IP"] = "192.168.0.20",
                ["Port"] = "7000"
            }));
    }

    private static string BuildAutoProgram(string templateDirectory, string controlDb, int stationNo)
    {
        var template = ReadTemplate(templateDirectory, "Auto.txt");
        return ReplaceTokens(template, new Dictionary<string, string>
        {
            ["StationNo"] = stationNo.ToString(),
            ["GeneratedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["ControlDb"] = controlDb
        });
    }

    private static string BuildInitProgram(string templateDirectory, string controlDb, int stationNo)
    {
        var template = ReadTemplate(templateDirectory, "Init.txt");
        return ReplaceTokens(template, new Dictionary<string, string>
        {
            ["StationNo"] = stationNo.ToString(),
            ["GeneratedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["ControlDb"] = controlDb
        });
    }

    private static string BuildObjectProgram(IEnumerable<ObjectProgramItem> items, Func<ObjectProgramItem, string> buildItem)
    {
        var sections = items
            .Where(item => item.Index != int.MaxValue)
            .Select(buildItem)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => $"{text}{Environment.NewLine}///////{Environment.NewLine}///////{Environment.NewLine}///////");
        return string.Join(Environment.NewLine + Environment.NewLine, sections).Trim();
    }

    private static List<ObjectProgramItem> BuildObjects(IReadOnlyList<IoSignal> inputs, IReadOnlyList<IoSignal> outputs, ObjectType targetType)
    {
        var groups = new Dictionary<string, ObjectProgramItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var signal in inputs)
        {
            AddSignalToObjectGroup(groups, signal, targetType, true);
        }

        foreach (var signal in outputs)
        {
            AddSignalToObjectGroup(groups, signal, targetType, false);
        }

        return groups.Values.OrderBy(item => item.Index).ThenBy(item => item.Key).ToList();
    }

    private static void AddSignalToObjectGroup(IDictionary<string, ObjectProgramItem> groups, IoSignal signal, ObjectType targetType, bool isInput)
    {
        var descriptor = DescribeObject(signal.Comment, targetType);
        if (descriptor is null)
        {
            return;
        }

        if (!groups.TryGetValue(descriptor.Key, out var item))
        {
            item = new ObjectProgramItem(descriptor.Type, descriptor.Key, descriptor.DisplayName, descriptor.Index);
            groups[descriptor.Key] = item;
        }

        if (isInput)
        {
            item.Inputs.Add(signal);
        }
        else
        {
            item.Outputs.Add(signal);
        }
    }

    private static ObjectDescriptor? DescribeObject(string comment, ObjectType targetType)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var normalized = comment.Trim();
        var candidates = new[]
        {
            CreateDescriptor(ObjectType.Cylinder, normalized, "(CY\\d{1,3})", "CY", "气缸"),
            CreateDescriptor(ObjectType.Vacuum, normalized, "(VAC\\d{1,3})", "VAC", "真空"),
            CreateDescriptor(ObjectType.Sensor, normalized, "(SENSOR\\d{1,3})", "SENSOR", "传感器", "感应"),
            CreateDescriptor(ObjectType.Motor, normalized, "(MOTOR\\d{1,3})", "MOTOR", "电机"),
            CreateDescriptor(ObjectType.Axis, normalized, "(AXIS\\d{1,3})", "AXIS", "轴", "伺服"),
            CreateDescriptor(ObjectType.Rotdisk, normalized, "(ROTDISK\\d{1,3})", "ROTDISK", "转盘"),
            CreateDescriptor(ObjectType.EpsonRobot, normalized, "(EPSONROB\\d{1,3})", "EPSONROB", "EPSON", "机器人"),
            CreateDescriptor(ObjectType.KukaRobot, normalized, "(KUKAROB\\d{1,3})", "KUKAROB", "KUKA", "机器人")
        };

        return candidates.FirstOrDefault(item => item is not null && item.Type == targetType);
    }

    private static ObjectDescriptor? CreateDescriptor(ObjectType type, string comment, string keyPattern, params string[] keywords)
    {
        var match = Regex.Match(comment, keyPattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var key = match.Groups[1].Value.ToUpperInvariant();
            return new ObjectDescriptor(type, key, ExtractDisplayName(comment), ParseIndexFromKey(key));
        }

        if (!keywords.Any(keyword => comment.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var displayName = ExtractDisplayName(comment);
        return new ObjectDescriptor(type, SanitizeKey(type, displayName), displayName, int.MaxValue);
    }

    private static int ParseIndexFromKey(string key)
    {
        var digits = new string(key.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var index) && index > 0 ? index : int.MaxValue;
    }

    private static string ExtractDisplayName(string comment)
    {
        var text = comment.Trim();
        var underscoreIndex = text.LastIndexOf('_');
        return underscoreIndex > 0 ? text[..underscoreIndex] : text;
    }

    private static string SanitizeKey(ObjectType type, string value)
    {
        var safe = Regex.Replace(value.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? type.ToString().ToUpperInvariant() : $"{type}_{safe}";
    }

    private static string ReadTemplate(string templateDirectory, string fileName)
    {
        var path = Path.Combine(templateDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"未找到模板文件：{path}");
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string ReplaceTokens(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;
        foreach (var pair in values)
        {
            result = result.Replace($"{{{pair.Key}}}", pair.Value);
        }

        return result.Trim();
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string ResolveSignalReference(IReadOnlyList<IoSignal> signals, params string[] keywords)
    {
        var signal = signals.FirstOrDefault(item => keywords.Any(keyword => item.Comment.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            ?? signals.FirstOrDefault();
        return BuildSignalReference(signal);
    }

    private static string BuildSignalReference(IoSignal? signal)
    {
        if (signal is null)
        {
            return "FALSE";
        }

        var comment = string.IsNullOrWhiteSpace(signal.Comment)
            ? "Signal"
            : Regex.Replace(signal.Comment.Trim(), "[\\s\\./\\\\-]+", "_");
        return $"{NormalizeAddress(signal.Address).Replace('.', '_')}_{comment}";
    }

    private static string BuildDeclarationName(IoSignal signal)
    {
        var basis = string.IsNullOrWhiteSpace(signal.Comment)
            ? $"{signal.Direction}_{NormalizeAddress(signal.Address)}"
            : $"{signal.Direction}_{NormalizeAddress(signal.Address)}_{signal.Comment}";

        var builder = new StringBuilder();
        foreach (var ch in basis)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
            }
            else if (ch is '.' or '-' or ' ' or '/')
            {
                builder.Append('_');
            }
        }

        if (builder.Length == 0)
        {
            builder.Append(signal.Direction);
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private static string NormalizeAddress(string address)
    {
        return address.Trim().TrimStart('%').ToUpperInvariant();
    }

    private static int GetWordAddress(string address)
    {
        var normalized = NormalizeAddress(address);
        var dotIndex = normalized.IndexOf('.');
        if (dotIndex < 0 || dotIndex <= 2)
        {
            return 0;
        }

        return int.TryParse(normalized.Substring(2, dotIndex - 2), out var word) ? word : 0;
    }

    private static string NormalizeOperationNumber(string operationNumber)
    {
        return string.IsNullOrWhiteSpace(operationNumber) ? "OP10" : operationNumber.Trim().ToUpperInvariant();
    }

    private static int ParseOperationNumber(string operationNumber)
    {
        var digits = new string(NormalizeOperationNumber(operationNumber).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) && value > 0 ? value : 10;
    }

    private static string BuildControlDbName(string operationNumber)
    {
        var stationNumber = ParseOperationNumber(operationNumber);
        return $"DB{stationNumber * 100}";
    }

    private static string BuildDriveDbName(string operationNumber)
    {
        var stationNumber = ParseOperationNumber(operationNumber);
        return $"DB{stationNumber * 100 + 50}";
    }

    private sealed record IoSignal(string Address, string Comment, string Direction);
    private sealed record ObjectDescriptor(ObjectType Type, string Key, string DisplayName, int Index);

    private sealed class ObjectProgramItem
    {
        public ObjectProgramItem(ObjectType type, string key, string name, int index)
        {
            Type = type;
            Key = key;
            Name = name;
            Index = index;
        }

        public ObjectType Type { get; }
        public string Key { get; }
        public string Name { get; }
        public int Index { get; }
        public List<IoSignal> Inputs { get; } = new();
        public List<IoSignal> Outputs { get; } = new();
    }

    private enum ObjectType
    {
        Cylinder,
        Vacuum,
        Sensor,
        Motor,
        Axis,
        Rotdisk,
        EpsonRobot,
        KukaRobot
    }
}
