using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePainting.Models;

namespace RemotePainting.ViewModels.Menus
{
    internal partial class UsernameVM : ObservableObject
    {
        private readonly MainRootViewModel _navigation;
        private readonly PaintingAppModel _paintingAppModel;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private string _userName = string.Empty;

        public UsernameVM(MainRootViewModel navigation, PaintingAppModel paintingAppModel)
        {
            _navigation = navigation;
            _paintingAppModel = paintingAppModel;
        }

        private bool CanConfirm() => !string.IsNullOrWhiteSpace(UserName);

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm()
        {
            _paintingAppModel.UserName = UserName;
            _navigation.ShowMainMenu();
        }
    }
}