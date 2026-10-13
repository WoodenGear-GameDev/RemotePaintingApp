using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        public CanvasVM(MainRootViewModel navigation, PaintingAppModel paintingAppModel)
        {
            _navigation = navigation;
            _paintingAppModel = paintingAppModel;
            _paintingAppModel.SetDisconnectedEvent(OnDisconnectedFromServer);
        }

        public void OnDisconnectedFromServer()
        {
            _paintingAppModel.CloseCanvas();
            _navigation.ShowMainMenu();
        }

        [RelayCommand]
        private void CloseCanvas()
        {
            _paintingAppModel.CloseCanvas();
            _navigation.ShowMainMenu();
        }
    }
}