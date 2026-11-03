using System.Runtime.InteropServices;
using PodPilot.Core.Interop;

namespace PodPilot.Core.Devices;

/// <summary>Resultaat van een verbind- of verbreekverzoek.</summary>
public enum AudioConnectResult
{
    /// <summary>
    /// Windows heeft het verzoek aangenomen. Dat betekent nog niet dat het lukt. Wacht daarvoor op
    /// <see cref="DeviceConnectionMonitor.WaitForAsync"/>.
    /// </summary>
    Requested,

    /// <summary>Er is geen audio-endpoint van dit apparaat gevonden (niet gepaird als audioapparaat?).</summary>
    NoAudioEndpoint,

    /// <summary>Er zijn endpoints gevonden, maar Windows wees het verzoek af.</summary>
    Failed,
}

/// <summary>
/// Verbindt of verbreekt een al gepaird Bluetooth-audioapparaat. Windows doet hetzelfde bij
/// "Verbinden" in het geluidsmenu.
/// </summary>
/// <remarks>
/// Werkwijze:
/// <list type="number">
/// <item>Zoek de audio-endpoints (luidspreker/microfoon) met dezelfde container-ID als het apparaat.</item>
/// <item>Volg via IDeviceTopology de connector naar het Kernel Streaming-filter van de
///       Bluetooth-audiodriver (A2DP of HFP).</item>
/// <item>Vraag op dat filter de property KSPROPERTY_ONESHOT_RECONNECT (of _DISCONNECT) op uit
///       de set KSPROPSETID_BtAudio.</item>
/// </list>
/// Hiervoor zijn geen beheerdersrechten nodig. De AirPods moeten wel wakker zijn: uit de case of
/// met de deksel open.
/// </remarks>
public static class BluetoothAudioConnector
{
    // KSPROPSETID_BtAudio uit bthdef/ksmedia.h
    private static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint KsPropertyOneShotReconnect = 0;
    private const uint KsPropertyOneShotDisconnect = 1;
    private const uint KsPropertyTypeGet = 0x1;

