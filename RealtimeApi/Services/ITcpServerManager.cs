using System.Text;

namespace RealtimeApi.Services;

public interface ITcpServerManager
{
    Task BroadcastMessageAsync(byte[] data);
    Task BroadcastMessageAsync(string message, Encoding encoding = default!);
}
