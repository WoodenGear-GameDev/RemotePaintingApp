using HarfBuzzSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemotePainting.Networking
{
    static class Network
    {
        public static async Task<List<ServerInfo>> DiscoverAllServersAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;

            byte[] requestBytes = Encoding.UTF8.GetBytes("GET_SERVER_INFOS");
            IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, port);

            await udpClient.SendAsync(requestBytes, requestBytes.Length, broadcastEndpoint);

            var foundServers = new List<IPEndPoint>();
            var foundServerInfos = new List<ServerInfo>();

            using var cts = new CancellationTokenSource(timeout);

            try
            {
                while (!cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    var result = await udpClient.ReceiveAsync(cts.Token);
                    string response = Encoding.UTF8.GetString(result.Buffer);

                    if (response.StartsWith("SERVER:"))
                    {
                        string payload = response.Substring("SERVER:".Length);

                        string[] parts = payload.Split('|');
                        if (parts.Length == 2)
                        {
                            string hostName = parts[0];
                            string ipString = parts[1];

                            if (IPAddress.TryParse(ipString, out IPAddress? ipAddress))
                            {
                                IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
                                if (!foundServers.Any(s => s.Address.Equals(endpoint.Address)))
                                {
                                    foundServers.Add(endpoint);
                                    foundServerInfos.Add(new ServerInfo(hostName, ipAddress));
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            return foundServerInfos;
        }
    }
    public interface INetworkService
    {
        //Task SendMessageAsync(string strokeData);

        event Action<string> StrokeReceived;
        event Action Disconnected;

        void Start();
        void Stop();
    }

    internal class Client : INetworkService
    {
        public event Action<string>? StrokeReceived;
        public event Action? Disconnected;
        private readonly string _serverIp = String.Empty;
        private readonly int _port;
        private TcpClient? _tcpClient;
        private Stream? _stream;
        private CancellationTokenSource? _cts;

        public Client(string serverIp, int port)
        {
            _serverIp = serverIp;
            _port = port;
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

                using var timeoutCts1 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
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
                    string hostName = Dns.GetHostName();
                    string message = $"CLIENT:{hostName}";
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);

                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);

                    using var timeoutCts2 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
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
                // Сработал таймаут или глобальная отмена
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
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    byte[] buffer = new byte[256];
                    int byteAmount = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (byteAmount == 0)
                    {
                        Disconnected?.Invoke();
                        break;
                    }
                }
            }
            catch (Exception)
            {
                Disconnected?.Invoke();
            }
        }

        private async Task SendMessageAsync(string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            await _stream!.WriteAsync(messageBytes, 0, messageBytes.Length, _cts!.Token);
        }
    }

    public class ServerInfo
    {
        public string HostName { get; private set; }
        public string IpAddress { get; private set; }
        public ServerInfo(string hostName, IPAddress ipAddress)
        {
            HostName = hostName;
            IpAddress = ipAddress.ToString();
        }
    }

    internal class Server : INetworkService
    {
        public event Action<string>? StrokeReceived;
        public event Action? Disconnected;
        private readonly int _port;
        public bool IsRunning { get; private set; }
        private TcpListener? _tcpListener;
        private List<ClientInfo> _currentClients = new();
        private CancellationTokenSource? _cts;

        public Server(int port)
        {
            _port = port;
        }

        private class ClientInfo
        {
            public string Name { get; private set; }
            public TcpClient Client { get; private set; }
            public ClientInfo(string name, TcpClient client)
            {
                Name = name;
                Client = client;
            }
        }

        private string GetServerInfo()
        {
            string hostName = Dns.GetHostName();
            try
            {
                var hostEntry = Dns.GetHostEntry(hostName);

                foreach (var ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return $"SERVER:{hostName}|{ip}";
                    }
                }
            }
            catch (Exception)
            {

            }

            return $"SERVER:{hostName}|127.0.0.1";
        }

        public void Start()
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _port);
                _tcpListener.Start();

                _cts = new CancellationTokenSource();

                _ = OpenForSearch(_port, _cts.Token);
                _ = StartListeningForClients(_port, _cts.Token);

                IsRunning = true;
            }
            catch (SocketException)
            {
                IsRunning = false;
                _tcpListener?.Stop();
            }
            catch (Exception)
            {
                IsRunning = false;
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _tcpListener?.Stop();
                foreach (var client in _currentClients)
                {
                    client.Client.Close();
                }
                _currentClients.Clear();
            }
            catch (Exception)
            {
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task OpenForSearch(int port, CancellationToken cancellationToken)
        {
            using var udpServer = new UdpClient(port);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await udpServer.ReceiveAsync(cancellationToken);
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message == "GET_SERVER_INFOS")
                    {
                        byte[] responseBytes = Encoding.UTF8.GetBytes(GetServerInfo());

                        await udpServer.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private async Task StartListeningForClients(int port, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _tcpListener!.AcceptTcpClientAsync(cancellationToken);
                    _ = RegisterClient(client, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        private async Task RegisterClient(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes("GET_NAME");
                await client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                byte[] buffer = new byte[256];
                int byteAmount = await client.GetStream().ReadAsync(buffer, 0, buffer.Length, linkedCts.Token);

                if (byteAmount == 0)
                {
                    client.Close();
                    return;
                }

                string response = Encoding.UTF8.GetString(buffer, 0, byteAmount);

                if (response.StartsWith("CLIENT:"))
                {
                    string payload = response.Substring("CLIENT:".Length);
                    ClientInfo clientInfo = new ClientInfo(payload, client);
                    _currentClients.Add(clientInfo);

                    messageBytes = Encoding.UTF8.GetBytes("WELCOME");
                    await client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);

                    System.Diagnostics.Debug.WriteLine($"Подключился: {payload}");
                    _ = StartListeningClientSignals(clientInfo, cancellationToken);
                }
                else
                {
                    client.Close();
                }
            }
            catch (Exception)
            {
                client.Close();
            }
        }

        private async Task StartListeningClientSignals(ClientInfo clientInfo, CancellationToken cancellationToken)
        {
            using (clientInfo.Client)
            using (var stream = clientInfo.Client.GetStream())
            {
                byte[] buffer = new byte[1024];

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int byteAmount = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                        if (byteAmount == 0)
                        {
                            break;
                        }

                        string message = Encoding.UTF8.GetString(buffer, 0, byteAmount);
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    _currentClients.Remove(clientInfo);
                }
            }
        }

        private async Task SendMessageAsync(string strokeData)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(strokeData);
            var disconnectedClients = new List<ClientInfo>();

            foreach (var clientInfo in _currentClients)
            {
                try
                {
                    await clientInfo.Client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length);
                }
                catch (Exception)
                {
                    disconnectedClients.Add(clientInfo);
                }
            }

            foreach (var clientInfo in disconnectedClients)
            {
                clientInfo.Client.Close();
                _currentClients.Remove(clientInfo);
            }
        }
    }
}
