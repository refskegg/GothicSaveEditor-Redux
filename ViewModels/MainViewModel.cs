using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using GothicSaveEditor.Core.Filtering;
using GothicSaveEditor.Core.Primitives;
using GothicSaveEditor.Core.Readers;
using GothicSaveEditor.Core.Services;
using GothicSaveEditor.Core.Utils;
using GothicSaveEditor.Models;
using NLog;

namespace GothicSaveEditor.ViewModels
{
    public sealed record FilterOption<T>(T Value, string DisplayName);

    public partial class MainViewModel : ObservableObject
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private ListCollectionView _variablesView = new(Array.Empty<GothicVariable>());
        public ICollectionView VariablesView
        {
            get => _variablesView;
            private set => SetProperty(ref _variablesView, (ListCollectionView)value);
        }

        public ObservableCollection<Script> Scripts { get; } = new();

        public ObservableCollection<FilterOption<VariableCategoryFilter>> CategoryFilterOptions { get; } = new();
        public ObservableCollection<FilterOption<VariableValueFilter>> ValueFilterOptions { get; } = new();

        private VariableCategoryFilter _selectedCategory = VariableCategoryFilter.All;
        public FilterOption<VariableCategoryFilter>? SelectedCategoryFilter
        {
            get => FindOption(CategoryFilterOptions, _selectedCategory);
            set
            {
                if (value != null && _selectedCategory != value.Value)
                {
                    _selectedCategory = value.Value;
                    OnPropertyChanged();
                    ApplyFiltersImmediate();
                }
            }
        }

        private VariableValueFilter _selectedValueFilter = VariableValueFilter.All;
        public FilterOption<VariableValueFilter>? SelectedValueFilter
        {
            get => FindOption(ValueFilterOptions, _selectedValueFilter);
            set
            {
                if (value != null && _selectedValueFilter != value.Value)
                {
                    _selectedValueFilter = value.Value;
                    OnPropertyChanged();
                    ApplyFiltersImmediate();
                }
            }
        }

        private SaveGame? _openedSaveGame;
        private int _modifiedCount;
        private int _dynamicInfoVersion;

        private VariableFilterCriteria _activeCriteria = new(string.Empty, VariableCategoryFilter.All, VariableValueFilter.All);
        private VariableGridComparer _activeComparer = new(VariableSortColumn.Name, descending: false);

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
            RefreshFilterLabels();
            _leftInfoLine = ResourceServices.GetString("Ready");
        }

        public void RefreshFilterLabels()
        {
            CategoryFilterOptions.Clear();
            CategoryFilterOptions.Add(new FilterOption<VariableCategoryFilter>(VariableCategoryFilter.All, ResourceServices.GetString("FilterCategoryAll")));
            CategoryFilterOptions.Add(new FilterOption<VariableCategoryFilter>(VariableCategoryFilter.Quests, ResourceServices.GetString("FilterCategoryQuests")));
            CategoryFilterOptions.Add(new FilterOption<VariableCategoryFilter>(VariableCategoryFilter.Dialogs, ResourceServices.GetString("FilterCategoryDialogs")));
            CategoryFilterOptions.Add(new FilterOption<VariableCategoryFilter>(VariableCategoryFilter.DefeatedEnemies, ResourceServices.GetString("FilterCategoryDefeated")));
            CategoryFilterOptions.Add(new FilterOption<VariableCategoryFilter>(VariableCategoryFilter.ModifiedOnly, ResourceServices.GetString("FilterCategoryModified")));
            OnPropertyChanged(nameof(SelectedCategoryFilter));

            ValueFilterOptions.Clear();
            ValueFilterOptions.Add(new FilterOption<VariableValueFilter>(VariableValueFilter.All, ResourceServices.GetString("FilterValueAll")));
            ValueFilterOptions.Add(new FilterOption<VariableValueFilter>(VariableValueFilter.Zero, ResourceServices.GetString("FilterValueZero")));
            ValueFilterOptions.Add(new FilterOption<VariableValueFilter>(VariableValueFilter.One, ResourceServices.GetString("FilterValueOne")));
            ValueFilterOptions.Add(new FilterOption<VariableValueFilter>(VariableValueFilter.GreaterThanZero, ResourceServices.GetString("FilterValueGreaterThanZero")));
            OnPropertyChanged(nameof(SelectedValueFilter));

            OnPropertyChanged(nameof(ModifiedInfoLine));
            if (_openedSaveGame == null && !IsBusy)
                LeftInfoLine = ResourceServices.GetString("Ready");
            UpdateVarCount();
        }

        private static FilterOption<T>? FindOption<T>(ObservableCollection<FilterOption<T>> list, T value)
            where T : notnull
        {
            foreach (var item in list)
            {
                if (EqualityComparer<T>.Default.Equals(item.Value, value))
                    return item;
            }
            return list.Count > 0 ? list[0] : null;
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
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() => UpdateModifiedCount(v.Modified));
                    return;
                }
                UpdateModifiedCount(v.Modified);
            }
        }

        private void UpdateModifiedCount(bool isModified)
        {
            _modifiedCount += isModified ? 1 : -1;
            if (_modifiedCount < 0) _modifiedCount = 0;
            NotifyModifiedChanged();

            if (_selectedCategory == VariableCategoryFilter.ModifiedOnly)
                ApplyFiltersImmediate();
        }

        private void ClearWorkSpace()
        {
            try
            {
                _searchCts?.Cancel();
                UnsubscribeVariables(_openedSaveGame?.VariablesList);
                VariablesView = new ListCollectionView(Array.Empty<GothicVariable>());
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

            int currentVarCount = _variablesView.Count;
            int totalVarCount = _openedSaveGame.VariablesList.Count;
            string countStr = currentVarCount == totalVarCount
                ? $"{totalVarCount:N0}"
                : $"{currentVarCount:N0} / {totalVarCount:N0}";
            RightInfoLine = string.Format(ResourceServices.GetString("VariablesCountFormat"), countStr);
        }

        public void ApplySort(VariableSortColumn column, ListSortDirection direction)
        {
            _activeComparer = new VariableGridComparer(column, direction == ListSortDirection.Descending);
            if (_openedSaveGame != null)
            {
                _variablesView.CustomSort = _activeComparer;
            }
        }

        private void ApplyFiltersImmediate()
        {
            _searchCts?.Cancel();
            _activeCriteria = new VariableFilterCriteria(_searchLine, _selectedCategory, _selectedValueFilter);

            if (_openedSaveGame == null)
                return;

            _variablesView.Refresh();
            UpdateVarCount();
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
                _activeCriteria = new VariableFilterCriteria(string.Empty, _selectedCategory, _selectedValueFilter);

                var view = new ListCollectionView(saveGame.VariablesList)
                {
                    CustomSort = _activeComparer,
                    Filter = obj => obj is GothicVariable v && VariableFilter.Matches(v, _activeCriteria)
                };
                VariablesView = view;

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

            if (_openedSaveGame == null)
                return;

            try
            {
                await Task.Delay(250, ct);
                ct.ThrowIfCancellationRequested();

                _activeCriteria = new VariableFilterCriteria(query, _selectedCategory, _selectedValueFilter);
                _variablesView.Refresh();
                UpdateVarCount();
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer keystroke
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
