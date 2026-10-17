using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePainting.Models;
using RemotePainting.Networking;
using System;
using System.Collections.Generic;
using System.Net;
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

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
        private string _errorMessage = string.Empty;

        public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

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

        [RelayCommand]
        private void ConnectByIp()
        {
            if (!IPAddress.TryParse(IpAddress.Trim(), out IPAddress? address))
            {
                ErrorMessage = "Enter a valid IP address, for example 192.168.1.10.";
                return;
            }

            ErrorMessage = string.Empty;
            _navigation.ConnectToCanvas(
                new ServerInfo("Manual connection", address.ToString(), _paintingAppModel.Port));
        }
    }
}