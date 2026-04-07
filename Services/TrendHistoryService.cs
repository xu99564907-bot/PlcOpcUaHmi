using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class TrendHistoryService
{
    public async Task AppendAsync(string path, IEnumerable<TrendSample> samples)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var exists = File.Exists(path);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        if (!exists)
        {
            await writer.WriteLineAsync("Time,Category,Value,Source");
        }
        foreach (var s in samples)
        {
            await writer.WriteLineAsync($"{s.Time:yyyy-MM-dd HH:mm:ss},{s.Category},{s.Value:F3},{s.Source}");
        }
    }

    public async Task<List<TrendSample>> LoadAsync(string path)
    {
        var result = new List<TrendSample>();
        if (!File.Exists(path)) return result;
        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 4) continue;
            result.Add(new TrendSample
            {
                Time = DateTime.TryParse(parts[0], out var t) ? t : DateTime.Now,
                Category = parts[1],
                Value = double.TryParse(parts[2], out var v) ? v : 0,
                Source = parts[3]
            });
        }
        return result;
    }
}
