using System.IO;
using System.Text.Json;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class ConfigurationService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task SaveAsync(string filePath, AppConfig config)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, config, _jsonOptions);
    }

    public async Task<AppConfig?> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<AppConfig>(stream, _jsonOptions);
    }
}
