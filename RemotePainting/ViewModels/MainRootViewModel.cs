using CommunityToolkit.Mvvm.ComponentModel;
using RemotePainting.Models;
using RemotePainting.Networking;

namespace RemotePainting.ViewModels;

public partial class MainRootViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableObject? _currentViewModel;
    private readonly PaintingAppModel _paintingAppModel = new PaintingAppModel();

    public MainRootViewModel()
    {
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

    public void CreateNewCanvas()
    {
        CurrentViewModel = new Menus.CanvasVM(this, _paintingAppModel);
    }

    public void ConnectToCanvas(ServerInfo serverInfo)
    {
        var canvasVM = new Menus.CanvasVM(this, _paintingAppModel);
        CurrentViewModel = canvasVM;
        canvasVM.ConnectToCanvas(serverInfo);
    }
}