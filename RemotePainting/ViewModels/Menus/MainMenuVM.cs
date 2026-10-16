using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePainting.Models;

namespace RemotePainting.ViewModels.Menus
{
    internal partial class MainMenuVM : ObservableObject
    {
        private readonly MainRootViewModel _navigation;
        private readonly PaintingAppModel _paintingAppModel;

        public MainMenuVM(MainRootViewModel navigation, PaintingAppModel paintingAppModel)
        {
            _navigation = navigation;
            _paintingAppModel = paintingAppModel;
        }

        [RelayCommand]
        private void CreateServer()
        {
            _navigation.CreateNewCanvas();
            _paintingAppModel.CreateNewCanvas();
        }

        [RelayCommand]
        private void JoinServer()
        {
            _navigation.ShowServerList();
        }
    }
}
