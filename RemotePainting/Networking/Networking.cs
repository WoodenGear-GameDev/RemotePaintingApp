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
    public class NetworkFinder
    {
        public List<ServerInfo> Servers { get; } = new();
        public event Action? OnNewServerFound;

        private ServiceDiscovery? _serviceDiscovery;

        public void StartSearching()
        {
            Servers.Clear();
            _serviceDiscovery = new ServiceDiscovery();

            _serviceDiscovery.ServiceInstanceDiscovered += (s, e) =>
            {
                foreach (var answer in e.Message.Answers.OfType<SRVRecord>())
                {
                    string hostName = answer.Target.ToString().TrimEnd('.');
                    int port = answer.Port;

                    var ipRecord = e.Message.AdditionalRecords
                        .OfType<ARecord>()
                        .FirstOrDefault(a => a.Name == answer.Target);

                    if (ipRecord != null)
                    {
                        string ipAddress = ipRecord.Address.ToString();

                        Dispatcher.UIThread.Post(() =>
                        {
                            bool exists = Servers.Any(srv => srv.IpAddress == ipAddress && srv.Port == port);
                            if (!exists)
                            {
                                Servers.Add(new ServerInfo(hostName, ipAddress, port));
                                OnNewServerFound?.Invoke();
                            }
                        });
                    }
                }
            };

            _serviceDiscovery.QueryServiceInstances("_remotepainting._tcp");
        }

        public void StopSearching()
        {
            _serviceDiscovery?.Dispose();
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
                    string message = $"CLIENT:{_userName}";
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
        public string HostName { get; init; }
        public string IpAddress { get; init; }
        public int Port { get; init; }
        public ServerInfo(string hostName, string ipAddress, int port)
        {
            HostName = hostName;
            IpAddress = ipAddress;
            Port = port;
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
        private readonly string _userName;

        private ServiceDiscovery? _serviceDiscovery;
        private ServiceProfile? _serviceProfile;

        public Server(int port, string userName)
        {
            _port = port;
            _userName = userName;
        }

        public void StartAdvertising(string serverName, int tcpPort)
        {
            _serviceProfile = new ServiceProfile(serverName, "_remotepainting._tcp", (ushort)tcpPort);

            _serviceDiscovery = new ServiceDiscovery();
            _serviceDiscovery.Advertise(_serviceProfile);
        }

        public void StopAdvertising()
        {
            _serviceDiscovery?.Unadvertise(_serviceProfile);
            _serviceDiscovery?.Dispose();
        }

        public void Start()
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _port);
                _tcpListener.Start();

                _cts = new CancellationTokenSource();

                StartAdvertising(_userName, _port);
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
                StopAdvertising();
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
                        string name = userName == "Me" ? _userName : userName;
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
            while (_currentClients.Any(c => c.Name == uniqueName) || uniqueName == _userName)
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
                messageBytes = Encoding.UTF8.GetBytes($"{data}|{_userName}\n");
            }
            else
            {
                messageBytes = Encoding.UTF8.GetBytes($"Message:{_userName}|{data}\n");
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