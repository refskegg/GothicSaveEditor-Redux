using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GothicSaveEditor.Core.Filtering;
using GothicSaveEditor.ViewModels;

namespace GothicSaveEditor.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly MainViewModel _viewModel;
        private bool _startupFileHandled;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startupFileHandled) return;
            _startupFileHandled = true;

            var args = Environment.GetCommandLineArgs();
            if (args.Length >= 2)
            {
                var path = args[1];
                if (path.EndsWith("SAVEDAT.SAV", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    await _viewModel.LoadSaveGameAsync(path);
                }
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_viewModel.CanCloseSave)
                e.Cancel = true;
        }

        private void VariablesDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;

            var nextDirection = e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            foreach (var col in VariablesDataGrid.Columns)
                col.SortDirection = null;

            e.Column.SortDirection = nextDirection;

            var sortCol = string.Equals(e.Column.SortMemberPath, "Value", StringComparison.Ordinal)
                ? VariableSortColumn.Value
                : VariableSortColumn.Name;

            _viewModel.ApplySort(sortCol, nextDirection);
        }
    }
}
