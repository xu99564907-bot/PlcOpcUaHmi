using System.IO;
using System.Text.Json;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class DesignerLayoutService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task SavePageAsync(string filePath, DesignerPage page)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, page, _jsonOptions);
    }

    public async Task<DesignerPage?> LoadPageAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<DesignerPage>(stream, _jsonOptions);
    }
}
