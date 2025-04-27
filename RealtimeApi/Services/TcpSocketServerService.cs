using Microsoft.Extensions.Options;
using RealtimeApi.Configurations;
using System.Net;
using System.Net.Sockets;

namespace RealtimeApi.Services;

public class TcpSocketServerService : IHostedService, ITcpServerManager
{
    private readonly ILogger<TcpSocketServerService> _logger;
    private readonly TcpServerFactory _tcpServerFactory;
    private readonly int _entryPort;
    private readonly List<int> _ports;
    private readonly List<TcpServerService> _servers = new();

    private readonly IPAddress _listenAddress;
    private readonly TcpListener _entryListener;

    private CancellationTokenSource _cts = new();

    public TcpSocketServerService(ILogger<TcpSocketServerService> logger, TcpServerFactory serverFactory, IOptions<TcpServerSetting> tcpServerSetting)
    {
        _logger = logger;
        _tcpServerFactory = serverFactory;

        var tcpServerConfig = tcpServerSetting.Value;
        var host = tcpServerConfig.Host;
        _entryPort = tcpServerConfig.EntryPort;
        _ports = tcpServerConfig.ServerPorts;
        _listenAddress = host == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(host);
        _entryListener = new TcpListener(_listenAddress, _entryPort);

        _logger.LogInformation("Initializing TCP Socket Server Service...");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Start backend servers
        foreach (var port in _ports)
        {
            var tcpServer = _tcpServerFactory.Create(address: _listenAddress, port: port);
            _servers.Add(tcpServer);
            _ = tcpServer.StartAsync(cancellationToken);
        }

        // Start entry listener
        _entryListener.Start();
        _logger.LogInformation("Started entry listener on port {Port}", _entryPort);

        _ = AcceptClientsAsync(_cts.Token);

        await Task.CompletedTask;
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _entryListener.AcceptTcpClientAsync(cancellationToken);
                _logger.LogInformation("Accepted new incoming client connection.");

                var selectedServer = GetLeastConnectionServer();

                if (selectedServer != null)
                {
                    _logger.LogInformation("Forwarding client to server with port {Port} (current {Connections} connections)",
                        selectedServer.Port, selectedServer.CurrentConnectionCount);

                    selectedServer.HandleExternalClient(client, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("No available TCP servers to handle incoming client.");
                    client.Close();
                }
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
        _entryListener.Stop();
        _cts.Cancel();

        foreach (var server in _servers)
        {
            server.Stop();
        }

        await Task.CompletedTask;
    }

    public async Task BroadcastMessageAsync(string message)
    {
        var tasks = _servers.Select(server => server.BroadcastMessageAsync(message));
        await Task.WhenAll(tasks);
    }
}
