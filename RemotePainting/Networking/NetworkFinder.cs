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
        private CancellationTokenSource? _searchCts;

        public void StartSearching()
        {
            Servers.Clear();
            StopSearching();

            _searchCts = new CancellationTokenSource();
            _serviceDiscovery = new ServiceDiscovery();

            _serviceDiscovery.ServiceInstanceDiscovered += (s, e) =>
            {
                if (!e.ServiceInstanceName.ToString().Contains("_remotepainting._tcp", StringComparison.OrdinalIgnoreCase))
                    return;

                var allRecords = e.Message.Answers.Cast<ResourceRecord>()
                    .Concat(e.Message.AdditionalRecords)
                    .Concat(e.Message.AuthorityRecords);

                foreach (var answer in allRecords.OfType<SRVRecord>())
                {
                    if (!answer.Name.ToString().Contains("_remotepainting._tcp", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string hostName = answer.Name.Labels.FirstOrDefault() ?? answer.Target.ToString().TrimEnd('.');

                    int port = answer.Port;

                    var ipRecord = allRecords
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

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_searchCts.Token.IsCancellationRequested)
                    {
                        _serviceDiscovery?.QueryServiceInstances("_remotepainting._tcp");
                        await Task.Delay(3000, _searchCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {

                }
            });
        }

        public void StopSearching()
        {
            _searchCts?.Cancel();
            _serviceDiscovery?.Dispose();
            _serviceDiscovery = null;
        }
    }
}
