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
        Task SendMessageAsync(string strokeData);

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
        private StreamReader? _reader;
        private StreamWriter? _writer;
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

        public async Task ConnectToServer()
        {
            _cts = new CancellationTokenSource();

            await _tcpClient!.ConnectAsync(_serverIp, _port, _cts.Token);

            using var stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            _ = StartListeningServer(_reader, _cts.Token);

            try
            {
                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (TaskCanceledException) { }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _tcpClient?.Close();
        }

        public async Task StartListeningServer(StreamReader reader, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? message = await reader.ReadLineAsync(cancellationToken);
                    if (message == null)
                    {
                        Disconnected?.Invoke();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {

            }
        }

        public async Task SendMessageAsync(string strokeData)
        {
            if (_writer != null)
                await _writer.WriteLineAsync(strokeData);
        }
    }

    public class ServerInfo
    {
        public string HostName { get; set; }
        public string IpAddress { get; set; }
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
        private List<TcpClient> _currentClients = new();
        private CancellationTokenSource? _cts;

        public Server(int port)
        {
            _port = port;
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
                    client.Close();
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

        public async Task OpenForSearch(int port, CancellationToken cancellationToken)
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
            }
        }

        public async Task StartListeningForClients(int port, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _tcpListener!.AcceptTcpClientAsync(cancellationToken);
                    _currentClients.Add(client);

                    _ = StartListeningClientSignals(client, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        public async Task StartListeningClientSignals(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            using (var stream = client.GetStream())
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
                    _currentClients.Remove(client);
                }
            }
        }

        public async Task SendMessageAsync(string strokeData)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(strokeData);
            foreach (var client in _currentClients)
            {
                try
                {
                    if (client.Connected)
                    {
                        await client.GetStream().WriteAsync(messageBytes, 0, messageBytes.Length);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