    // PKEY_Device_ContainerId en PKEY_DeviceInterface_FriendlyName (functiondiscoverykeys_devpkey.h)
    private static readonly PropertyKey ContainerIdKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private static readonly PropertyKey InterfaceNameKey = new(new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), 2);

    private const uint StgmRead = 0;

    /// <param name="log">Optioneel: ontvangt diagnoseregels (welke endpoints en filters er gevonden zijn).</param>
    public static Task<AudioConnectResult> ConnectAsync(Guid containerId, string deviceName,
                                                        Action<string>? log = null) =>
        SendBothRoutesAsync(containerId, deviceName, KsPropertyOneShotReconnect, log);

    public static Task<AudioConnectResult> DisconnectAsync(Guid containerId, string deviceName,
                                                           Action<string>? log = null) =>
        SendBothRoutesAsync(containerId, deviceName, KsPropertyOneShotDisconnect, log);

    /// <summary>
    /// Stuurt het verzoek via twee routes: (1) rechtstreeks naar de KS-filters van het apparaat
    /// (Microsoft-profieldrivers), (2) via de audio-endpoints. Een offload-driver als Intel Smart Sound
    /// neemt route 2 aan zonder iets te doen; route 1 bereikt dan toch de echte Bluetooth-driver.
    /// </summary>
    private static async Task<AudioConnectResult> SendBothRoutesAsync(Guid containerId, string deviceName,
                                                                      uint propertyId, Action<string>? log)
    {
        var viaFilters = false;
        try
        {
            foreach (var path in await KsFilterInterfaces.FindAsync(containerId))
            {
                var error = await Task.Run(() =>
                    KsFilterInterfaces.SendProperty(path, KsPropSetIdBtAudio, propertyId, KsPropertyTypeGet));
                log?.Invoke($"filter-interface {path}: {(error == 0 ? "OK" : $"fout {error}")}");
                viaFilters |= error == 0;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"filter-interfaces niet op te vragen: {ex.Message}");
        }

        var viaEndpoints = await Task.Run(() => SendOneShot(containerId, deviceName, propertyId, log));
        return viaFilters ? AudioConnectResult.Requested : viaEndpoints;
    }

    /// <summary>
    /// True als er een actief audio-endpoint van het apparaat is. Dan is de audio echt verbonden;
    /// een Bluetooth-verbinding alleen (ConnectionStatus) is niet genoeg.
    /// </summary>
    public static Task<bool> IsAudioActiveAsync(Guid containerId, string deviceName) => Task.Run(() =>
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(EDataFlow.All, DeviceState.Active, out var endpoints);
            endpoints.GetCount(out var count);

            for (uint i = 0; i < count; i++)
            {
                endpoints.Item(i, out var endpoint);
                if (BelongsToDevice(endpoint, containerId, deviceName))
                    return true;
            }
        }
        catch (Exception)
        {
            // Onleesbaar = niet actief.
        }

        return false;
    });

    /// <summary>Wacht (door te pollen) tot de audio wel of niet actief is. Geeft true als dat op tijd gebeurt.</summary>
    public static async Task<bool> WaitForAudioAsync(Guid containerId, string deviceName, bool active,
                                                     TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (await IsAudioActiveAsync(containerId, deviceName) == active)
                return true;
            await Task.Delay(500);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    private static AudioConnectResult SendOneShot(Guid containerId, string deviceName, uint propertyId,
                                                  Action<string>? log)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            // Een verbroken Bluetooth-endpoint is "unplugged" of "not present", geen "active".
            enumerator.EnumAudioEndpoints(EDataFlow.All,
                DeviceState.Active | DeviceState.Unplugged | DeviceState.NotPresent, out var endpoints);
            endpoints.GetCount(out var count);

            var foundEndpoint = false;
            var accepted = false;
            var handledFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (uint i = 0; i < count; i++)
            {
                endpoints.Item(i, out var endpoint);
                if (!BelongsToDevice(endpoint, containerId, deviceName))
                {
                    if (log is not null)
                        log($"(overgeslagen) {Describe(endpoint)}");
                    continue;
                }

                foundEndpoint = true;
                endpoint.GetId(out var endpointId);
                endpoint.GetState(out var state);
                log?.Invoke($"endpoint {endpointId} (status 0x{state:X})");

                // A2DP en HFP hebben elk een eigen filter. HFP heeft er twee endpoints op (luidspreker en
                // microfoon), dus elk filter maar één keer aanspreken.
                if (!TryGetBluetoothFilter(enumerator, endpoint, out var filterId, out var ksControl))
                {
                    log?.Invoke("  geen KS-filter gevonden");
                    continue;
                }
                if (!handledFilters.Add(filterId))
                    continue;

                var property = new KsProperty(KsPropSetIdBtAudio, propertyId, KsPropertyTypeGet);
                var hr = ksControl.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(),
                                              IntPtr.Zero, 0, out _);
                log?.Invoke($"  filter {filterId}: HRESULT 0x{hr:X8}");
                if (hr >= 0)
                    accepted = true;

                // Voor verbinden is één aangenomen verzoek genoeg. Bij verbreken sluiten we alle profielen.
                if (accepted && propertyId == KsPropertyOneShotReconnect)
                    break;
            }

            if (accepted)
                return AudioConnectResult.Requested;
            return foundEndpoint ? AudioConnectResult.Failed : AudioConnectResult.NoAudioEndpoint;
        }
        catch (Exception)
        {
            return AudioConnectResult.Failed;
        }
    }

    /// <summary>
    /// True als het endpoint bij het apparaat hoort. Eerst vergelijken we de container-ID. Werkt dat
    /// niet, dan de naam van de Bluetooth-interface (bijv. "AirPods").
    /// </summary>
    private static bool BelongsToDevice(IMMDevice endpoint, Guid containerId, string deviceName)
    {
        try
        {
            endpoint.OpenPropertyStore(StgmRead, out var store);

            if (ReadGuid(store, ContainerIdKey) is Guid endpointContainer)
                return endpointContainer == containerId;

            return string.Equals(ReadString(store, InterfaceNameKey), deviceName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Loopt van het endpoint naar het KS-filter van de Bluetooth-driver en vraagt daar IKsControl op.</summary>
    private static bool TryGetBluetoothFilter(IMMDeviceEnumerator enumerator, IMMDevice endpoint,
                                              out string filterId, out IKsControl ksControl)
    {
        filterId = "";
        ksControl = null!;
        try
        {
            var topologyIid = typeof(IDeviceTopology).GUID;
            endpoint.Activate(ref topologyIid, ClsCtx.All, IntPtr.Zero, out var topologyObject);
            var topology = (IDeviceTopology)topologyObject;

            topology.GetConnector(0, out var connector);
            connector.GetDeviceIdConnectedTo(out filterId);

            enumerator.GetDevice(filterId, out var filterDevice);
            var ksIid = typeof(IKsControl).GUID;
            filterDevice.Activate(ref ksIid, ClsCtx.All, IntPtr.Zero, out var ksObject);
            ksControl = (IKsControl)ksObject;
            return true;
        }
        catch (Exception)
        {
            // COM-fouten komen ook als andere .NET-exceptions binnen (bijv. DirectoryNotFoundException
            // voor een endpoint dat er niet meer is). Elke fout betekent hier: dit endpoint overslaan.
            return false;
        }
    }

    /// <summary>Korte omschrijving van een endpoint voor de diagnose-log.</summary>
    private static string Describe(IMMDevice endpoint)
    {
        try
        {
            endpoint.GetState(out var state);
            endpoint.OpenPropertyStore(StgmRead, out var store);
            return $"'{ReadString(store, InterfaceNameKey)}' container {ReadGuid(store, ContainerIdKey)} status 0x{state:X}";
        }
        catch (Exception)
        {
            return "(onleesbaar)";
        }
    }

    private static Guid? ReadGuid(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(ref key, out var value);
        try
        {
            return value.VarType == PropVariant.VtClsid && value.Pointer != IntPtr.Zero
                ? Marshal.PtrToStructure<Guid>(value.Pointer)
                : null;
        }
        finally
        {
            Ole32.PropVariantClear(ref value);
        }
    }

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(ref key, out var value);
        try
        {
            return value.VarType == PropVariant.VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            Ole32.PropVariantClear(ref value);
        }
    }
}
