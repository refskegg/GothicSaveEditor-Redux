using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
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
    }
}
