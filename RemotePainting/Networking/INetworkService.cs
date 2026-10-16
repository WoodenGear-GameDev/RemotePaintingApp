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
}
