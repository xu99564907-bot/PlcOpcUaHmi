using System.Collections.Generic;

namespace PlcOpcUaHmi.Models;

public class AppConfig
{
    public OpcUaConnectionOptions Connection { get; set; } = new();
    public List<TagItem> Tags { get; set; } = new();
    public List<EventBinding> EventBindings { get; set; } = new();
    public IoGenerationSettings IoGeneration { get; set; } = new();
    public List<IoTableRow> IoTableRows { get; set; } = new();
}
