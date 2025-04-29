namespace RealtimeApi.Services;

public interface ITcpServerManager
{
    Task BroadcastMessageAsync(byte[] data);
    Task BroadcastMessageAsync(string message);
}
