using System.Globalization;
using System.IO;
using CsvHelper;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class CsvImportService
{
    public async Task<List<TagItem>> ImportTagsAsync(string csvFilePath)
    {
        using var reader = new StreamReader(csvFilePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = new List<TagCsvRecord>();
        await foreach (var record in csv.GetRecordsAsync<TagCsvRecord>())
        {
            records.Add(record);
        }

        return records.Select(r => new TagItem
        {
            Name = r.Name ?? string.Empty,
            NodeId = r.NodeId ?? string.Empty,
            DataType = string.IsNullOrWhiteSpace(r.DataType) ? "String" : r.DataType,
            Category = string.IsNullOrWhiteSpace(r.Category) ? "General" : r.Category,
            Group = string.IsNullOrWhiteSpace(r.Group) ? "Default" : r.Group,
            Direction = string.IsNullOrWhiteSpace(r.Direction) ? "Input" : r.Direction,
            Description = r.Description ?? string.Empty,
            IsAlarm = r.IsAlarm,
            IsWritable = r.IsWritable
        }).ToList();
    }

    private class TagCsvRecord
    {
        public string? Name { get; set; }
        public string? NodeId { get; set; }
        public string? DataType { get; set; }
        public string? Category { get; set; }
        public string? Group { get; set; }
        public string? Direction { get; set; }
        public string? Description { get; set; }
        public bool IsAlarm { get; set; }
        public bool IsWritable { get; set; }
    }
}
