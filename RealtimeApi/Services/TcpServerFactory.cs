using System.Net;

namespace RealtimeApi.Services;

public class TcpServerFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TcpServerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public TcpServerService Create(IPAddress address, int port = 5000, int bufferSize = 4096)
    {
        var logger = _loggerFactory.CreateLogger<TcpServerService>();
        return new TcpServerService(logger, address, port, bufferSize);
    }
}
