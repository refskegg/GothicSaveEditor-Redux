using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using GothicSaveEditor.Core.Services;
using GothicSaveEditor.Core.Utils;
using GothicSaveEditor.Core.Writers;

namespace GothicSaveEditor.ViewModels
{
    public partial class MainViewModel
    {
        public IAsyncRelayCommand OpenCommand { get; private set; } = null!;
        public IAsyncRelayCommand SaveCommand { get; private set; } = null!;
        public IAsyncRelayCommand SaveAsCommand { get; private set; } = null!;
        public IRelayCommand CloseCommand { get; private set; } = null!;
        public IRelayCommand SettingsCommand { get; private set; } = null!;
        public IAsyncRelayCommand MakeBackupCommand { get; private set; } = null!;
        public IRelayCommand AboutCommand { get; private set; } = null!;
        public IAsyncRelayCommand ExportVariablesCommand { get; private set; } = null!;
        public IRelayCommand CancelCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            OpenCommand = new AsyncRelayCommand(ExecuteOpenAsync, () => !IsBusy);
            SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, () => IsSaveModified && !IsBusy);
            SaveAsCommand = new AsyncRelayCommand(ExecuteSaveAsAsync, () => !IsSaveNull && !IsBusy);
            CloseCommand = new RelayCommand(ExecuteClose, () => !IsSaveNull && !IsBusy);
            SettingsCommand = new RelayCommand(ExecuteSettings);
            MakeBackupCommand = new AsyncRelayCommand(ExecuteMakeBackupAsync, () => !IsSaveNull && !IsBusy);
            AboutCommand = new RelayCommand(ExecuteAbout);
            ExportVariablesCommand = new AsyncRelayCommand(ExecuteExportVariablesAsync, () => !IsSaveNull && !IsBusy);
            CancelCommand = new RelayCommand(() => _operationCts?.Cancel(), () => IsBusy);
        }

        private void NotifyCommandsCanExecuteChanged()
        {
            OpenCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
            CloseCommand.NotifyCanExecuteChanged();
            MakeBackupCommand.NotifyCanExecuteChanged();
            ExportVariablesCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        private async Task ExecuteOpenAsync()
        {
            if (!IsSaveNull && !CanCloseSave)
                return;

            string? saveGamePath;
            try
            {
                saveGamePath = FileService.ImportSave();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                MessageBox.Show(ResourceServices.GetString("UnableToLoadSavegameLoadError"));
                return;
            }

            if (string.IsNullOrWhiteSpace(saveGamePath))
                return;

            if (!File.Exists(saveGamePath))
            {
                MessageBox.Show(ResourceServices.GetString("UnableToLoadSavegameWrongPath"));
                return;
            }

            ClearWorkSpace();
            await LoadSaveGameAsync(saveGamePath);
        }

        private async Task ExecuteSaveAsync()
        {
            var saveGame = _openedSaveGame;
            if (saveGame == null) return;

            IsBusy = true;
            ProgressValue = 0;
            LeftInfoLine = ResourceServices.GetString("SavingSaveGame");
            var progress = new Progress<double>(p => ProgressValue = p * 100.0);

            try
            {
                // Mandatory backup check before saving
                if (Options.KeepBackups)
                {
                    try
                    {
                        await Task.Run(() => BackupService.MakeBackup(saveGame.FilePath, Options.MaxBackups));
                    }
                    catch (Exception backupEx)
                    {
                        Logger.Error(backupEx, "Mandatory backup failed; aborting save.");
                        MessageBox.Show(
                            ResourceServices.GetString("UnableToBackup") + "\n" + backupEx.Message,
                            ResourceServices.GetString("Warning"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        LeftInfoLine = saveGame.FilePath;
                        return;
                    }
                }

                await Task.Run(() => SaveDatWriter.Save(saveGame, progress: progress, resetModifiedFlags: false));
                foreach (var variable in saveGame.VariablesList)
                {
                    if (variable.Modified)
                        variable.SetUnModified();
                }
                _modifiedCount = 0;
                NotifyModifiedChanged();
                SetDynamicInfo("SavedSucessfully", setToPath: true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                LeftInfoLine = saveGame.FilePath;
                MessageBox.Show(ResourceServices.GetString("UnableToSaveSavegame") + "\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
            }
        }

        private async Task ExecuteSaveAsAsync()
        {
            var saveGame = _openedSaveGame;
            if (saveGame == null) return;

            string? path = FileService.ExportSave();
            if (string.IsNullOrWhiteSpace(path))
                return;

            IsBusy = true;
            ProgressValue = 0;
            LeftInfoLine = ResourceServices.GetString("SavingSaveGame");
            var progress = new Progress<double>(p => ProgressValue = p * 100.0);

            try
            {
                await Task.Run(() => SaveDatWriter.Save(saveGame, destinationPath: path, progress: progress));
                SetDynamicInfo("SavedSucessfully", setToPath: true);
            }
            catch (IOException ex) when (ex.HResult == -2147024864)
            {
                Logger.Error(ex);
                LeftInfoLine = saveGame.FilePath;
                MessageBox.Show(ResourceServices.GetString("UnableToSaveSavegameProcess"));
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                LeftInfoLine = saveGame.FilePath;
                MessageBox.Show(ResourceServices.GetString("UnableToSaveSavegame") + "\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
            }
        }

        private void ExecuteClose()
        {
            if (CanCloseSave)
                ClearWorkSpace();
        }

        private static void ExecuteSettings()
        {
            try
            {
                WindowsService.OpenSettings();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        private async Task ExecuteMakeBackupAsync()
        {
            var saveGame = _openedSaveGame;
            if (saveGame == null) return;

            IsBusy = true;
            try
            {
                await Task.Run(() => BackupService.MakeBackup(saveGame.FilePath, Options.MaxBackups));
                SetDynamicInfo("BackupCreated", setToPath: true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                MessageBox.Show(ResourceServices.GetString("UnableToBackup") + "\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void ExecuteAbout()
        {
            try
            {
                WindowsService.OpenAbout();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        private async Task ExecuteExportVariablesAsync()
        {
            if (_openedSaveGame == null) return;

            try
            {
                string? path = FileService.ExportVariables();
                if (string.IsNullOrWhiteSpace(path))
                    return;

                LeftInfoLine = ResourceServices.GetString("ExportingVariables");
                await Task.Run(() => ExportVariablesTask(path));
                SetDynamicInfo("VariablesExportedSucessfully", setToPath: true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                MessageBox.Show(ResourceServices.GetString("UnableToExportVariables"));
            }
        }
    }
}
