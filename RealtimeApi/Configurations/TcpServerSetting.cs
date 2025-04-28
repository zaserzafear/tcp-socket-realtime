namespace RealtimeApi.Configurations;

public class TcpServerSetting
{
    public string Host { get; set; } = "0.0.0.0";
    public int EntryPort { get; set; } = 5000;
    public List<int> ServerPorts { get; set; } = new();
    public int BufferSize { get; set; }
    public int BackLog { get; set; }
}
