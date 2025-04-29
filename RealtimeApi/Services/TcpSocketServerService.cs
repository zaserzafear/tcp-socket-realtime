using Microsoft.Extensions.Options;
using RealtimeApi.Configurations;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RealtimeApi.Services;

public class TcpSocketServerService : IHostedService, ITcpServerManager
{
    private readonly ILogger<TcpSocketServerService> _logger;
    private readonly TcpServerFactory _tcpServerFactory;
    private readonly int _entryPort;
    private readonly List<int> _ports;
    private readonly int _bufferSize;
    private readonly int _entrySocketBacklog;
    private readonly int _backlog;
    private readonly List<TcpServerService> _servers = new();

    private readonly IPAddress _listenAddress;
    private readonly Socket _entrySocket;

    public TcpSocketServerService(ILogger<TcpSocketServerService> logger, TcpServerFactory serverFactory, IOptions<TcpServerSetting> tcpServerSetting)
    {
        _logger = logger;
        _tcpServerFactory = serverFactory;

        var tcpServerConfig = tcpServerSetting.Value;
        var host = tcpServerConfig.Host;
        _entryPort = tcpServerConfig.EntryPort;
        _ports = tcpServerConfig.ServerPorts;
        _bufferSize = tcpServerConfig.BufferSize;
        _backlog = tcpServerConfig.BackLog;
        _entrySocketBacklog = _backlog * _ports.Count;
        _listenAddress = host == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(host);

        _entrySocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        _entrySocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _entrySocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _entrySocket.Bind(new IPEndPoint(_listenAddress, _entryPort));

        _logger.LogInformation("Initializing TCP Socket Server Service...");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Start backend servers
        foreach (var port in _ports)
        {
            var tcpServer = _tcpServerFactory.Create(address: _listenAddress, port: port, bufferSize: _bufferSize);
            _servers.Add(tcpServer);
            _ = tcpServer.StartAsync(_backlog, cancellationToken);
        }

        // Start entry socket
        _entrySocket.Listen(backlog: _entrySocketBacklog);
        _logger.LogInformation("Started entry socket listener on port {Port} with backlog {Backlog}", _entryPort, _entrySocketBacklog);

        _ = AcceptClientsAsync(cancellationToken);

        await Task.CompletedTask;
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _entrySocket.AcceptAsync(cancellationToken);

                int totalConnections = _servers.Sum(server => server.CurrentConnectionCount);
                if (totalConnections >= _entrySocketBacklog)
                {
                    _logger.LogWarning("Connection limit reached. Rejecting new client connection.");
                    try
                    {
                        // Send a message to the client explaining why the connection is being rejected
                        var rejectMessage = Encoding.UTF8.GetBytes("Server is at full capacity. Please try again later.");
                        await clientSocket.SendAsync(rejectMessage, SocketFlags.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending rejection message to client.");
                    }

                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Dispose();
                    clientSocket.Close();
                    continue; // Skip accepting new client
                }

                _logger.LogInformation("Accepted new incoming client connection.");
                var selectedServer = GetLeastConnectionServer();

                if (selectedServer != null)
                {
                    _logger.LogInformation("Forwarding client to server with port {Port} (current {Connections} connections)",
                        selectedServer.Port, selectedServer.CurrentConnectionCount);

                    _ = selectedServer.HandleClientAsync(clientSocket);
                }
                else
                {
                    _logger.LogWarning("No available TCP servers to handle incoming client.");
                    clientSocket.Close();
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client.");
            }
        }
    }

    private TcpServerService? GetLeastConnectionServer()
    {
        var servers = _servers
            .OrderBy(s => s.CurrentConnectionCount);
        return servers.FirstOrDefault();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping TCP Socket Server Service...");

        try
        {
            _entrySocket.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing entry socket.");
        }

        foreach (var server in _servers)
        {
            server.Stop();
        }

        await Task.CompletedTask;
    }

    public async Task BroadcastMessageAsync(byte[] data)
    {
        var tasks = _servers.Select(server => server.BroadcastMessageAsync(data));
        await Task.WhenAll(tasks);
    }

    public async Task BroadcastMessageAsync(string message)
    {
        var data = Encoding.UTF8.GetBytes(message);

        var tasks = _servers.Select(server => server.BroadcastMessageAsync(data));
        await Task.WhenAll(tasks);
    }
}
