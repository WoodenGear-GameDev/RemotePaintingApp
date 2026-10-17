using CommunityToolkit.Mvvm.ComponentModel;
using RemotePainting.Models;
using RemotePainting.Networking;
using System;

namespace RemotePainting.ViewModels;

public partial class MainRootViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableObject? _currentViewModel;
    private readonly PaintingAppModel _paintingAppModel = new PaintingAppModel();

    public MainRootViewModel()
    {
        if (_paintingAppModel.HasSavedUserName)
            ShowMainMenu();
        else
            ShowUsername();
    }

    public void ShowUsername()
    {
        CurrentViewModel = new Menus.UsernameVM(this, _paintingAppModel);
    }

    public void ShowMainMenu()
    {
        CurrentViewModel = new Menus.MainMenuVM(this, _paintingAppModel);
    }

    public void ShowServerList()
    {
        CurrentViewModel = new Menus.ServerListVM(this, _paintingAppModel);
    }

    public void CreateNewCanvas(Action startServerAction)
    {
        var canvasVM = new Menus.CanvasVM(this, _paintingAppModel);
        CurrentViewModel = canvasVM;

        startServerAction();
        canvasVM.GetLocalIP();
    }

    public void ConnectToCanvas(ServerInfo serverInfo)
    {
        var canvasVM = new Menus.CanvasVM(this, _paintingAppModel);
        CurrentViewModel = canvasVM;
        canvasVM.ConnectToCanvas(serverInfo);
    }
}