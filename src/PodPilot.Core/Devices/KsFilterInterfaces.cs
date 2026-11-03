using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;

namespace PodPilot.Core.Devices;

/// <summary>
/// Tweede route naar de Bluetooth-audiodriver: open de KS-filters (device interfaces in
/// KSCATEGORY_AUDIO) van het apparaat zelf en stuur de one-shot property met DeviceIoControl.
/// Zo bereiken we de Microsoft-profieldriver (BTHENUM), ook als het audio-endpoint via een
/// offload-driver loopt (bijv. Intel Smart Sound).
/// </summary>
internal static class KsFilterInterfaces
{
    private const string KsCategoryAudio = "{6994AD04-93EF-11D0-A3CC-00A0C9223196}";

    // CTL_CODE(FILE_DEVICE_KS = 0x2F, 0x000, METHOD_NEITHER, FILE_ANY_ACCESS)
    private const uint IoctlKsProperty = 0x002F0003;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x3;
    private const uint OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct KsPropertyData
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security,
                                                    uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
                                               ref KsPropertyData inBuffer, uint inSize,
                                               IntPtr outBuffer, uint outSize,
                                               out uint bytesReturned, IntPtr overlapped);

    /// <summary>Geeft de paden van alle ingeschakelde KS-audiofilters in de container van het apparaat.</summary>
    public static async Task<IReadOnlyList<string>> FindAsync(Guid containerId)
    {
        var aqs = $"System.Devices.InterfaceClassGuid:=\"{KsCategoryAudio}\" AND " +
                  $"System.Devices.ContainerId:=\"{{{containerId}}}\" AND " +
                  "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var interfaces = await DeviceInformation.FindAllAsync(aqs, null, DeviceInformationKind.DeviceInterface);
        return interfaces.Select(i => i.Id).ToList();
    }

    /// <summary>Stuurt een KS-property (GET, zonder data) naar een filter. Geeft 0 of de Win32-foutcode.</summary>
    public static int SendProperty(string devicePath, Guid set, uint id, uint flags)
    {
        using var handle = CreateFile(devicePath, GenericRead | GenericWrite, FileShareReadWrite,
                                      IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return Marshal.GetLastWin32Error();

        var property = new KsPropertyData { Set = set, Id = id, Flags = flags };
        return DeviceIoControl(handle, IoctlKsProperty, ref property, (uint)Marshal.SizeOf<KsPropertyData>(),
                               IntPtr.Zero, 0, out _, IntPtr.Zero)
            ? 0
            : Marshal.GetLastWin32Error();
    }
}
