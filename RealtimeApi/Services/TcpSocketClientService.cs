using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using RealtimeApi.Configurations;

namespace RealtimeApi.Services;

public class TcpSocketClientService : BackgroundService
{
    private readonly ILogger<TcpSocketClientService> _logger;
    private readonly TcpClientSetting _tcpClientSetting;
    private readonly Encoding _encoding;
    private readonly ITcpServerManager tcpServerManager;

    public TcpSocketClientService(ILogger<TcpSocketClientService> logger, IOptions<TcpClientSetting> tcpClientSetting, ITcpServerManager tcpServerManager)
    {
        _logger = logger;
        _tcpClientSetting = tcpClientSetting.Value;
        _encoding = Encoding.GetEncoding("Windows-1252"); // Windows-1252 encoding created ONCE
        this.tcpServerManager = tcpServerManager;
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
                    _logger.LogWarning("Server closed connection.");
                    break;
                }

                var receivedText = _encoding.GetString(buffer, 0, bytesRead);
                _logger.LogInformation(receivedText);
                await tcpServerManager.BroadcastMessageAsync(receivedText);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Connection lost during data receiving.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while receiving data.");
                break;
            }
        }
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
