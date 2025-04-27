namespace RealtimeApi.Services;

public interface ITcpServerManager
{
    Task BroadcastMessageAsync(string message);
}
