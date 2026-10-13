using RemotePainting.Controls;
using RemotePainting.Networking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace RemotePainting.Models
{
    internal class PaintingAppModel
    {
         private static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        public event Action<string>? OnErrorOccurred;
        public event Action<string>? OnDrawDataReceived;
        private INetworkService? _networkService;
        public List<ServerInfo> Servers { get; private set; } = new();
        private int _port = 50001;
        private IDrawingBoard? _drawingBoard;

        public void AttachBoard(IDrawingBoard board)
        {
            _drawingBoard = board;
            _drawingBoard.CommandProduced += SendDrawingData;
        }

        public void SendDrawingData(DrawCommand dCmd)
        {
            string data;
            switch (dCmd)
            {
                case BeginStroke b:
                    data = string.Create(IC, $"DRAW:BEGIN_STROKE:{b.Author}|{b.Id}|{b.Color}|{b.Thickness}|{b.X}|{b.Y}>");
                    break;
 
                case AddPoint a:
                    data = string.Create(IC, $"DRAW:ADD_POINT:{a.Author}|{a.Id}|{a.X}|{a.Y}>");
                    break;
 
                case RemoveStroke r:
                    data = string.Create(IC, $"DRAW:REMOVE_STROKE:{r.Author}|{r.Id}>");
                    break;
 
                case ClearAll:
                    data = "DRAW:CLEAR_ALL>";
                    break;
 
                default:
                    return;
            }
 
            _networkService?.SendData(data);
        }

        public void ReceiveData(string data)
        {
            
            if (!data.StartsWith("DRAW:"))
                return;

            string[] commands = data.Split('>');

            foreach(var com in commands)
            {
                if(com.StartsWith("DRAW:"))
                {
                    string payload = com.Substring("DRAW:".Length);

                    if (payload == "CLEAR_ALL")
                    {
                        ApplyDoOnBoard(new ClearAll());
                        System.Diagnostics.Debug.WriteLine($"CLEAR_ALL");
                        return;
                    }

                    string[] commandParts = payload.Split(':', 2);
                    if (commandParts.Length < 2)
                        return;

                    string commandType = commandParts[0];
                    string[] args = commandParts[1].Split('|');

                    switch (commandType)
                    {
                        case "BEGIN_STROKE":
                            if (args.Length == 6)
                            {
                                string author = args[0];
                                int id = int.Parse(args[1]);
                                uint color = uint.Parse(args[2]);
                                float thickness = float.Parse(args[3], CultureInfo.InvariantCulture);
                                float x = float.Parse(args[4], CultureInfo.InvariantCulture);
                                float y = float.Parse(args[5], CultureInfo.InvariantCulture);

                                ApplyDoOnBoard(new BeginStroke(author, id, color, thickness, x, y));
                                return;
                            }
                            break;

                        case "ADD_POINT":
                            if (args.Length == 4)
                            {
                                string author = args[0];
                                int id = int.Parse(args[1]);
                                float x = float.Parse(args[2], CultureInfo.InvariantCulture);
                                float y = float.Parse(args[3], CultureInfo.InvariantCulture);

                                ApplyDoOnBoard(new AddPoint(author, id, x, y));
                                return;
                            }
                            break;

                        case "REMOVE_STROKE":
                            if (args.Length == 2)
                            {
                                string author = args[0];
                                int id = int.Parse(args[1]);

                                ApplyDoOnBoard(new RemoveStroke(author, id));
                            }
                            break;
                    }
                }
            }
        }

        public void ApplyDoOnBoard(DrawCommand dCmd)
        {
            _drawingBoard?.Apply(dCmd);
        }

        public void CreateNewCanvas()
        {
            _networkService = new Networking.Server(_port);
            _networkService.OnDataReceived += ReceiveData;
            _networkService.Start();
        }

        public void ConnectToCanvas(string ipAddress)
        {
            _networkService = new Networking.Client(ipAddress, _port);
            _networkService.OnDataReceived += ReceiveData;
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