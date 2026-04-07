using System.IO;
using System.Text.Json;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class DesignerProjectService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task SaveProjectAsync(string filePath, DesignerProject project)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, project, _jsonOptions);
    }

    public async Task<DesignerProject?> LoadProjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<DesignerProject>(stream, _jsonOptions);
    }
}
