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

        private bool _isSearching = false;

        public ServerListVM(MainRootViewModel navigation, PaintingAppModel paintingAppModel)
        {
            _navigation = navigation;
            _paintingAppModel = paintingAppModel;

            _cts = new CancellationTokenSource();

            _ = SearchingLoop();
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
            System.Diagnostics.Debug.WriteLine($"{selectedServer.IpAddress}");
            if (selectedServer == null) return;

            _paintingAppModel.ConnectToCanvas(selectedServer.IpAddress);
            _navigation.ShowCanvas();
        }

        private async Task ExecuteSearchAsync(CancellationToken cancellationToken)
        {
            if (_isSearching) return;

            _isSearching = true;
            try
            {
                await _paintingAppModel.RefreshAvailableServers(cancellationToken);
                DiscoveredServers = _paintingAppModel.Servers;
            }
            finally
            {
                _isSearching = false;
            }
        }

        private async Task SearchingLoop()
        {
            while (!_cts!.IsCancellationRequested)
            {
                await ExecuteSearchAsync(_cts.Token);

                try
                {
                    await Task.Delay(2000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}