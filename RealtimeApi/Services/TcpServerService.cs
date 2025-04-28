using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RealtimeApi.Services;

public class TcpServerService
{
    private readonly ILogger<TcpServerService> _logger;
    private readonly string _serverIdentity;
    private readonly int _port;
    private readonly int _bufferSize;
    private readonly ConcurrentDictionary<string, Socket> _clients = new();
    private int _maxConnections = 0;

    private Socket _listenerSocket;

    public int CurrentConnectionCount => _clients.Count;
    public int Port => _port;

    public TcpServerService(ILogger<TcpServerService> logger, IPAddress address, int port, int bufferSize)
    {
        _logger = logger;
        _serverIdentity = $"{address}:{port}";
        _port = port;
        _bufferSize = bufferSize;

        _logger.LogInformation($"[{_serverIdentity}] Initializing TCP Server...");
        _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        _listenerSocket.Bind(new IPEndPoint(address, port));
    }

    public async Task StartAsync(int backlog, CancellationToken stoppingToken)
    {
        _listenerSocket.Listen(backlog);
        _logger.LogInformation($"[{_serverIdentity}] TCP Server started with backlog {backlog}.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _listenerSocket.AcceptAsync(stoppingToken);
                _ = HandleClientAsync(clientSocket);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, $"[{_serverIdentity}] Error accepting client.");
            }
        }
    }

    public async Task HandleClientAsync(Socket clientSocket)
    {
        var endpoint = clientSocket.RemoteEndPoint?.ToString();

        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.LogWarning($"[{_serverIdentity}] Client socket has no endpoint. Closing.");
            clientSocket.Dispose();
            return;
        }

        if (!_clients.TryAdd(endpoint, clientSocket))
        {
            _logger.LogWarning($"[{_serverIdentity}] Failed to add client {endpoint} to clients dictionary.");
            clientSocket.Dispose();
            return;
        }

        UpdateMaxConnections();
        _logger.LogInformation($"[{_serverIdentity}] Client connected: {endpoint}. Current: {_clients.Count}, Max: {_maxConnections}");

        byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
        try
        {
            while (true)
            {
                int bytesReceived;
                try
                {
                    bytesReceived = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    _logger.LogWarning($"[{_serverIdentity}] Client {endpoint} forcibly closed the connection.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[{_serverIdentity}] Unexpected error handling client {endpoint}.");
                    break;
                }

                if (bytesReceived == 0)
                {
                    _logger.LogInformation($"[{_serverIdentity}] Client {endpoint} disconnected (gracefully).");
                    break;
                }

                var request = Encoding.UTF8.GetString(buffer.AsSpan(0, bytesReceived));
                _logger.LogInformation($"[{_serverIdentity}] Received from {endpoint}: {request}");

                var response = Encoding.UTF8.GetBytes("ACK: " + request);
                await clientSocket.SendAsync(response, SocketFlags.None);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            RemoveClient(endpoint);

            try
            {
                if (clientSocket.Connected)
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[{_serverIdentity}] Error shutting down client socket {endpoint}.");
            }
            finally
            {
                clientSocket.Dispose();
            }
        }
    }

    private void RemoveClient(string endpoint)
    {
        if (_clients.TryRemove(endpoint, out var clientSocket))
        {
            try
            {
                if (clientSocket.Connected)
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[{_serverIdentity}] Error shutting down client {endpoint}");
            }
            finally
            {
                clientSocket.Dispose();
            }

            _logger.LogInformation($"[{_serverIdentity}] Client disconnected: {endpoint}. Current: {_clients.Count}, Max: {_maxConnections}");
        }
    }

    private void UpdateMaxConnections()
    {
        _maxConnections = Math.Max(_clients.Count, _maxConnections);
    }

    public async Task BroadcastMessageAsync(byte[] data)
    {
        var disconnectedClients = new List<string>();

        foreach (var kvp in _clients)
        {
            var clientSocket = kvp.Value;

            if (clientSocket.Connected)
            {
                try
                {
                    await clientSocket.SendAsync(data, SocketFlags.None);
                }
                catch (SocketException)
                {
                    disconnectedClients.Add(kvp.Key);
                }
                catch (ObjectDisposedException)
                {
                    disconnectedClients.Add(kvp.Key);
                }
            }
            else
            {
                disconnectedClients.Add(kvp.Key);
            }
        }

        foreach (var client in disconnectedClients)
        {
            RemoveClient(client);
        }

        await Task.CompletedTask;
    }

    public void Stop()
    {
        _logger.LogInformation($"[{_serverIdentity}] Stopping TCP Server...");
        _listenerSocket.Close();

        foreach (var clientSocket in _clients.Values)
        {
            clientSocket.Close();
        }

        _clients.Clear();
    }
}
