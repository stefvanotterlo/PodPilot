using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace PodPilot.App.Services;

/// <summary>
/// Een sneltoets die overal in Windows werkt (RegisterHotKey). De meldingen komen binnen op een
/// onzichtbaar berichtvenster op de UI-thread.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private static readonly IntPtr HwndMessage = new(-3);

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000; // ingedrukt houden telt als één keer

    private readonly HwndSource _source;
    private bool _isRegistered;

    public GlobalHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("PodPilot.Hotkey") { ParentWindow = HwndMessage });
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>
    /// Zet de sneltoets (null = uit). Geeft false als een andere app deze combinatie al gebruikt;
    /// er is dan geen sneltoets actief.
    /// </summary>
    public bool Set(Hotkey? hotkey)
    {
        if (_isRegistered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _isRegistered = false;
        }

        if (hotkey is not { } h)
            return true;

        var modifiers = ModNoRepeat
                        | (h.Modifiers.HasFlag(ModifierKeys.Alt) ? ModAlt : 0)
                        | (h.Modifiers.HasFlag(ModifierKeys.Control) ? ModControl : 0)
                        | (h.Modifiers.HasFlag(ModifierKeys.Shift) ? ModShift : 0)
                        | (h.Modifiers.HasFlag(ModifierKeys.Windows) ? ModWin : 0);
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(h.Key);

        _isRegistered = RegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey);
        return _isRegistered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Set(null);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
