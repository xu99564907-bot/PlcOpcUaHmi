using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class RecipeService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task SaveAsync(string path, IEnumerable<RecipeItem> items)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(items, Options);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<List<RecipeItem>> LoadAsync(string path)
    {
        if (!File.Exists(path)) return new List<RecipeItem>();
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<RecipeItem>>(json, Options) ?? new List<RecipeItem>();
    }
}
