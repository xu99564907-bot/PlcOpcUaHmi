namespace PlcOpcUaHmi.Models;

public class EventBinding
{
    public string TagName { get; set; } = string.Empty;
    public string TriggerCondition { get; set; } = "ValueChanged";
    public string EventName { get; set; } = string.Empty;
    public string ActionTarget { get; set; } = string.Empty;
    public string ActionParameter { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
