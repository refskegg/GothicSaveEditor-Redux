using System.Linq;
using System.Windows;
using GothicSaveEditor.Views;

namespace GothicSaveEditor.Core.Services
{
    public static class WindowsService
    {
        public static bool IsWindowOpen<T>(string name = "") where T : Window
        {
            return string.IsNullOrEmpty(name)
                ? Application.Current.Windows.OfType<T>().Any()
                : Application.Current.Windows.OfType<T>().Any(w => w.Name.Equals(name));
        }

        public static void OpenSettings()
        {
            if (IsWindowOpen<SettingsWindow>())
                return;
            var sw = new SettingsWindow
            {
                Owner = Application.Current.MainWindow
            };
            sw.ShowDialog();
        }

        public static void OpenAbout()
        {
            if (IsWindowOpen<AboutWindow>())
                return;
            var aw = new AboutWindow
            {
                Owner = Application.Current.MainWindow
            };
            aw.ShowDialog();
        }
    }
}
