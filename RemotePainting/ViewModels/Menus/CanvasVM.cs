using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePainting.Controls;
using RemotePainting.Models;
using RemotePainting.Networking;
using System.Collections.Generic;

namespace RemotePainting.ViewModels.Menus
{
    internal partial class CanvasVM : ObservableObject
    {
        private readonly MainRootViewModel _navigation;
        private readonly PaintingAppModel _paintingAppModel;

        [ObservableProperty]
        private Color _selectedColor = Colors.Black;

        [ObservableProperty]
        private double _thickness = 3;
        [ObservableProperty]
        private string _userInput = string.Empty;
        [ObservableProperty]
        private string _chatHistory = string.Empty;

        public CanvasVM(MainRootViewModel navigation, PaintingAppModel paintingAppModel)
        {
            _navigation = navigation;
            _paintingAppModel = paintingAppModel;
            _paintingAppModel.OnMessageReceived += ShowNewMessage;
        }

        public void AttachBoard(IDrawingBoard board)
        {
            _paintingAppModel.AttachBoard(board);
        }

        public void ConnectToCanvas(ServerInfo serverInfo)
        {
            _paintingAppModel.ConnectToCanvas(serverInfo, OnDisconnectedFromServer);
        }

        public void OnDisconnectedFromServer()
        {
            _paintingAppModel.CloseCanvas();
            _navigation.ShowServerList();
        }

        [RelayCommand]
        private void CloseCanvas()
        {
            _paintingAppModel.CloseCanvas();
            _navigation.ShowMainMenu();
        }

        [RelayCommand]
        private void SendUserMessage()
        {
            string message = UserInput;
            if (message != null)
            {
                _paintingAppModel.SendUserMessage(message);
                ChatHistory += $"\nMe: {message}";
                UserInput = string.Empty;
            }
        }

        private void ShowNewMessage(string userName, string message)
        {
            ChatHistory += $"\n{userName}: {message}";
        }
    }
}