using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GothicSaveEditor.Core.Primitives;
using GothicSaveEditor.Core.Readers;
using GothicSaveEditor.Core.Services;
using GothicSaveEditor.Core.Utils;
using GothicSaveEditor.Core.Writers;
using GothicSaveEditor.Models;
using NLog;

namespace GothicSaveEditor.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private IReadOnlyList<GothicVariable> _displayedVariables = Array.Empty<GothicVariable>();
        public IReadOnlyList<GothicVariable> DisplayedVariables
        {
            get => _displayedVariables;
            private set => SetProperty(ref _displayedVariables, value);
        }

        public ObservableCollection<Script> Scripts { get; } = new();

        private SaveGame? _openedSaveGame;
        private int _modifiedCount;
        private int _dynamicInfoVersion;

        private CancellationTokenSource? _operationCts;
        private CancellationTokenSource? _searchCts;

        private bool IsSaveNull => _openedSaveGame == null;
        public bool IsSaveModified => _openedSaveGame != null && _modifiedCount > 0;
        public int ModifiedCount => _modifiedCount;
        public string ModifiedInfoLine => _modifiedCount > 0
            ? string.Format(ResourceServices.GetString("UnsavedChangesFormat"), _modifiedCount)
            : string.Empty;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    NotifyCommandsCanExecuteChanged();
                }
            }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            private set => SetProperty(ref _progressValue, value);
        }

        private string _searchLine = string.Empty;
        public string SearchLine
        {
            get => _searchLine;
            set
            {
                if (SetProperty(ref _searchLine, value ?? string.Empty))
                {
                    _ = DebouncedSearchAsync(_searchLine);
                }
            }
        }

        private string _leftInfoLine = string.Empty;
        public string LeftInfoLine
        {
            get => _leftInfoLine;
            set => SetProperty(ref _leftInfoLine, value);
        }

        private string _rightInfoLine = string.Empty;
        public string RightInfoLine
        {
            get => _rightInfoLine;
            set => SetProperty(ref _rightInfoLine, value);
        }

        public string GseVersion => Options.GseVersion;

        public MainViewModel()
        {
            InitializeCommands();
            _leftInfoLine = ResourceServices.GetString("Ready");
        }

        public bool CanCloseSave =>
            !IsSaveModified ||
            MessageBox.Show(
                ResourceServices.GetString("SavegameWasModified"),
                ResourceServices.GetString("Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

        private void SubscribeVariables(IReadOnlyList<GothicVariable> variables)
        {
            _modifiedCount = 0;
            foreach (var v in variables)
            {
                if (v.Modified) _modifiedCount++;
                v.PropertyChanged += OnVariablePropertyChanged;
            }
            NotifyModifiedChanged();
        }

        private void UnsubscribeVariables(IReadOnlyList<GothicVariable>? variables)
        {
            if (variables != null)
            {
                foreach (var v in variables)
                    v.PropertyChanged -= OnVariablePropertyChanged;
            }
            _modifiedCount = 0;
            NotifyModifiedChanged();
        }

        private void NotifyModifiedChanged()
        {
            OnPropertyChanged(nameof(IsSaveModified));
            OnPropertyChanged(nameof(ModifiedCount));
            OnPropertyChanged(nameof(ModifiedInfoLine));
            SaveCommand.NotifyCanExecuteChanged();
        }

        private void OnVariablePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GothicVariable.Modified) && sender is GothicVariable v)
            {
                _modifiedCount += v.Modified ? 1 : -1;
                if (_modifiedCount < 0) _modifiedCount = 0;
                NotifyModifiedChanged();
            }
        }

        private void ClearWorkSpace()
        {
            try
            {
                _searchCts?.Cancel();
                UnsubscribeVariables(_openedSaveGame?.VariablesList);
                DisplayedVariables = Array.Empty<GothicVariable>();
                Scripts.Clear();
                _openedSaveGame = null;
                LeftInfoLine = ResourceServices.GetString("Ready");
                RightInfoLine = string.Empty;
                _searchLine = string.Empty;
                OnPropertyChanged(nameof(SearchLine));
                NotifyCommandsCanExecuteChanged();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        public void UpdateVarCount()
        {
            if (_openedSaveGame == null)
            {
                RightInfoLine = string.Empty;
                return;
            }

            int currentVarCount = DisplayedVariables.Count;
            int totalVarCount = _openedSaveGame.VariablesList.Count;
            string countStr = currentVarCount == totalVarCount
                ? $"{totalVarCount:N0}"
                : $"{currentVarCount:N0} / {totalVarCount:N0}";
            RightInfoLine = string.Format(ResourceServices.GetString("VariablesCountFormat"), countStr);
        }

        private async void SetDynamicInfo(string resourceKey, bool setToPath = false)
        {
            int version = ++_dynamicInfoVersion;
            string restoreText = setToPath && _openedSaveGame != null
                ? _openedSaveGame.FilePath
                : LeftInfoLine;

            LeftInfoLine = ResourceServices.GetString(resourceKey);
            await Task.Delay(Options.InfoLinePopUpTime);

            if (_dynamicInfoVersion == version)
            {
                LeftInfoLine = restoreText;
            }
        }

        public async Task LoadSaveGameAsync(string saveGamePath)
        {
            _operationCts?.Cancel();
            _operationCts = new CancellationTokenSource();
            var ct = _operationCts.Token;

            IsBusy = true;
            ProgressValue = 0;
            LeftInfoLine = ResourceServices.GetString("LoadingSaveGame");

            var progress = new Progress<double>(p => ProgressValue = p * 100.0);

            try
            {
                var encoding = EncodingHelper.GetEncoding(Options.CodePage);
                var saveGame = await Task.Run(() => BinSafeReader.ReadSaveGame(saveGamePath, encoding, progress, ct), ct);

                UnsubscribeVariables(_openedSaveGame?.VariablesList);
                _openedSaveGame = saveGame;
                SubscribeVariables(saveGame.VariablesList);

                _searchLine = string.Empty;
                OnPropertyChanged(nameof(SearchLine));
                DisplayedVariables = saveGame.VariablesList;

                LeftInfoLine = saveGame.FilePath;
                UpdateVarCount();
                NotifyCommandsCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                ClearWorkSpace();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                SetDynamicInfo("UnableToLoadSavegame");
                MessageBox.Show(ResourceServices.GetString("SaveIsBrokenText") + "\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
            }
        }

        private async Task DebouncedSearchAsync(string query)
        {
            _searchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchCts = cts;
            var ct = cts.Token;

            var saveGame = _openedSaveGame;
            if (saveGame == null)
                return;

            try
            {
                await Task.Delay(250, ct);

                IReadOnlyList<GothicVariable> result;
                if (string.IsNullOrWhiteSpace(query))
                {
                    result = saveGame.VariablesList;
                }
                else
                {
                    string trimmed = query.Trim();
                    result = await Task.Run(() =>
                    {
                        var all = saveGame.VariablesList;
                        var matches = new List<GothicVariable>(Math.Min(all.Count, 1024));
                        for (int i = 0; i < all.Count; i++)
                        {
                            if ((i & 1023) == 0)
                                ct.ThrowIfCancellationRequested();

                            var item = all[i];
                            if (item.FullName.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                                matches.Add(item);
                        }
                        return (IReadOnlyList<GothicVariable>)matches;
                    }, ct);
                }

                ct.ThrowIfCancellationRequested();
                DisplayedVariables = result;
                UpdateVarCount();
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer search keystroke
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                MessageBox.Show(ResourceServices.GetString("UnableToSearch"));
            }
        }

        private void ExportVariablesTask(string path)
        {
            var saveGame = _openedSaveGame;
            if (saveGame == null) return;

            var encoding = EncodingHelper.GetEncoding(Options.CodePage);
            var writeText = new StringBuilder(saveGame.VariablesList.Count * 32);
            foreach (var variable in saveGame.VariablesList)
            {
                writeText.Append(variable.FullName).Append(" = ").AppendLine(variable.Value.ToString());
            }
            File.WriteAllText(path, writeText.ToString(), encoding);
        }
    }
}
