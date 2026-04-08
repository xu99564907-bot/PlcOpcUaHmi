using System.Collections.Generic;

namespace PlcOpcUaHmi.Models;

public class IoTableImportResult
{
    public string SourceFilePath { get; set; } = string.Empty;
    public int EncodingCodePage { get; set; } = 65001;
    public List<string> Headers { get; set; } = new();
    public List<IoTableRow> Rows { get; set; } = new();
}
