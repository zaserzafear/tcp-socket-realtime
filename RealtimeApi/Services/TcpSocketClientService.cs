using Microsoft.Extensions.Options;
using RealtimeApi.Configurations;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RealtimeApi.Services;

public class TcpSocketClientService : BackgroundService
{
    private readonly ILogger<TcpSocketClientService> _logger;
    private readonly TcpClientSetting _tcpClientSetting;
    private readonly Encoding _encoding;
    private readonly string _broadcastMethod;
    private readonly ITcpServerManager _tcpServerManager;

    public TcpSocketClientService(ILogger<TcpSocketClientService> logger, IOptions<TcpClientSetting> tcpClientSetting, ITcpServerManager tcpServerManager)
    {
        _logger = logger;
        _tcpClientSetting = tcpClientSetting.Value;
        _encoding = Encoding.GetEncoding(_tcpClientSetting.Encoding);
        _broadcastMethod = _tcpClientSetting.BroadcastMethod;
        _tcpServerManager = tcpServerManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ips = await ResolveDnsAsync(_tcpClientSetting.Host);
                if (ips.Length == 0)
                {
                    _logger.LogWarning("No IPs resolved for {Host}. Retrying after delay...", _tcpClientSetting.Host);
                    await Task.Delay(_tcpClientSetting.ReconnectDelayMs, stoppingToken);
                    continue;
                }

                foreach (var ip in ips)
                {
                    using var client = new TcpClient();
                    try
                    {
                        _logger.LogInformation("Connecting to {Ip}:{Port}...", ip, _tcpClientSetting.Port);
                        await client.ConnectAsync(ip, _tcpClientSetting.Port);

                        _logger.LogInformation("Connected to {Ip}:{Port}", ip, _tcpClientSetting.Port);

                        using var networkStream = client.GetStream();
                        await ReceiveDataAsync(networkStream, stoppingToken);
                    }
                    catch (SocketException ex)
                    {
                        _logger.LogError(ex, "Failed to connect to {Ip}:{Port}", ip, _tcpClientSetting.Port);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error while connecting or communicating");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in TCP client service loop");
            }

            _logger.LogInformation("Reconnecting after delay...");
            await Task.Delay(_tcpClientSetting.ReconnectDelayMs, stoppingToken);
        }
    }

    private async Task ReceiveDataAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[_tcpClientSetting.BufferSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                if (bytesRead == 0)
                {
                    _logger.LogWarning("Server closed the connection.");
                    break;
                }

                HandleReceivedData(buffer, bytesRead, cancellationToken);
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "Connection lost during data reception.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while receiving data.");
                break;
            }
        }
    }

    private void HandleReceivedData(byte[] buffer, int bytesRead, CancellationToken cancellationToken)
    {
        var receivedBytes = buffer[..bytesRead];
        var receivedText = _encoding.GetString(receivedBytes);

        if (_tcpClientSetting.WriteLogRecievedMessage)
        {
            _logger.LogInformation("Received: {ReceivedText}", receivedText);
        }

        if (IsBroadcastByteMode())
        {
            _tcpServerManager.BroadcastMessage(receivedBytes);
        }
        else
        {
            _tcpServerManager.BroadcastMessage(receivedText, _encoding);
        }
    }

    private bool IsBroadcastByteMode()
    {
        return string.Equals(_broadcastMethod, "byte", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IPAddress[]> ResolveDnsAsync(string host)
    {
        try
        {
            _logger.LogInformation("Resolving DNS for {Host}", host);
            return await Dns.GetHostAddressesAsync(host);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "DNS resolution failed for {Host}", host);
            return Array.Empty<IPAddress>();
        }
    }
}
