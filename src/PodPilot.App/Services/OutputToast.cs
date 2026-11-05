using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PodPilot.App.Services;

/// <summary>
/// Korte melding onderaan in het midden van het scherm (bijv. "Geluid via: Luidsprekers"), in de
/// stijl van PodPilot. Neemt geen focus, verdwijnt na anderhalve seconde vanzelf.
/// </summary>
internal static class OutputToast
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(1.5);
    private static Window? _current;

    /// <param name="colors">Kleurendictionary van het gewenste thema (zie ThemeManager.CreateColorsFor).</param>
    public static void Show(string text, ResourceDictionary colors)
    {
        // Snel achter elkaar wisselen: de vorige melding meteen weg.
        _current?.Close();

        // E7F5 = luidspreker
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("SymbolFont"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        content.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });

        var panel = new Border
        {
            Style = (Style)Application.Current.FindResource("TrayMenuPanel"),
            Padding = new Thickness(18, 12, 18, 12),
            MinWidth = 0,
            Child = content,
        };
        panel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = panel,
        };
        window.Resources.MergedDictionaries.Add(colors);
        window.Closed += (_, _) =>
        {
            if (_current == window)
                _current = null;
        };

        _current = window;
        window.Show();
        PlaceBottomCenter(window);

        var timer = new DispatcherTimer { Interval = VisibleFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) => window.Close();
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        timer.Start();
    }

    /// <summary>Onderaan in het midden van het werkgebied van het scherm waar de muis is (schermpixels).</summary>
    private static void PlaceBottomCenter(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        GetCursorPos(out var cursor);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(cursor, 2 /* dichtstbijzijnde */), ref info))
            return;

        for (var i = 0; i < 2; i++)
        {
            GetWindowRect(handle, out var r);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            var x = info.rcWork.Left + (info.rcWork.Right - info.rcWork.Left - width) / 2;
            var y = info.rcWork.Bottom - height - 48;
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010 /* NOSIZE, NOZORDER, NOACTIVATE */);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
