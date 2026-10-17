using Avalonia.Threading;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemotePainting.Networking
{
    public class ClientInfo
    {
        public string Name { get; private set; }
        public TcpClient Client { get; private set; }
        public ClientInfo(string name, TcpClient client)
        {
            Name = name;
            Client = client;
        }
    }

    internal class Client : INetworkService
    {
        public event Action<string>? OnDataReceived;
        public event Action? OnDisconnectedFromServer;
        public event Action? OnConnectedToServer;
        private int _isDisconnected = 0;
        private readonly string _serverIp = string.Empty;
        private readonly int _port;
        private TcpClient? _tcpClient;
        private Stream? _stream;
        private CancellationTokenSource? _cts;
        private readonly string _userName;


        public Client(string serverIp, int port, string userName)
        {
            _serverIp = serverIp;
            _port = port;
            _userName = userName;

        }

        public void Start(out string? localIP)
        {
            localIP = null;

            _tcpClient = new TcpClient();
            _ = ConnectToServer();
        }

        public void Stop()
        {
            _cts?.Cancel();
            _tcpClient?.Close();
        }

        private async Task ConnectToServer()
        {
            _cts = new CancellationTokenSource();

            try
            {
                using var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, connectTimeoutCts.Token);

                await _tcpClient!.ConnectAsync(_serverIp, _port, linkedCts.Token);

                _ = Registration(_tcpClient.GetStream(), _cts.Token);

                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!_cts.IsCancellationRequested)
                    HandleDisconnection();
            }
            catch (Exception)
            {
                HandleDisconnection();
            }
        }

        private async Task Registration(Stream stream, CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[256];

                using var timeoutCts1 = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                using var linkedCts1 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts1.Token);

                int byteAmount = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts1.Token);
                if (byteAmount == 0)
                {
                    HandleDisconnection();
                    return;
                }

                string request = Encoding.UTF8.GetString(buffer, 0, byteAmount);

                if (request == "GET_NAME")
                {
                    string message = $"CLIENT:{_userName}";
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);

                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);

                    using var timeoutCts2 = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    using var linkedCts2 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts2.Token);

                    byteAmount = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts2.Token);
                    if (byteAmount == 0)
                    {
                        HandleDisconnection();
                        return;
                    }

                    string response = Encoding.UTF8.GetString(buffer, 0, byteAmount);

                    if (response == "WELCOME")
                    { 
                        _stream = stream;
                        _ = StartListeningServer(stream, cancellationToken);
                        OnConnectedToServer?.Invoke();
                    }
                    else
                    {
                        HandleDisconnection();
                    }
                }
                else
                {
                    HandleDisconnection();
                }
            }
            catch (OperationCanceledException)
            {
                if (!_cts!.IsCancellationRequested)
                    HandleDisconnection();
            }
            catch (Exception)
            {
                HandleDisconnection();
            }
        }

        private void HandleDisconnection()
        {
            if (Interlocked.Exchange(ref _isDisconnected, 1) == 1)
                return;

            Stop();

            Dispatcher.UIThread.Post(() => OnDisconnectedFromServer?.Invoke());
        }

        private async Task StartListeningServer(Stream stream, CancellationToken cancellationToken)
        {
            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {

                        string? data = await reader.ReadLineAsync(cancellationToken);

                        if (string.IsNullOrEmpty(data))
                        {
                            HandleDisconnection();
                            break;
                        }

                        OnDataReceived?.Invoke(data);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!_cts!.IsCancellationRequested)
                        HandleDisconnection();
                }
                catch (Exception)
                {
                    HandleDisconnection();
                }
            }
        }

        public async Task SendData(string message)
        {
            if (_stream == null || _cts == null || _cts.IsCancellationRequested)
                return;

            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes($"{message}\n");
                await _stream.WriteAsync(messageBytes, 0, messageBytes.Length, _cts.Token);
            }
            catch (Exception)
            {
                HandleDisconnection();
            }
        }
    }
}
