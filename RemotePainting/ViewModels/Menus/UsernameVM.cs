using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePainting.Models;
using System;
using System.IO;

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
            UserName = paintingAppModel.HasSavedUserName ? paintingAppModel.UserName : string.Empty;
            ErrorMessage = paintingAppModel.UserNameLoadError is null
                ? string.Empty
                : $"Could not load your saved name: {paintingAppModel.UserNameLoadError}";
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
        private string _errorMessage = string.Empty;

        public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

        private bool CanConfirm() => !string.IsNullOrWhiteSpace(UserName);

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm()
        {
            try
            {
                _paintingAppModel.SaveUserName(UserName);
                _navigation.ShowMainMenu();
            }
            catch (IOException exception)
            {
                ErrorMessage = $"Could not save your name: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                ErrorMessage = $"Could not save your name: {exception.Message}";
            }
        }
    }
}