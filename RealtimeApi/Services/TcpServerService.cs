using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RealtimeApi.Services;

public class TcpServerService
{
    private readonly ILogger<TcpServerService> _logger;
    private readonly TcpListener _listener;
    private readonly string _serverIdentity;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private int _maxConnections = 0;

    public int CurrentConnectionCount => _clients.Count;
    public int Port => _port;

    public TcpServerService(ILogger<TcpServerService> logger, IPAddress address, int port)
    {
        _logger = logger;
        _port = port;
        _serverIdentity = $"{address}:{port}";

        LogInformation("Initializing TCP Server...");
        _listener = new TcpListener(address, port);
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        LogInformation("TCP Server started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                var endpoint = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();

                if (_clients.TryAdd(endpoint, client))
                {
                    UpdateMaxConnections();
                    LogInformation("Client connected: {Endpoint}. Current: {CurrentConnections}, Max: {MaxConnections}",
                        endpoint, _clients.Count, _maxConnections);

                    _ = HandleClientAsync(client, endpoint, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Error accepting client.");
            }
        }
    }

    public void HandleExternalClient(TcpClient client, CancellationToken cancellationToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();

        if (_clients.TryAdd(endpoint, client))
        {
            UpdateMaxConnections();
            LogInformation($"External client connected: {endpoint}. Current: {_clients.Count}, Max: {_maxConnections}");

            _ = HandleClientAsync(client, endpoint, cancellationToken);
        }
    }

    public async Task BroadcastMessageAsync(string message)
    {
        var data = Encoding.UTF8.GetBytes(message);

        var tasks = _clients.Values
            .Where(c => c.Connected)
            .Select(async client =>
            {
                try
                {
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                }
                catch (Exception ex)
                {
                    LogError(ex, "Error sending message to client.");
                }
            });

        await Task.WhenAll(tasks);
    }

    private async Task HandleClientAsync(TcpClient client, string endpoint, CancellationToken token)
    {
        using var stream = client.GetStream();
        var buffer = new byte[4096];

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, token);
                if (bytesRead == 0) break;

                var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                LogInformation("Received from {Endpoint}: {Message}", endpoint, request);

                var response = Encoding.UTF8.GetBytes("ACK: " + request);
                await stream.WriteAsync(response, token);
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "Error handling client {Endpoint}", endpoint);
        }
        finally
        {
            _clients.TryRemove(endpoint, out _);
            LogInformation("Client disconnected: {Endpoint}. Current: {CurrentConnections}, Max: {MaxConnections}",
                endpoint, _clients.Count, _maxConnections);

            client.Close();
        }
    }

    private void UpdateMaxConnections()
    {
        _maxConnections = Math.Max(_clients.Count, _maxConnections);
    }

    public void Stop()
    {
        LogInformation("Stopping TCP Server...");
        _listener.Stop();

        foreach (var client in _clients.Values)
        {
            client.Close();
        }

        _clients.Clear();
    }

    private void LogInformation(string message, params object[] args)
    {
        _logger.LogInformation("[{Server}] " + message, new object[] { _serverIdentity }.Concat(args).ToArray());
    }

    private void LogError(Exception exception, string message, params object[] args)
    {
        _logger.LogError(exception, "[{Server}] " + message, new object[] { _serverIdentity }.Concat(args).ToArray());
    }
}
