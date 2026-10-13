using CommunityToolkit.Mvvm.ComponentModel;
using RemotePainting.Models;

namespace RemotePainting.ViewModels;

public partial class MainRootViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableObject? _currentViewModel;
    private readonly PaintingAppModel _paintingAppModel = new PaintingAppModel();

    public MainRootViewModel()
    {
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        CurrentViewModel = new Menus.MainMenuVM(this, _paintingAppModel);
    }

    public void ShowServerList()
    {
        CurrentViewModel = new Menus.ServerListVM(this, _paintingAppModel);
    }

    public void ShowCanvas()
    {
        CurrentViewModel = new Menus.CanvasVM(this, _paintingAppModel);
    }
}