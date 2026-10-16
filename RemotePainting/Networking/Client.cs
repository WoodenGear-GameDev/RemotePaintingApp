using Avalonia.Threading;
using Makaretu.Dns;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
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
        public event Action<ClientInfo>? Connected;
        public event Action? Disconnected;
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

        public async Task SyncClientBoard(ClientInfo clientInfo, List<(string, string)> data)
        {
            Stop();
        }

        public void Start()
        {
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
                await _tcpClient!.ConnectAsync(_serverIp, _port, _cts.Token);

                _stream = _tcpClient.GetStream();
                _ = Registration(_stream, _cts.Token);

                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (OperationCanceledException)
            {
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
                        _ = StartListeningServer(stream, cancellationToken);
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
                HandleDisconnection();
            }
            catch (Exception)
            {
                HandleDisconnection();
            }
        }

        private void HandleDisconnection()
        {
            _cts?.Cancel();
            _tcpClient?.Close();
            Disconnected?.Invoke();
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

                        string? data = await reader.ReadLineAsync();

                        if (string.IsNullOrEmpty(data))
                        {
                            Disconnected?.Invoke();
                            break;
                        }

                        OnDataReceived?.Invoke(data);
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    Disconnected?.Invoke();
                }
            }
        }

        public async Task SendData(string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes($"{message}\n");

            await _stream!.WriteAsync(messageBytes, 0, messageBytes.Length, _cts!.Token);
        }
    }
}
