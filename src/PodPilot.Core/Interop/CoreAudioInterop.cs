using System.Runtime.InteropServices;

namespace PodPilot.Core.Interop;

// COM-definities voor de Windows Core Audio API (mmdeviceapi.h, devicetopology.h) en Kernel
// Streaming (ks.h). Alleen de methoden die we gebruiken staan erin. Bij COM bepaalt de volgorde
// de vtable, dus methoden die vóór een gebruikte methode staan, zijn wel opgenomen.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal static class DeviceState
{
    public const uint Active = 0x1;
    public const uint Disabled = 0x2;
    public const uint NotPresent = 0x4;
    public const uint Unplugged = 0x8;
}

internal static class ClsCtx
{
    public const uint All = 0x17; // CLSCTX_INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

/// <summary>Vereenvoudigde PROPVARIANT: alleen het type en de pointer naar de waarde.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    public IntPtr Pointer;
    private IntPtr _pointer2;

    public const ushort VtLpwstr = 31;
    public const ushort VtClsid = 72;
}

/// <summary>KSPROPERTY uit ks.h.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KsProperty
{
    public Guid Set;
    public uint Id;
    public uint Flags;

    public KsProperty(Guid set, uint id, uint flags)
    {
        Set = set;
        Id = id;
        Flags = flags;
    }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice endpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
                  [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(uint storageAccess, out IPropertyStore properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetState(out uint state);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
}

[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    void GetConnectorCount(out uint count);
    void GetConnector(uint index, out IConnector connector);
}

[ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    void GetConnectorType(out int type); // GetType in C++; hernoemd vanwege object.GetType()
    void GetDataFlow(out int flow);
    void ConnectTo(IConnector other);
    void Disconnect();
    void IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
    void GetConnectedTo(out IConnector other);
    void GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsControl
{
    [PreserveSig]
    int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength,
                   out uint bytesReturned);
}

internal static class Ole32
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant value);
}
