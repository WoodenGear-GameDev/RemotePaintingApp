using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePainting.Models;
using RemotePainting.Networking;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RemotePainting.ViewModels.Menus
{
    internal partial class ServerListVM : ObservableObject
    {
        private readonly MainRootViewModel _navigation;
        private readonly PaintingAppModel _paintingAppModel;
        private CancellationTokenSource? _cts;

        [ObservableProperty]
        private List<ServerInfo>? _discoveredServers = new();

        public ServerListVM(MainRootViewModel navigation, PaintingAppModel paintingAppModel)
        {
            _navigation = navigation;
            _paintingAppModel = paintingAppModel;

            _cts = new CancellationTokenSource();

            _paintingAppModel.StartFindingServers(OnNewServerFound);
        }

        public void OnNewServerFound()
        {
            DiscoveredServers = _paintingAppModel.GetDiscoveredServers();
        }

        [RelayCommand]
        private void BackToMainMenu()
        {
            _cts?.Cancel();
            _navigation.ShowMainMenu();
        }

        [RelayCommand]
        private async Task ConnectToServer(ServerInfo selectedServer)
        {
            if (selectedServer == null) return;

            _navigation.ConnectToCanvas(selectedServer);
        }
    }
}