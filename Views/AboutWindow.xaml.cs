using System.Windows;
using GothicSaveEditor.Core.Utils;

namespace GothicSaveEditor.Views
{
    public partial class AboutWindow
    {
        public string AppTitle => Options.GseVersion;

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
