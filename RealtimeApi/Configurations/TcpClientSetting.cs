namespace RealtimeApi.Configurations;

public class TcpClientSetting
{
    public bool WriteLogRecievedMessage { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
    public int BufferSize { get; set; } = 2048;
    public int ReconnectDelayMs { get; set; } = 1000;
}
