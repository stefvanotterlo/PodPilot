using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace PodPilot.App.Services;

/// <summary>Eén regel in het tray-menu; null in de lijst = scheidingslijn.</summary>
/// <param name="Glyph">Symbool uit Segoe Fluent Icons.</param>
/// <param name="Shortcut">Sneltoets rechts in de regel (alleen ter info), of null.</param>
public sealed record TrayMenuEntry(string Glyph, string Text, string? Shortcut, Action OnClick, bool IsEnabled = true);

/// <summary>
/// Het menu onder de rechtermuisknop van het tray-icoon, in de stijl van PodPilot (afgeronde
/// hoeken, icoontjes, thema van de pop-up) in plaats van het grijze WinForms-menu.
/// </summary>
/// <remarks>
/// Een eigen klein venster in plaats van een WPF-ContextMenu: zonder eigen venster verliest een
/// ContextMenu bij een tray-app meteen de focus en gaat het direct weer dicht. Het venster sluit
/// bij een klik ernaast (Deactivated), bij Esc en na een keuze.
/// </remarks>
internal static class TrayMenu
{
    private static Window? _open;

    /// <param name="colors">Kleurendictionary van het gewenste thema (zie ThemeManager.CreateColorsFor).</param>
    public static void Show(IReadOnlyList<TrayMenuEntry?> entries, ResourceDictionary colors)
    {
        _open?.Close();

        var list = new Menu { Background = System.Windows.Media.Brushes.Transparent };
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
            {
                Style = (Style)Application.Current.FindResource("TrayMenuPanel"),
                Child = list,
            },
        };
        window.Resources.MergedDictionaries.Add(colors);

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                list.Items.Add(new Separator { Style = (Style)Application.Current.FindResource("TrayMenuSeparator") });
                continue;
            }

            var item = new MenuItem
            {
                Style = (Style)Application.Current.FindResource("TrayMenuItem"),
                Header = entry.Text,
                Tag = entry.Glyph,
                InputGestureText = entry.Shortcut ?? "",
                IsEnabled = entry.IsEnabled,
            };
            var onClick = entry.OnClick;
            item.Click += (_, _) =>
            {
                window.Close();
                onClick();
            };
            list.Items.Add(item);
        }

        var closing = false;
        window.Deactivated += (_, _) =>
        {
            if (!closing)
                window.Close();
        };
        window.Closing += (_, _) => closing = true;
        window.Closed += (_, _) =>
        {
            if (_open == window)
                _open = null;
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                window.Close();
        };

        _open = window;
        window.Show();
        PlaceAtCursor(window);
        window.Activate();
    }

    /// <summary>
    /// Zet het menu boven-links van de muis (zoals Windows-menu's bij het systeemvak), binnen het
    /// werkgebied van dat scherm. In schermpixels, dus goed bij elke schaal.
    /// </summary>
    private static void PlaceAtCursor(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetCursorPos(out var cursor))
            return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(cursor, 2 /* dichtstbijzijnde */), ref info))
            return;

        // Twee keer: op een scherm met een andere schaal verandert de grootte na het verplaatsen.
        for (var i = 0; i < 2; i++)
        {
            GetWindowRect(handle, out var r);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            var x = Math.Clamp(cursor.X - width, info.rcWork.Left, info.rcWork.Right - width);
            var y = Math.Clamp(cursor.Y - height, info.rcWork.Top, info.rcWork.Bottom - height);
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0001 /* NOSIZE */ | 0x0004 /* NOZORDER */);
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
