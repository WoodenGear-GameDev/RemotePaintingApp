using RemotePainting.Controls;
using RemotePainting.Networking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace RemotePainting.Models
{
    internal class PaintingAppModel
    {
        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        public event Action<string>? OnDrawDataReceived;
        public event Action<string, string>? OnMessageReceived;
        private readonly NetworkFinder _networkFinder = new();
        private INetworkService? _networkService;
        private int _port = 7777;
        private IDrawingBoard? _drawingBoard;

        public void AttachBoard(IDrawingBoard board)
        {
            _drawingBoard = board;
            _drawingBoard.CommandProduced += SendDrawingData;
        }

        public void StartFindingServers(Action action)
        {
            _networkFinder.OnNewServerFound += action;
            _networkFinder.StartSearching();
        }

        public List<ServerInfo> GetDiscoveredServers()
        {
            return _networkFinder.Servers;
        }

        public void LoadServerBoard(ClientInfo clientInfo)
        {
            var commands = _drawingBoard!.GetAllStrokeCommands();

            List<(string, string)> dataList = new();
            foreach (var dCmd in commands)
            {
                string userName = string.Empty;
                string data = string.Empty;
                switch (dCmd)
                {
                    case BeginStroke b:
                        data = string.Create(IC, $"DRAW:BEGIN_STROKE:{b.Id}|{b.Color}|{b.Thickness}|{b.X}|{b.Y}");
                        userName = b.Author;
                        break;
                    case AddPoint a:
                        data = string.Create(IC, $"DRAW:ADD_POINT:{a.Id}|{a.X}|{a.Y}");
                        userName = a.Author;
                        break;
                }
                if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(data))
                {
                    dataList.Add((userName, data));
                }
            }

            _ = _networkService!.SyncClientBoard(clientInfo, dataList);
        }

        public void SendDrawingData(DrawCommand dCmd)
        {
            string data;
            switch (dCmd)
            {
                case BeginStroke b:
                    data = string.Create(IC, $"DRAW:BEGIN_STROKE:{b.Id}|{b.Color}|{b.Thickness}|{b.X}|{b.Y}");
                    break;

                case AddPoint a:
                    data = string.Create(IC, $"DRAW:ADD_POINT:{a.Id}|{a.X}|{a.Y}");
                    break;

                case RemoveStroke r:
                    data = string.Create(IC, $"DRAW:REMOVE_STROKE:{r.Id}");
                    break;

                case ClearAll:
                    data = "DRAW:CLEAR_ALL:";
                    break;
                case CursorMove c:
                    data = string.Create(IC, $"DRAW:CURSOR:{c.X}|{c.Y}|{UserName}");
                    break;

                case CursorHide:
                    data = $"DRAW:CURSOR_HIDE:{UserName}";
                    break;

                default:
                    return;
            }

            _networkService?.SendData(data);
        }

        public void SendUserMessage(string message)
        {
            _networkService?.SendData(message);
        }

        public void ReceiveMessage(string message)
        {
            if (message.StartsWith("Message:"))
            {
                ShowMessage(message.Substring("Message:".Length));
            }
            else if (message.StartsWith("DRAW:"))
            {
                ReceiveData(message.Substring("DRAW:".Length));
            }
        }

        public void ShowMessage(string message)
        {
            string[] args = message.Split('|', 2);
            string userName = args[0];
            string userMessage = message.Substring($"{userName}|".Length);

            OnMessageReceived?.Invoke(userName, userMessage);
        }

        public void ReceiveData(string data)
        {
            string[] commandParts = data.Split(':', 2);
            if (commandParts.Length < 2)
                return;

            string commandType = commandParts[0];
            string[] args = commandParts[1].Split('|');

            switch (commandType)
            {
                case "BEGIN_STROKE":
                    if (args.Length == 6)
                    {
                        string author = args[5];
                        int id = int.Parse(args[0]);
                        uint color = uint.Parse(args[1]);
                        float thickness = float.Parse(args[2], CultureInfo.InvariantCulture);
                        float x = float.Parse(args[3], CultureInfo.InvariantCulture);
                        float y = float.Parse(args[4], CultureInfo.InvariantCulture);

                        ApplyDoOnBoard(new BeginStroke(author, id, color, thickness, x, y));
                    }
                    break;

                case "ADD_POINT":
                    if (args.Length == 4)
                    {
                        string author = args[3];
                        int id = int.Parse(args[0]);
                        float x = float.Parse(args[1], CultureInfo.InvariantCulture);
                        float y = float.Parse(args[2], CultureInfo.InvariantCulture);

                        ApplyDoOnBoard(new AddPoint(author, id, x, y));
                    }
                    break;

                case "REMOVE_STROKE":
                    if (args.Length == 2)
                    {
                        string author = args[1];
                        int id = int.Parse(args[0]);

                        ApplyDoOnBoard(new RemoveStroke(author, id));
                    }
                    break;
                case "CLEAR_ALL":
                    ApplyDoOnBoard(new ClearAll());
                    break;
                case "CURSOR":
                    if (args.Length >= 3)
                    {
                        float cx = float.Parse(args[0], CultureInfo.InvariantCulture);
                        float cy = float.Parse(args[1], CultureInfo.InvariantCulture);
                        ApplyDoOnBoard(new CursorMove(args[2], cx, cy));
                    }
                    break;

                case "CURSOR_HIDE":
                    if (args.Length >= 1 && args[0].Length > 0)
                        ApplyDoOnBoard(new CursorHide(args[0]));
                    break;

                default:
                    return;
            }
        }

        public void ApplyDoOnBoard(DrawCommand dCmd)
        {
            _drawingBoard?.Apply(dCmd);
        }

        public void CreateNewCanvas()
        {
            _networkService = new Networking.Server(_port, UserName);
            _networkService.OnDataReceived += ReceiveMessage;
            _networkService.Connected += LoadServerBoard;
            _networkService.Start();
        }

        public void ConnectToCanvas(ServerInfo serverInfo)
        {
            _networkFinder.StopSearching();
            _networkService = new Networking.Client(serverInfo.IpAddress, serverInfo.Port, UserName);
            _networkService.OnDataReceived += ReceiveMessage;
            _networkService.Start();
        }

        public void CloseCanvas()
        {
            _networkService?.Stop();
            _networkService = null;
        }

        public void SetDisconnectedEvent(Action onDisconnected)
        {
            if (_networkService != null)
            {
                _networkService.Disconnected += onDisconnected;
            }
        }
        private string _userName = "Anonymous";
        public string UserName
        {
            get => _userName;
            set => _userName = Sanitize(value);
        }

        private static string Sanitize(string? name)
        {
            var clean = new string((name ?? "").Where(c => c != '|' && c != ':' && !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length > 20) clean = clean.Substring(0, 20);
            return clean.Length == 0 ? "Anonymous" : clean;
        }

    }
}