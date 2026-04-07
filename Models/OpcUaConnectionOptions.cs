namespace PlcOpcUaHmi.Models;

public class OpcUaConnectionOptions
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4840;
    public string EndpointPath { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseAnonymous { get; set; } = true;

    public string GetEndpointUrl()
    {
        var suffix = string.IsNullOrWhiteSpace(EndpointPath) ? string.Empty : "/" + EndpointPath.Trim('/');
        return $"opc.tcp://{ServerIp}:{Port}{suffix}";
    }
}
