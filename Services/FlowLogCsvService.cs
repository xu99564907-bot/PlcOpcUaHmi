using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class FlowLogCsvService
{
    public async Task AppendAsync(string filePath, FlowStepRecord step)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var exists = File.Exists(filePath);
        await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        if (!exists)
        {
            await writer.WriteLineAsync("FlowId,FlowName,Time,StartTime,EndTime,DurationSeconds,StepNo,Icon,Title,Comment,Result,RelatedAlarm,IsAbnormal,ShiftKey,ArchiveDate");
        }

        await writer.WriteLineAsync(string.Join(",",
            Escape(step.FlowId),
            Escape(step.FlowName),
            step.Time.ToString("yyyy-MM-dd HH:mm:ss"),
            step.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
            step.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
            step.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture),
            step.StepNo.ToString(CultureInfo.InvariantCulture),
            Escape(step.Icon),
            Escape(step.Title),
            Escape(step.Comment),
            Escape(step.Result),
            Escape(step.RelatedAlarm),
            step.IsAbnormal ? "true" : "false",
            Escape(step.ShiftKey),
            Escape(step.ArchiveDate)));
    }

    public async Task<List<FlowStepRecord>> LoadAsync(string filePath)
    {
        var result = new List<FlowStepRecord>();
        if (!File.Exists(filePath))
        {
            return result;
        }

        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        foreach (var line in lines.Skip(1))
        {
            var parts = ParseCsvLine(line);
            if (parts.Count < 15) continue;
            result.Add(new FlowStepRecord
            {
                FlowId = parts[0],
                FlowName = parts[1],
                Time = DateTime.TryParse(parts[2], out var time) ? time : DateTime.Now,
                StartTime = DateTime.TryParse(parts[3], out var startTime) ? startTime : DateTime.Now,
                EndTime = DateTime.TryParse(parts[4], out var endTime) ? endTime : DateTime.Now,
                DurationSeconds = double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ? duration : 0,
                StepNo = int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stepNo) ? stepNo : 0,
                Icon = parts[7],
                Title = parts[8],
                Comment = parts[9],
                Result = parts[10],
                RelatedAlarm = parts[11],
                IsAbnormal = bool.TryParse(parts[12], out var abnormal) && abnormal,
                ShiftKey = parts[13],
                ArchiveDate = parts[14]
            });
        }
        return result;
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
