using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class AlarmService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task SaveHistoryAsync(string filePath, IEnumerable<AlarmRecord> alarms)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, alarms, _jsonOptions);
    }

    public async Task<List<AlarmRecord>> LoadHistoryAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new List<AlarmRecord>();
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<AlarmRecord>>(stream, _jsonOptions) ?? new List<AlarmRecord>();
    }
}
