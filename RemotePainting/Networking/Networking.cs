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
        event Action<string> OnDataReceived;
        event Action Disconnected;
        event Action<ClientInfo> Connected;

        void Start();
        void Stop();
        Task SendData(string data);
        Task SyncClientBoard(ClientInfo clientInfo, List<(string, string)> dataList);
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

        public Client(string serverIp, int port)
        {
            _serverIp = serverIp;
            _port = port;
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
            await Task.Delay(500, _cts.Token);

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
        public event Action<string>? OnDataReceived;
        public event Action<ClientInfo>? Connected;
        public event Action? Disconnected;
        private readonly int _port;
        private const int MaxClients = 3;
        public bool IsRunning { get; private set; }
        private TcpListener? _tcpListener;
        private List<ClientInfo> _currentClients = new();
        private CancellationTokenSource? _cts;
        private readonly string _hostName = Dns.GetHostName();

        public Server(int port)
        {
            _port = port;
        }

        private string GetServerInfo()
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(_hostName);

                foreach (var ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ip.ToString();
                        if (ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.") || (ipStr.StartsWith("172.") && IsValid172Range(ipStr)))
                        {
                            return $"SERVER:{_hostName}|{ip}";
                        }
                    }
                }
            }
            catch (Exception)
            {

            }

            return $"SERVER:{_hostName}|127.0.0.1";
        }

        private bool IsValid172Range(string ipStr)
        {
            var parts = ipStr.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out int secondOctet))
            {
                return secondOctet >= 16 && secondOctet <= 31;
            }
            return false;
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
                    if (_currentClients.Count >= MaxClients)
                    {
                        client.Close();
                    }
                    else
                    {
                        _ = RegisterClient(client, cancellationToken);
                    }

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
                byte[] messageBytes;
                if (_currentClients.Count >= MaxClients)
                {
                    messageBytes = Encoding.UTF8.GetBytes("FULL");
                    await client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
                    client.Close();
                    return;
                }

                messageBytes = Encoding.UTF8.GetBytes("GET_NAME");
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
                    string name = GetUnicClientName(payload);
                    ClientInfo clientInfo = new ClientInfo(name, client);

                    if (_currentClients.Count >= MaxClients)
                    {
                        client.Close();
                        return;
                    }

                    _currentClients.Add(clientInfo);

                    messageBytes = Encoding.UTF8.GetBytes("WELCOME");
                    await client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);

                    OnDataReceived?.Invoke($"Message:{name}|I'm your new friend!");
                    Connected?.Invoke(clientInfo);

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

        public async Task SyncClientBoard(ClientInfo clientInfo, List<(string, string)> dataList)
        {
            try
            {
                byte[] messageBytes;
                foreach (var (userName, data) in dataList)
                {
                    if (data.StartsWith("DRAW:"))
                    {
                        string name = userName == "Me" ? "(Server)" + _hostName : userName;
                        messageBytes = Encoding.UTF8.GetBytes($"{data}|{name}\n");
                        await clientInfo.Client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length);
                    }
                }
            }
            catch (Exception)
            {
                clientInfo.Client.Close();
                _currentClients.Remove(clientInfo);
            }
        }

        private string GetUnicClientName(string name)
        {
            int suffix = 2;
            string uniqueName = name;
            while (_currentClients.Any(c => c.Name == uniqueName) || uniqueName == _hostName)
            {
                uniqueName = $"{name}_{suffix}";
                suffix++;
            }
            return uniqueName;
        }

        private async Task StartListeningClientSignals(ClientInfo clientInfo, CancellationToken cancellationToken)
        {
            using (clientInfo.Client)
            using (var stream = clientInfo.Client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {

                        string? data = await reader.ReadLineAsync();

                        if (string.IsNullOrEmpty(data))
                        {
                            clientInfo.Client.Close();
                            _currentClients.Remove(clientInfo);
                            break;
                        }

                        if (data.StartsWith("DRAW:"))
                        {
                            OnDataReceived?.Invoke($"{data}|{clientInfo.Name}");
                        }
                        else
                        {
                            OnDataReceived?.Invoke($"Message:{clientInfo.Name}|{data}");
                        }

                        _ = RetransmitData(data, clientInfo.Name);
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    clientInfo.Client.Close();
                    _currentClients.Remove(clientInfo);
                }
            }
        }

        public async Task SendData(string data)
        {
            byte[] messageBytes;
            if (data.StartsWith("DRAW:"))
            {
                messageBytes = Encoding.UTF8.GetBytes($"{data}|(Server){_hostName}\n");
            }
            else
            {
                messageBytes = Encoding.UTF8.GetBytes($"Message:(Server){_hostName}|{data}\n");
            }

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

        private async Task RetransmitData(string data, string sourceName)
        {
            byte[] messageBytes;
            if (data.StartsWith("DRAW:"))
            {
                messageBytes = Encoding.UTF8.GetBytes($"{data}|{sourceName}\n");
            }
            else
            {
                messageBytes = Encoding.UTF8.GetBytes($"Message:{sourceName}|{data}\n");
            }

            var disconnectedClients = new List<ClientInfo>();
            foreach (var clientInfo in _currentClients)
            {
                if (clientInfo.Name != sourceName)
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
            }
            foreach (var clientInfo in disconnectedClients)
            {
                clientInfo.Client.Close();
                _currentClients.Remove(clientInfo);
            }
        }
    }
}