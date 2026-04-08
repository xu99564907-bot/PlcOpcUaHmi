using System.Text;
using System.IO;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class IoTableImportService
{
    public async Task<IoTableImportResult> ImportAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension != ".csv" && extension != ".txt")
        {
            throw new NotSupportedException("当前版本支持 CSV/TXT 格式的 IO 表。若源文件是 Excel，请先另存为 CSV。");
        }

        var encoding = DetectEncoding(filePath);
        var rawText = await File.ReadAllTextAsync(filePath, encoding);
        var rawLines = rawText.Split(["\r\n", "\n"], StringSplitOptions.None);
        var headers = ParseHeaders(rawLines);
        var rows = ParseRows(rawLines);
        if (rows.Count == 0)
        {
            return new IoTableImportResult
            {
                SourceFilePath = filePath,
                EncodingCodePage = encoding.CodePage,
                Headers = headers
            };
        }

        var inputs = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.InputAddress))
            .Select(r => new IoSignal(r.InputAddress.Trim(), r.InputComment.Trim()))
            .ToList();
        var outputs = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.OutputAddress))
            .Select(r => new IoSignal(r.OutputAddress.Trim(), r.OutputComment.Trim()))
            .ToList();

        var normalizedInputs = NormalizeSignals(inputs, "IX");
        var normalizedOutputs = NormalizeSignals(outputs, "QX");
        var max = Math.Max(normalizedInputs.Count, normalizedOutputs.Count);
        var normalizedRows = new List<IoTableRow>(max);
        for (var i = 0; i < max; i++)
        {
            normalizedRows.Add(new IoTableRow
            {
                InputAddress = i < normalizedInputs.Count ? normalizedInputs[i].Address : string.Empty,
                InputComment = i < normalizedInputs.Count ? normalizedInputs[i].Comment : string.Empty,
                OutputAddress = i < normalizedOutputs.Count ? normalizedOutputs[i].Address : string.Empty,
                OutputComment = i < normalizedOutputs.Count ? normalizedOutputs[i].Comment : string.Empty
            });
        }

        return new IoTableImportResult
        {
            SourceFilePath = filePath,
            EncodingCodePage = encoding.CodePage,
            Headers = headers,
            Rows = normalizedRows
        };
    }

    public async Task SaveAsync(string filePath, IEnumerable<IoTableRow> rows, IReadOnlyList<string>? headers, int encodingCodePage)
    {
        var encoding = TryGetEncoding(encodingCodePage) ?? Encoding.UTF8;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var headerColumns = headers is { Count: >= 4 }
            ? headers.Take(4).ToList()
            : new List<string> { "输入地址", "输入变量", "输出地址", "输出变量" };

        var lines = new List<string>
        {
            string.Join(",", headerColumns.Select(EscapeCsv))
        };

        foreach (var row in rows)
        {
            lines.Add(string.Join(",",
                EscapeCsv(row.InputAddress),
                EscapeCsv(row.InputComment),
                EscapeCsv(row.OutputAddress),
                EscapeCsv(row.OutputComment)));
        }

        await File.WriteAllTextAsync(filePath, string.Join(Environment.NewLine, lines), encoding);
    }

    private static List<IoTableRow> ParseRows(IEnumerable<string> lines)
    {
        var parsed = new List<IoTableRow>();
        var materialized = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(SplitLine)
            .Where(columns => columns.Count > 0)
            .ToList();

        if (materialized.Count == 0)
        {
            return parsed;
        }

        var startIndex = IsHeaderRow(materialized[0]) ? 1 : 0;
        for (var i = startIndex; i < materialized.Count; i++)
        {
            var row = materialized[i];
            parsed.Add(new IoTableRow
            {
                InputAddress = GetColumn(row, 0),
                InputComment = GetColumn(row, 1),
                OutputAddress = GetColumn(row, 2),
                OutputComment = GetColumn(row, 3)
            });
        }

        return parsed
            .Where(r =>
                !string.IsNullOrWhiteSpace(r.InputAddress) ||
                !string.IsNullOrWhiteSpace(r.InputComment) ||
                !string.IsNullOrWhiteSpace(r.OutputAddress) ||
                !string.IsNullOrWhiteSpace(r.OutputComment))
            .ToList();
    }

    private static List<string> ParseHeaders(IEnumerable<string> lines)
    {
        var first = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (string.IsNullOrWhiteSpace(first))
        {
            return new List<string> { "输入地址", "输入变量", "输出地址", "输出变量" };
        }

        var columns = SplitLine(first);
        return IsHeaderRow(columns)
            ? columns.Take(4).Concat(Enumerable.Repeat(string.Empty, 4)).Take(4).ToList()
            : new List<string> { "输入地址", "输入变量", "输出地址", "输出变量" };
    }

    private static Encoding DetectEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3)
        {
            if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8;
            }
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode;
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode;
            }
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
        }

        var gb18030 = TryGetEncoding(54936);
        if (gb18030 is not null && LooksLikeIoHeader(gb18030.GetString(bytes)))
        {
            return gb18030;
        }

        var gbk = TryGetEncoding(936);
        if (gbk is not null && LooksLikeIoHeader(gbk.GetString(bytes)))
        {
            return gbk;
        }

        if (LooksLikeIoHeader(Encoding.Default.GetString(bytes)))
        {
            return Encoding.Default;
        }

        return Encoding.Default;
    }

    private static Encoding? TryGetEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeIoHeader(string text)
    {
        var firstLine = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return firstLine.Contains("输入")
            || firstLine.Contains("输出")
            || firstLine.Contains("地址")
            || firstLine.Contains("变量");
    }

    private static string GetColumn(IReadOnlyList<string> columns, int index)
    {
        return index < columns.Count ? columns[index].Trim() : string.Empty;
    }

    private static bool IsHeaderRow(IReadOnlyList<string> columns)
    {
        var combined = string.Join("|", columns).ToLowerInvariant();
        return combined.Contains("输入")
            || combined.Contains("输出")
            || combined.Contains("地址")
            || combined.Contains("注释")
            || combined.Contains("comment")
            || combined.Contains("address");
    }

    private static List<string> SplitLine(string line)
    {
        var delimiter = line.Contains('\t') ? '\t' : ',';
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        result.Add(builder.ToString());
        return result;
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\r') && !text.Contains('\n'))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static List<IoSignal> NormalizeSignals(IReadOnlyList<IoSignal> source, string expectedPrefix)
    {
        if (source.Count == 0)
        {
            return new List<IoSignal>();
        }

        var normalized = new List<IoSignal>();
        for (var i = 0; i < source.Count; i++)
        {
            var current = source[i];
            normalized.Add(current);

            if (!TryParseAddress(current.Address, expectedPrefix, out var currentWord))
            {
                continue;
            }

            if (i < source.Count - 1 && TryParseAddress(source[i + 1].Address, expectedPrefix, out var nextWord))
            {
                var fillWord = currentWord;
                while (fillWord % 2 == 0 && nextWord - fillWord > 1)
                {
                    fillWord++;
                    for (var bit = 0; bit < 8; bit++)
                    {
                        normalized.Add(new IoSignal($"{expectedPrefix}{fillWord}.{bit}", string.Empty));
                    }
                }
            }
            else if (i == source.Count - 1 && currentWord % 2 == 0)
            {
                var fillWord = currentWord + 1;
                for (var bit = 0; bit < 8; bit++)
                {
                    normalized.Add(new IoSignal($"{expectedPrefix}{fillWord}.{bit}", string.Empty));
                }
            }
        }

        return normalized;
    }

    private static bool TryParseAddress(string address, string expectedPrefix, out int word)
    {
        word = -1;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var normalized = address.Trim().TrimStart('%').ToUpperInvariant();
        if (!normalized.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var dotIndex = normalized.IndexOf('.');
        if (dotIndex <= expectedPrefix.Length)
        {
            return false;
        }

        return int.TryParse(normalized.Substring(expectedPrefix.Length, dotIndex - expectedPrefix.Length), out word);
    }

    private sealed record IoSignal(string Address, string Comment);
}
