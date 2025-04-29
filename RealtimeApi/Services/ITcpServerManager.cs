using System.Text;

namespace RealtimeApi.Services;

public interface ITcpServerManager
{
    void BroadcastMessage(byte[] data);
    void BroadcastMessage(string message, Encoding? encoding = null);
}
