using System.IO;
using System.Text.Json;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class NamingRulesService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task<NamingRulesConfig> LoadOrCreateAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            var defaultConfig = NamingRulesConfig.CreateDefault();
            await SaveAsync(filePath, defaultConfig);
            return defaultConfig;
        }

        await using var stream = File.OpenRead(filePath);
        var config = await JsonSerializer.DeserializeAsync<NamingRulesConfig>(stream, _jsonOptions);
        if (config is not null)
        {
            Normalize(config);
            return config;
        }

        var fallback = NamingRulesConfig.CreateDefault();
        await SaveAsync(filePath, fallback);
        return fallback;
    }

    public async Task SaveAsync(string filePath, NamingRulesConfig config)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Normalize(config);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, config, _jsonOptions);
    }

    private static void Normalize(NamingRulesConfig config)
    {
        config.Cylinder ??= new CylinderNamingRules();
        config.Axis ??= new AxisNamingRules();
        config.Cylinder.MotionAssignmentMode ??= "ByRowOrder";
        config.Cylinder.FirstOccurrenceRole ??= "Work";
        config.Cylinder.SecondOccurrenceRole ??= "Home";
        config.Cylinder.GroupedSuffixes ??= new List<string> { "A", "B", "C", "D" };
        config.Cylinder.SegmentSeparators ??= new List<string> { "_" };
        config.Cylinder.HomeKeywords ??= new List<string>();
        config.Cylinder.WorkKeywords ??= new List<string>();
        config.Cylinder.VerticalKeywords ??= new List<string>();
        config.Axis.PositiveKeywords ??= new List<string>();
        config.Axis.NegativeKeywords ??= new List<string>();

        if (config.Cylinder.SegmentSeparators.Count == 0)
        {
            config.Cylinder.SegmentSeparators.Add("_");
        }

        if (config.Cylinder.GroupedSuffixes.Count == 0)
        {
            config.Cylinder.GroupedSuffixes.AddRange(new[] { "A", "B", "C", "D" });
        }
    }
}
