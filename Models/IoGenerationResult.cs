using System.Collections.Generic;

namespace PlcOpcUaHmi.Models;

public class IoGenerationResult
{
    public string OutputDirectory { get; set; } = string.Empty;
    public List<GeneratedProgramArtifact> Artifacts { get; set; } = new();
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
}
