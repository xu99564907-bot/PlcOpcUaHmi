using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class ParameterService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task SaveAsync(string filePath, IEnumerable<ParameterItem> parameters)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, parameters, _jsonOptions);
    }

    public async Task<List<ParameterItem>> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new List<ParameterItem>();
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<ParameterItem>>(stream, _jsonOptions) ?? new List<ParameterItem>();
    }
}
