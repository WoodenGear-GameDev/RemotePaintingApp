using RemotePainting.Networking;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RemotePainting.Models
{
    internal class PaintingAppModel
    {
        public event Action<string>? OnErrorOccurred;
        private INetworkService? _networkService;
        public List<ServerInfo> Servers { get; private set; } = new();
        private int _port = 7777;

        public void CreateNewCanvas()
        {
            _networkService = new Networking.Server(_port);
            _networkService.Start();
        }

        public void ConnectToCanvas(string ipAddress)
        {
            _networkService = new Networking.Client(ipAddress, _port);
            _networkService.Start();
        }

        public void CloseCanvas()
        {
            _networkService?.Stop();
            _networkService = null;
        }

        public async Task RefreshAvailableServers(CancellationToken cancellationToken)
        {
            Servers = await Networking.Network.DiscoverAllServersAsync(_port, TimeSpan.FromSeconds(3), cancellationToken);
        }

        public void SetDisconnectedEvent(Action onDisconnected)
        {
            if (_networkService != null)
            {
                _networkService.Disconnected += onDisconnected;
            }
        }
    }
}