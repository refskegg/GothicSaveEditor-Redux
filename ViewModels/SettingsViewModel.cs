using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GothicSaveEditor.Core.Services;
using GothicSaveEditor.Core.Utils;
using NLog;

namespace GothicSaveEditor.ViewModels
{
    public sealed record EncodingOption(int CodePage, string DisplayName);

    public class SettingsViewModel : ObservableObject
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public string PathLine
        {
            get => Options.GamePath;
            set
            {
                if (Options.GamePath != value)
                {
                    Options.GamePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentLanguage
        {
            get => Options.Language;
            set
            {
                if (Options.Language != value)
                {
                    Options.Language = value;
                    App.SetLanguage(value);
                    RefreshEncodings();
                    if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                        mainVm.RefreshFilterLabels();
                    OnPropertyChanged();
                }
            }
        }

        public bool KeepBackups
        {
            get => Options.KeepBackups;
            set
            {
                if (Options.KeepBackups != value)
                {
                    Options.KeepBackups = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool DarkMode
        {
            get => Options.DarkMode;
            set
            {
                if (Options.DarkMode != value)
                {
                    Options.DarkMode = value;
                    App.SetTheme(value);
                    OnPropertyChanged();
                }
            }
        }

        public bool AssociateSavFiles
        {
            get => Options.AssociateSavFiles;
            set
            {
                if (Options.AssociateSavFiles != value)
                {
                    Options.AssociateSavFiles = value;
                    if (value)
                        AssociationService.EnsureAssociationsSet();
                    OnPropertyChanged();
                }
            }
        }

        public EncodingOption SelectedEncoding
        {
            get => Encodings.FirstOrDefault(e => e.CodePage == Options.CodePage) ?? Encodings[0];
            set
            {
                if (value != null && Options.CodePage != value.CodePage)
                {
                    Options.CodePage = value.CodePage;
                    OnPropertyChanged();
                }
            }
        }

        public string BackupsInfo
        {
            get
            {
                var backupCount = BackupService.BackupsCount;
                if (backupCount == 0)
                    return "";
                var backupsSize = BackupService.BackupsSize;
                return $"  {backupCount} ({backupsSize} MB)  ";
            }
        }

        public ObservableCollection<string> Languages { get; } = new() { "Deutsch", "English", "Русский" };

        public ObservableCollection<EncodingOption> Encodings { get; } = new();

        private void RefreshEncodings()
        {
            int currentCp = Options.CodePage;
            Encodings.Clear();
            Encodings.Add(new EncodingOption(1252, ResourceServices.GetString("Encoding1252")));
            Encodings.Add(new EncodingOption(1250, ResourceServices.GetString("Encoding1250")));
            Encodings.Add(new EncodingOption(1251, ResourceServices.GetString("Encoding1251")));
            OnPropertyChanged(nameof(SelectedEncoding));
            _ = currentCp;
        }

        private readonly SettingsWindow _settingsWindow;

        public IRelayCommand SelectFolderCommand { get; }
        public IRelayCommand CloseWindowCommand { get; }
        public IRelayCommand OpenBackupsFolderCommand { get; }
        public IRelayCommand DeleteBackupsCommand { get; }

        public SettingsViewModel(SettingsWindow settingsWindow)
        {
            _settingsWindow = settingsWindow;
            RefreshEncodings();

            SelectFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    var path = FileService.PickGothicFolder();
                    if (path != null)
                        PathLine = path;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                    MessageBox.Show(ResourceServices.GetString("UnableToSelectGameFolder"));
                }
            });

            CloseWindowCommand = new RelayCommand(() => _settingsWindow.Close());

            OpenBackupsFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    BackupService.OpenBackupsFolder();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            });

            DeleteBackupsCommand = new RelayCommand(() =>
            {
                if (MessageBox.Show(
                        ResourceServices.GetString("AgreeBackupsDeleting"),
                        ResourceServices.GetString("Warning"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    if (!BackupService.DeleteAll())
                    {
                        MessageBox.Show(ResourceServices.GetString("CouldNotDeleteBackups"));
                    }
                    else
                    {
                        OnPropertyChanged(nameof(BackupsInfo));
                        DeleteBackupsCommand?.NotifyCanExecuteChanged();
                    }
                }
            }, () => BackupService.BackupsCount > 0);
        }
    }
}
