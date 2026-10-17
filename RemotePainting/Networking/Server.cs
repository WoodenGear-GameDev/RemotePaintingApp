using Makaretu.Dns;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemotePainting.Networking
{
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
        public event Action<ClientInfo>? OnClientConnected;
        private readonly int _port;
        private const int MaxClients = 3;
        public bool IsRunning { get; private set; }
        private TcpListener? _tcpListener;
        private List<ClientInfo> _currentClients = new();
        private CancellationTokenSource? _cts;
        private readonly string _userName;

        private ServiceDiscovery? _serviceDiscovery;
        private ServiceProfile? _serviceProfile;
        private IPAddress? _localIp;

        public Server(int port, string userName)
        {
            _port = port;
            _userName = userName;
            _localIp = GetLocalIpAddress();
        }

        private static IPAddress GetLocalIpAddress()
        {
            try
            {
                var validInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                  ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
                                 !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Description.Contains("TUN", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var ni in validInterfaces)
                {
                    var ipProps = ni.GetIPProperties();
                    if (ipProps.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                    {
                        var ipv4 = ipProps.UnicastAddresses
                            .FirstOrDefault(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (ipv4 != null)
                            return ipv4.Address;
                    }
                }

                foreach (var ni in validInterfaces)
                {
                    var ipv4 = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (ipv4 != null)
                        return ipv4.Address;
                }
            }
            catch { }

            return IPAddress.Loopback;
        }

        public void StartAdvertising(string serverName, int tcpPort)
        {
            _serviceProfile = new ServiceProfile(serverName, "_remotepainting._tcp", (ushort)tcpPort, new[] { _localIp });
            _serviceDiscovery = new ServiceDiscovery();
            _serviceDiscovery.Advertise(_serviceProfile);
        }

        public void StopAdvertising()
        {
            _serviceDiscovery?.Unadvertise(_serviceProfile);
            _serviceDiscovery?.Dispose();
        }

        public void Start(out string? localIP)
        {
            localIP = _localIp!.ToString();
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

                    try
                    {
                        messageBytes = Encoding.UTF8.GetBytes("WELCOME");
                        await client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);

                        OnDataReceived?.Invoke($"Message:{name}|I'm here now!");
                        _ = RetransmitData("I'm here now!", name);
                        OnClientConnected?.Invoke(clientInfo);

                        _ = StartListeningClientSignals(clientInfo, cancellationToken);
                    }
                    catch (Exception)
                    {
                        HandleClientDisconnection(clientInfo);
                    }
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

        private void HandleClientDisconnection(ClientInfo clientInfo)
        {
            OnDataReceived?.Invoke($"Message:{clientInfo.Name}|I'm leaving now!");
            _ = RetransmitData("I'm leaving now!", clientInfo.Name);

            _currentClients.Remove(clientInfo);
            clientInfo.Client.Close();
        }

        public async Task SyncClientBoard(ClientInfo clientInfo, List<(string, string)> dataList)
        {
            if (dataList.Count == 0) return;

            try
            {
                await Task.Delay(200);

                var sb = new StringBuilder();
                foreach (var (userName, data) in dataList)
                {
                    if (data.StartsWith("DRAW:"))
                    {
                        string name = userName == "Me" ? _userName : userName;
                        sb.Append(data).Append('|').Append(name).Append('\n');
                    }
                }

                if (sb.Length > 0)
                {
                    byte[] messageBytes = Encoding.UTF8.GetBytes(sb.ToString());
                    await clientInfo.Client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length);
                }
            }
            catch (Exception)
            {
                HandleClientDisconnection(clientInfo);
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
                    HandleClientDisconnection(clientInfo);
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
                HandleClientDisconnection(clientInfo);
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
                HandleClientDisconnection(clientInfo);
            }
        }
    }
}
