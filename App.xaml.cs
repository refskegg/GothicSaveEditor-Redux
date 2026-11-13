using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GothicSaveEditor.Core.Utils;
using MaterialDesignThemes.Wpf;
using NLog;

namespace GothicSaveEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected override void OnStartup(StartupEventArgs e)
        {
            EncodingHelper.EnsureRegistered();

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            base.OnStartup(e);

            // Ensure Options static constructor runs and applies saved language & theme
            SetTheme(Options.DarkMode);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Fatal(e.Exception, "Unhandled UI exception");
            string prefix = GothicSaveEditor.Core.Services.ResourceServices.GetString("UnexpectedError");
            if (string.IsNullOrEmpty(prefix) || prefix == "UnexpectedError")
                prefix = "An unexpected error occurred:";

            MessageBox.Show(
                $"{prefix}\n{e.Exception.Message}",
                "Gothic Savegame Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                Logger.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={0})", e.IsTerminating);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        }

        public static void SetTheme(bool isDark)
        {
            try
            {
                var paletteHelper = new PaletteHelper();
                Theme theme = paletteHelper.GetTheme();
                theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
                paletteHelper.SetTheme(theme);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to apply theme");
            }
        }

        public static void SetLanguage(string lang = "")
        {
            var dict = new ResourceDictionary();
            switch (lang)
            {
                case "Deutsch":
                    dict.Source = new Uri("Resources/German.xaml", UriKind.Relative);
                    Options.Language = "Deutsch";
                    break;
                case "Русский":
                    dict.Source = new Uri("Resources/Russian.xaml", UriKind.Relative);
                    Options.Language = "Русский";
                    break;
                default:
                    dict.Source = new Uri("Resources/English.xaml", UriKind.Relative);
                    Options.Language = "English";
                    break;
            }

            var oldDict = Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.StartsWith("Resources", StringComparison.OrdinalIgnoreCase));

            if (oldDict != null)
            {
                var ind = Current.Resources.MergedDictionaries.IndexOf(oldDict);
                Current.Resources.MergedDictionaries.Remove(oldDict);
                Current.Resources.MergedDictionaries.Insert(ind, dict);
            }
            else
            {
                Current.Resources.MergedDictionaries.Add(dict);
            }
        }
    }
}
