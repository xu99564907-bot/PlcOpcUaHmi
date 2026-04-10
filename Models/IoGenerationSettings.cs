namespace PlcOpcUaHmi.Models;

public class IoGenerationSettings
{
    public string PlcType { get; set; } = "汇川中型PLC";
    public string OperationNumber { get; set; } = "OP10";
    public int ControlDbMultiplier { get; set; } = 100;
    public int ControlDbOffset { get; set; } = 0;
    public int DriveDbOffset { get; set; } = 50;
}
