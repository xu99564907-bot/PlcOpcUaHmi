using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class IoTableImportService
{
    private static readonly Regex IoAddressPattern = new(@"^[A-Za-z]{1,4}\d+(?:\.\d+)?$", RegexOptions.Compiled);
    private static readonly List<string> StructuredHeaders =
    [
        "输入模块",
        "输入地址",
        "输入工位",
        "输入变量注释",
        "输入备注",
        "输出模块",
        "输出地址",
        "输出工位",
        "输出变量注释",
        "输出备注"
    ];

    public async Task<IoTableImportResult> ImportAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".csv" or ".txt" => await ImportDelimitedAsync(filePath),
            ".xlsx" => await ImportExcelAsync(filePath),
            _ => throw new NotSupportedException("当前版本支持 CSV/TXT/XLSX 格式的 IO 表。")
        };
    }

    public async Task SaveAsync(string filePath, IEnumerable<IoTableRow> rows, IReadOnlyList<string>? headers, int encodingCodePage)
    {
        var rowList = rows.ToList();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (extension == ".xlsx")
        {
            await SaveWorkbookAsync(filePath, rowList, ResolveHeaders(headers, rowList));
            return;
        }

        var encoding = TryGetEncoding(encodingCodePage) ?? Encoding.UTF8;
        var headerColumns = ResolveHeaders(headers, rowList);
        var lines = new List<string>
        {
            string.Join(",", headerColumns.Select(EscapeCsv))
        };

        foreach (var row in rowList)
        {
            var values = headerColumns.Count >= StructuredHeaders.Count
                ? GetStructuredRowValues(row)
                : GetLegacyRowValues(row);
            lines.Add(string.Join(",", values.Select(EscapeCsv)));
        }

        await File.WriteAllTextAsync(filePath, string.Join(Environment.NewLine, lines), encoding);
    }

    private async Task<IoTableImportResult> ImportDelimitedAsync(string filePath)
    {
        var encoding = DetectEncoding(filePath);
        var rawText = await File.ReadAllTextAsync(filePath, encoding);
        var rows = rawText
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(SplitLine)
            .Cast<IReadOnlyList<string>>()
            .ToList();

        var parsed = ParseRows(rows);
        return new IoTableImportResult
        {
            SourceFilePath = filePath,
            EncodingCodePage = encoding.CodePage,
            Headers = parsed.Headers,
            Rows = parsed.Rows
        };
    }

    private async Task<IoTableImportResult> ImportExcelAsync(string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var rows = LoadWorksheetRows(archive, "IO表");
        var parsed = ParseRows(rows);
        return new IoTableImportResult
        {
            SourceFilePath = filePath,
            EncodingCodePage = 65001,
            Headers = parsed.Headers,
            Rows = parsed.Rows
        };
    }

    private static ParsedIoTable ParseRows(IReadOnlyList<IReadOnlyList<string>> sourceRows)
    {
        var materialized = sourceRows
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .Select(row => row.Select(cell => cell?.Trim() ?? string.Empty).ToList())
            .Cast<IReadOnlyList<string>>()
            .ToList();

        if (materialized.Count == 0)
        {
            return new ParsedIoTable(new List<string>(StructuredHeaders), new List<IoTableRow>());
        }

        var headerIndex = materialized.FindIndex(IsHeaderRow);
        if (headerIndex < 0)
        {
            return ParseLegacyRows(materialized, 0, hasHeader: false);
        }

        var headerRow = materialized[headerIndex];
        if (IsStructuredHeaderRow(headerRow))
        {
            return ParseStructuredRows(materialized, headerIndex);
        }

        if (IsModuleSheetHeaderRow(headerRow))
        {
            return ParseModuleSheetRows(materialized, headerIndex);
        }

        return ParseLegacyRows(materialized, headerIndex, hasHeader: true);
    }

    private static ParsedIoTable ParseStructuredRows(IReadOnlyList<IReadOnlyList<string>> rows, int headerIndex)
    {
        var headerRow = rows[headerIndex];
        var map = BuildHeaderIndexMap(headerRow);
        var parsed = new List<IoTableRow>();

        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var item = new IoTableRow
            {
                InputModule = GetColumn(row, map, "输入模块"),
                InputAddress = GetColumn(row, map, "输入地址"),
                InputStation = GetColumn(row, map, "输入工位"),
                InputComment = GetColumn(row, map, "输入变量注释"),
                InputRemark = GetColumn(row, map, "输入备注"),
                OutputModule = GetColumn(row, map, "输出模块"),
                OutputAddress = GetColumn(row, map, "输出地址"),
                OutputStation = GetColumn(row, map, "输出工位"),
                OutputComment = GetColumn(row, map, "输出变量注释"),
                OutputRemark = GetColumn(row, map, "输出备注")
            };

            if (HasStructuredContent(item))
            {
                parsed.Add(item);
            }
        }

        return new ParsedIoTable(new List<string>(StructuredHeaders), NormalizeRows(parsed));
    }

    private static ParsedIoTable ParseModuleSheetRows(IReadOnlyList<IReadOnlyList<string>> rows, int headerIndex)
    {
        var headerRow = rows[headerIndex];
        var inputSectionCol = FindColumnIndex(headerRow, "模块编号");
        var inputAddressCol = FindColumnIndex(headerRow, "输入地址");
        var inputStationCol = FindNextColumnIndex(headerRow, inputAddressCol + 1, "工位");
        var inputCommentCol = FindNextColumnIndex(headerRow, inputStationCol + 1, "变量注释");
        var inputRemarkCol = FindNextColumnIndex(headerRow, inputCommentCol + 1, "备注");

        var outputLabelCol = FindColumnIndex(headerRow, "输出地址") - 1;
        var outputAddressCol = FindColumnIndex(headerRow, "输出地址");
        var outputStationCol = FindNextColumnIndex(headerRow, outputAddressCol + 1, "工位");
        var outputCommentCol = FindNextColumnIndex(headerRow, outputStationCol + 1, "变量注释");
        var outputRemarkCol = FindNextColumnIndex(headerRow, outputCommentCol + 1, "备注");

        var parsed = new List<IoTableRow>();
        var currentInputModule = string.Empty;
        var currentOutputModule = string.Empty;

        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var inputCandidate = GetColumn(row, inputAddressCol);
            var outputCandidate = GetColumn(row, outputAddressCol);

            if (LooksLikeModuleValue(inputCandidate))
            {
                currentInputModule = inputCandidate;
            }

            if (LooksLikeModuleValue(outputCandidate))
            {
                currentOutputModule = outputCandidate;
            }

            var item = new IoTableRow();

            if (LooksLikeIoAddress(inputCandidate))
            {
                item.InputModule = currentInputModule;
                item.InputAddress = inputCandidate;
                item.InputStation = GetColumn(row, inputStationCol);
                item.InputComment = GetColumn(row, inputCommentCol);
                item.InputRemark = GetColumn(row, inputRemarkCol);
            }

            if (LooksLikeIoAddress(outputCandidate))
            {
                item.OutputModule = currentOutputModule;
                item.OutputAddress = outputCandidate;
                item.OutputStation = GetColumn(row, outputStationCol);
                item.OutputComment = GetColumn(row, outputCommentCol);
                item.OutputRemark = GetColumn(row, outputRemarkCol);
            }

            if (!HasStructuredContent(item))
            {
                var inputSectionLabel = GetColumn(row, inputSectionCol);
                var outputSectionLabel = outputLabelCol >= 0 ? GetColumn(row, outputLabelCol) : string.Empty;
                if (LooksLikeModuleValue(inputSectionLabel))
                {
                    currentInputModule = inputCandidate;
                }

                if (LooksLikeModuleValue(outputSectionLabel))
                {
                    currentOutputModule = outputCandidate;
                }

                continue;
            }

            parsed.Add(item);
        }

        return new ParsedIoTable(new List<string>(StructuredHeaders), NormalizeRows(parsed));
    }

    private static ParsedIoTable ParseLegacyRows(IReadOnlyList<IReadOnlyList<string>> rows, int headerIndex, bool hasHeader)
    {
        var parsed = new List<IoTableRow>();
        var startIndex = hasHeader ? headerIndex + 1 : headerIndex;
        for (var i = startIndex; i < rows.Count; i++)
        {
            var row = rows[i];
            var item = new IoTableRow
            {
                InputAddress = GetColumn(row, 0),
                InputComment = GetColumn(row, 1),
                OutputAddress = GetColumn(row, 2),
                OutputComment = GetColumn(row, 3)
            };

            if (HasLegacyContent(item))
            {
                parsed.Add(item);
            }
        }

        var headers = hasHeader
            ? rows[headerIndex].Take(4).ToList()
            : new List<string> { "输入地址", "输入变量", "输出地址", "输出变量" };

        return new ParsedIoTable(headers, NormalizeRows(parsed));
    }

    private static List<IoTableRow> NormalizeRows(IReadOnlyList<IoTableRow> rows)
    {
        if (rows.Count == 0)
        {
            return new List<IoTableRow>();
        }

        var inputs = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.InputAddress))
            .Select(r => new IoSignal(r.InputModule, r.InputAddress, r.InputStation, r.InputComment, r.InputRemark))
            .ToList();
        var outputs = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.OutputAddress))
            .Select(r => new IoSignal(r.OutputModule, r.OutputAddress, r.OutputStation, r.OutputComment, r.OutputRemark))
            .ToList();

        var normalizedInputs = NormalizeSignals(inputs, "IX");
        var normalizedOutputs = NormalizeSignals(outputs, "QX");
        var max = Math.Max(normalizedInputs.Count, normalizedOutputs.Count);
        var normalizedRows = new List<IoTableRow>(max);

        for (var i = 0; i < max; i++)
        {
            var input = i < normalizedInputs.Count ? normalizedInputs[i] : null;
            var output = i < normalizedOutputs.Count ? normalizedOutputs[i] : null;
            normalizedRows.Add(new IoTableRow
            {
                InputModule = input?.Module ?? string.Empty,
                InputAddress = input?.Address ?? string.Empty,
                InputStation = input?.Station ?? string.Empty,
                InputComment = input?.Comment ?? string.Empty,
                InputRemark = input?.Remark ?? string.Empty,
                OutputModule = output?.Module ?? string.Empty,
                OutputAddress = output?.Address ?? string.Empty,
                OutputStation = output?.Station ?? string.Empty,
                OutputComment = output?.Comment ?? string.Empty,
                OutputRemark = output?.Remark ?? string.Empty
            });
        }

        return normalizedRows;
    }

    private static async Task SaveWorkbookAsync(string filePath, IReadOnlyList<IoTableRow> rows, IReadOnlyList<string> headers)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        await using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        WriteXmlEntry(archive, "[Content_Types].xml", BuildContentTypes());
        WriteXmlEntry(archive, "_rels/.rels", BuildRootRelationships());
        WriteXmlEntry(archive, "xl/workbook.xml", BuildWorkbook());
        WriteXmlEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships());
        WriteXmlEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(headers, rows));
    }

    private static IReadOnlyList<IReadOnlyList<string>> LoadWorksheetRows(ZipArchive archive, string preferredSheetName)
    {
        var workbook = LoadXml(archive, "xl/workbook.xml");
        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var sharedStrings = LoadSharedStrings(archive);

        XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sheetElement = workbook
            .Descendants(mainNs + "sheet")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("name"), preferredSheetName, StringComparison.OrdinalIgnoreCase))
            ?? workbook.Descendants(mainNs + "sheet").FirstOrDefault()
            ?? throw new InvalidOperationException("未在 Excel 文件中找到可用工作表。");

        var relationId = (string?)sheetElement.Attribute(relNs + "id")
            ?? throw new InvalidOperationException("Excel 工作表关系丢失。");

        var relation = workbookRels
            .Descendants(pkgRelNs + "Relationship")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("Id"), relationId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("未找到 Excel 工作表目标。");

        var target = ((string?)relation.Attribute("Target") ?? string.Empty).Replace('\\', '/');
        if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            target = $"xl/{target.TrimStart('/')}";
        }

        var sheet = LoadXml(archive, target);
        var rows = new List<IReadOnlyList<string>>();
        var sheetRows = sheet.Descendants(mainNs + "row").ToList();
        var maxColumn = 0;

        foreach (var row in sheetRows)
        {
            foreach (var cell in row.Elements(mainNs + "c"))
            {
                maxColumn = Math.Max(maxColumn, GetColumnIndex((string?)cell.Attribute("r")));
            }
        }

        foreach (var row in sheetRows)
        {
            var values = Enumerable.Repeat(string.Empty, maxColumn + 1).ToArray();
            foreach (var cell in row.Elements(mainNs + "c"))
            {
                var index = GetColumnIndex((string?)cell.Attribute("r"));
                values[index] = GetCellValue(cell, sharedStrings, mainNs);
            }

            rows.Add(values);
        }

        return rows;
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return doc
            .Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToList();
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new FileNotFoundException($"缺少 Excel 结构文件：{path}");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string GetCellValue(XElement cell, IReadOnlyList<string> sharedStrings, XNamespace ns)
    {
        var type = (string?)cell.Attribute("t");
        return type switch
        {
            "s" => ResolveSharedString(cell.Element(ns + "v")?.Value, sharedStrings),
            "inlineStr" => string.Concat(cell.Descendants(ns + "t").Select(text => text.Value)),
            _ => cell.Element(ns + "v")?.Value?.Trim() ?? string.Empty
        };
    }

    private static string ResolveSharedString(string? indexText, IReadOnlyList<string> sharedStrings)
    {
        return int.TryParse(indexText, out var index) && index >= 0 && index < sharedStrings.Count
            ? sharedStrings[index]
            : string.Empty;
    }

    private static XDocument BuildContentTypes()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        return new XDocument(
            new XElement(ns + "Types",
                new XElement(ns + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ns + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ns + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ns + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
    }

    private static XDocument BuildRootRelationships()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XElement(ns + "Relationships",
                new XElement(ns + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument BuildWorkbook()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return new XDocument(
            new XElement(ns + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                new XElement(ns + "sheets",
                    new XElement(ns + "sheet",
                        new XAttribute("name", "IO表"),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(relNs + "id", "rId1")))));
    }

    private static XDocument BuildWorkbookRelationships()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XElement(ns + "Relationships",
                new XElement(ns + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml"))));
    }

    private static XDocument BuildWorksheet(IReadOnlyList<string> headers, IReadOnlyList<IoTableRow> rows)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dataRows = new List<IReadOnlyList<string>> { headers };
        dataRows.AddRange(rows.Select(row => headers.Count >= StructuredHeaders.Count ? GetStructuredRowValues(row) : GetLegacyRowValues(row)));

        return new XDocument(
            new XElement(ns + "worksheet",
                new XElement(ns + "sheetData",
                    dataRows.Select((values, rowIndex) =>
                        new XElement(ns + "row",
                            new XAttribute("r", rowIndex + 1),
                            values.Select((value, columnIndex) =>
                                new XElement(ns + "c",
                                    new XAttribute("r", $"{GetExcelColumnName(columnIndex)}{rowIndex + 1}"),
                                    new XAttribute("t", "inlineStr"),
                                    new XElement(ns + "is",
                                        new XElement(ns + "t", value ?? string.Empty)))))))));
    }

    private static void WriteXmlEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer);
    }

    private static IReadOnlyList<string> ResolveHeaders(IReadOnlyList<string>? headers, IReadOnlyList<IoTableRow> rows)
    {
        if (headers is { Count: >= 10 })
        {
            return headers.Take(10).ToList();
        }

        if (headers is { Count: >= 4 } && !HasStructuredColumns(rows))
        {
            return headers.Take(4).ToList();
        }

        return HasStructuredColumns(rows)
            ? new List<string>(StructuredHeaders)
            : new List<string> { "输入地址", "输入变量", "输出地址", "输出变量" };
    }

    private static bool HasStructuredColumns(IEnumerable<IoTableRow> rows)
    {
        return rows.Any(row =>
            !string.IsNullOrWhiteSpace(row.InputModule) ||
            !string.IsNullOrWhiteSpace(row.InputStation) ||
            !string.IsNullOrWhiteSpace(row.InputRemark) ||
            !string.IsNullOrWhiteSpace(row.OutputModule) ||
            !string.IsNullOrWhiteSpace(row.OutputStation) ||
            !string.IsNullOrWhiteSpace(row.OutputRemark));
    }

    private static IReadOnlyList<string> GetStructuredRowValues(IoTableRow row)
    {
        return
        [
            row.InputModule,
            row.InputAddress,
            row.InputStation,
            row.InputComment,
            row.InputRemark,
            row.OutputModule,
            row.OutputAddress,
            row.OutputStation,
            row.OutputComment,
            row.OutputRemark
        ];
    }

    private static IReadOnlyList<string> GetLegacyRowValues(IoTableRow row)
    {
        return
        [
            row.InputAddress,
            row.InputComment,
            row.OutputAddress,
            row.OutputComment
        ];
    }

    private static Encoding DetectEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
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

        foreach (var codePage in new[] { 54936, 936 })
        {
            var encoding = TryGetEncoding(codePage);
            if (encoding is not null && LooksLikeIoHeader(encoding.GetString(bytes)))
            {
                return encoding;
            }
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
            || firstLine.Contains("变量")
            || firstLine.Contains("模块");
    }

    private static bool IsHeaderRow(IReadOnlyList<string> columns)
    {
        var combined = string.Join("|", columns).ToLowerInvariant();
        return combined.Contains("输入")
            || combined.Contains("输出")
            || combined.Contains("地址")
            || combined.Contains("注释")
            || combined.Contains("comment")
            || combined.Contains("address")
            || combined.Contains("模块");
    }

    private static bool IsStructuredHeaderRow(IReadOnlyList<string> columns)
    {
        return FindColumnIndex(columns, "输入模块") >= 0
            && FindColumnIndex(columns, "输入地址") >= 0
            && FindColumnIndex(columns, "输出模块") >= 0
            && FindColumnIndex(columns, "输出地址") >= 0;
    }

    private static bool IsModuleSheetHeaderRow(IReadOnlyList<string> columns)
    {
        return FindColumnIndex(columns, "模块编号") >= 0
            && FindColumnIndex(columns, "输入地址") >= 0
            && FindColumnIndex(columns, "输出地址") >= 0;
    }

    private static Dictionary<string, int> BuildHeaderIndexMap(IReadOnlyList<string> headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerRow.Count; i++)
        {
            var text = headerRow[i].Trim();
            if (!string.IsNullOrWhiteSpace(text) && !map.ContainsKey(text))
            {
                map[text] = i;
            }
        }

        return map;
    }

    private static int FindColumnIndex(IReadOnlyList<string> row, string header)
    {
        for (var i = 0; i < row.Count; i++)
        {
            if (string.Equals(row[i].Trim(), header, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindNextColumnIndex(IReadOnlyList<string> row, int startIndex, string header)
    {
        for (var i = Math.Max(startIndex, 0); i < row.Count; i++)
        {
            if (string.Equals(row[i].Trim(), header, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetColumn(IReadOnlyList<string> columns, int index)
    {
        return index >= 0 && index < columns.Count ? columns[index].Trim() : string.Empty;
    }

    private static string GetColumn(IReadOnlyList<string> columns, IReadOnlyDictionary<string, int> map, string header)
    {
        return map.TryGetValue(header, out var index) ? GetColumn(columns, index) : string.Empty;
    }

    private static bool HasStructuredContent(IoTableRow row)
    {
        return !string.IsNullOrWhiteSpace(row.InputModule)
            || !string.IsNullOrWhiteSpace(row.InputAddress)
            || !string.IsNullOrWhiteSpace(row.InputStation)
            || !string.IsNullOrWhiteSpace(row.InputComment)
            || !string.IsNullOrWhiteSpace(row.InputRemark)
            || !string.IsNullOrWhiteSpace(row.OutputModule)
            || !string.IsNullOrWhiteSpace(row.OutputAddress)
            || !string.IsNullOrWhiteSpace(row.OutputStation)
            || !string.IsNullOrWhiteSpace(row.OutputComment)
            || !string.IsNullOrWhiteSpace(row.OutputRemark);
    }

    private static bool HasLegacyContent(IoTableRow row)
    {
        return !string.IsNullOrWhiteSpace(row.InputAddress)
            || !string.IsNullOrWhiteSpace(row.InputComment)
            || !string.IsNullOrWhiteSpace(row.OutputAddress)
            || !string.IsNullOrWhiteSpace(row.OutputComment);
    }

    private static bool LooksLikeIoAddress(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && IoAddressPattern.IsMatch(value.Trim().TrimStart('%'));
    }

    private static bool LooksLikeModuleValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (LooksLikeIoAddress(text))
        {
            return false;
        }

        return text.Contains("模块", StringComparison.OrdinalIgnoreCase)
            || text.Contains("PLC本体", StringComparison.OrdinalIgnoreCase)
            || text.Contains("输入", StringComparison.OrdinalIgnoreCase)
            || text.Contains("输出", StringComparison.OrdinalIgnoreCase);
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
                        normalized.Add(new IoSignal(current.Module, $"{expectedPrefix}{fillWord}.{bit}", current.Station, string.Empty, string.Empty));
                    }
                }
            }
            else if (i == source.Count - 1 && currentWord % 2 == 0)
            {
                var fillWord = currentWord + 1;
                for (var bit = 0; bit < 8; bit++)
                {
                    normalized.Add(new IoSignal(current.Module, $"{expectedPrefix}{fillWord}.{bit}", current.Station, string.Empty, string.Empty));
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

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return 0;
        }

        var column = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return Math.Max(0, column - 1);
    }

    private static string GetExcelColumnName(int index)
    {
        var dividend = index + 1;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar(65 + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private sealed record IoSignal(string Module, string Address, string Station, string Comment, string Remark);
    private sealed record ParsedIoTable(List<string> Headers, List<IoTableRow> Rows);
}
