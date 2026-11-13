using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace GothicSaveEditor.Styles
{
    public partial class WindowStyle
    {
        private const int MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        void WindowLoaded(object sender, RoutedEventArgs e)
        {
            ((Window)sender).StateChanged += WindowStateChanged;
        }

        void WindowStateChanged(object? sender, EventArgs e)
        {
            if (sender is not Window w) return;
            var handle = w.GetWindowHandle();
            if (w.Template?.FindName("MainBorder", w) is not Border containerBorder) return;

            if (w.WindowState == WindowState.Maximized)
            {
                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info) && (info.dwFlags & 1) != 0)
                {
                    containerBorder.Padding = new Thickness(
                        SystemParameters.WorkArea.Left + 7,
                        SystemParameters.WorkArea.Top + 7,
                        (SystemParameters.PrimaryScreenWidth - SystemParameters.WorkArea.Right) + 7,
                        (SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Bottom) + 5);
                }
            }
            else
            {
                containerBorder.Padding = new Thickness(7, 7, 7, 5);
            }
        }

        void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            sender.ForWindowFromTemplate(SystemCommands.CloseWindow);
        }

        void MinButtonClick(object sender, RoutedEventArgs e)
        {
            sender.ForWindowFromTemplate(SystemCommands.MinimizeWindow);
        }

        void MaxButtonClick(object sender, RoutedEventArgs e)
        {
            sender.ForWindowFromTemplate(w =>
            {
                if (w.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(w);
                else SystemCommands.MaximizeWindow(w);
            });
        }
    }
}
