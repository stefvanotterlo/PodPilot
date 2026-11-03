using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PodPilot.App.Services;

/// <summary>
/// Maakt een snelkoppeling in het Startmenu met een vaste AppUserModelID (AUMID).
/// </summary>
/// <remarks>
/// Windows koppelt meldingen van een niet-verpakte app aan zo'n snelkoppeling. Zonder snelkoppeling
/// komen meldingen wel in het Meldingencentrum, maar dan staat de app niet bij Instellingen > Meldingen
/// en verschijnen er vaak geen banners. De installer (stap 8) maakt dezelfde snelkoppeling.
/// </remarks>
internal static class StartMenuShortcut
{
    /// <summary>Vaste app-ID voor meldingen, het tray-icoon en de snelkoppeling.</summary>
    public const string AppUserModelId = "PodPilot.App";

    // PKEY_AppUserModel_ID (propkey.h)
    private static readonly PropertyKey AppUserModelIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PodPilot.lnk");

    /// <summary>Geeft dit proces de vaste app-ID. Aanroepen bij het opstarten, vóór het eerste venster.</summary>
    public static void SetProcessAppId() => SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

    /// <summary>
    /// Maakt de snelkoppeling aan of werkt hem bij, zodat hij naar de huidige exe wijst.
    /// Fouten worden genegeerd: zonder snelkoppeling werkt de app ook, alleen zonder banners.
    /// </summary>
    public static void EnsureExists()
    {
        try
        {
            var exePath = Environment.ProcessPath!;
            var link = (IShellLinkW)new ShellLinkComObject();
            link.SetPath(exePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath)!);
            link.SetDescription("AirPods-batterij en verbinding");

            var store = (IPropertyStore)link;
            var key = AppUserModelIdKey;
            var value = new PropVariant { VarType = PropVariant.VtLpwstr, Pointer = Marshal.StringToCoTaskMemUni(AppUserModelId) };
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref value);
            }

            ((IPersistFile)link).Save(ShortcutPath, true);
        }
        catch (Exception)
        {
            // Bijv. als het Startmenu door beleid op alleen-lezen staat.
        }
    }

    // --- COM-interop --------------------------------------------------------------------

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkComObject
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription(IntPtr name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(IntPtr args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(IntPtr iconPath, int maxPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public const ushort VtLpwstr = 31;

        public ushort VarType;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        public IntPtr Pointer;
        private IntPtr _pointer2;
    }
}
