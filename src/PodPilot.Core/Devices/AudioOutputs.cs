using System.Runtime.InteropServices;
using PodPilot.Core.Interop;

namespace PodPilot.Core.Devices;

/// <summary>Een actief afspeelapparaat (geluidsuitgang) van Windows.</summary>
/// <param name="Id">Endpoint-ID van Windows.</param>
/// <param name="Name">Naam zoals in het geluidsmenu, bijv. "Luidsprekers (Realtek(R) Audio)".</param>
public sealed record AudioOutput(string Id, string Name, Guid? ContainerId);

/// <summary>
/// De geluidsuitgangen van Windows: welke er zijn, welke de standaard is, en een andere als
/// standaard instellen (voor alle drie de rollen: console, multimedia en communicatie).
/// </summary>
public static class AudioOutputs
{
    // PKEY_Device_FriendlyName en PKEY_Device_ContainerId (functiondiscoverykeys_devpkey.h)
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly PropertyKey ContainerIdKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private const int RoleMultimedia = 1;

    /// <summary>Alle actieve geluidsuitgangen, in de volgorde van Windows.</summary>
    public static IReadOnlyList<AudioOutput> GetActive()
    {
        var result = new List<AudioOutput>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out var endpoints);
            endpoints.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                endpoints.Item(i, out var endpoint);
                endpoint.GetId(out var id);
                endpoint.OpenPropertyStore(0, out var store);
                result.Add(new AudioOutput(id, ReadString(store, FriendlyNameKey) ?? id, ReadGuid(store, ContainerIdKey)));
            }
        }
        catch (Exception)
        {
            // Geen audio-API beschikbaar: lege lijst.
        }

        return result;
    }

    /// <summary>ID van de huidige standaard-geluidsuitgang (rol multimedia), of null.</summary>
    public static string? GetDefaultId()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, RoleMultimedia, out var endpoint);
            endpoint.GetId(out var id);
            return id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Maakt deze uitgang de standaard voor alle rollen. Geeft true als dat lukte.</summary>
    public static bool SetDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            var ok = true;
            for (var role = 0; role <= 2; role++)
                ok &= policy.SetDefaultEndpoint(id, role) >= 0;
            return ok;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// De stereo-uitgang van een Bluetooth-apparaat (de AirPods), niet de "Hands-Free"-uitgang die
    /// voor bellen is bedoeld en slechter klinkt.
    /// </summary>
    public static AudioOutput? FindForDevice(Guid containerId)
    {
        var outputs = GetActive().Where(o => o.ContainerId == containerId).ToList();
        return outputs.FirstOrDefault(o => !IsHandsFree(o.Name)) ?? outputs.FirstOrDefault();
    }

    /// <summary>De uitgang na de huidige standaard (en na de laatste weer de eerste).</summary>
    public static AudioOutput? Next()
    {
        var outputs = GetActive();
        if (outputs.Count == 0)
            return null;

        var current = GetDefaultId();
        var index = outputs.ToList().FindIndex(o => o.Id == current);
        return outputs[(index + 1) % outputs.Count];
    }

    /// <summary>Een endpoint (uitgang of microfoon) met zijn status, voor het handsfree-profiel.</summary>
    public sealed record Endpoint(string Id, string Name, bool IsCapture, uint State)
    {
        public bool IsDisabled => (State & DeviceState.Disabled) != 0;
    }

    /// <summary>
    /// De "Hands-Free"-endpoints van een Bluetooth-apparaat (luidspreker en microfoon van het
    /// belprofiel), in elke status, ook uitgeschakeld.
    /// </summary>
    public static IReadOnlyList<Endpoint> GetHandsFreeEndpoints(Guid containerId)
    {
        var result = new List<Endpoint>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(EDataFlow.All,
                DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged | DeviceState.NotPresent, out var endpoints);
            endpoints.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                endpoints.Item(i, out var endpoint);
                endpoint.OpenPropertyStore(0, out var store);
                if (ReadGuid(store, ContainerIdKey) != containerId)
                    continue;

                var name = ReadString(store, FriendlyNameKey) ?? "";
                endpoint.GetId(out var id);
                endpoint.GetState(out var state);
                // Microfoons van een Bluetooth-apparaat horen altijd bij het belprofiel.
                var isCapture = id.StartsWith("{0.0.1.", StringComparison.Ordinal);
                if (isCapture || IsHandsFree(name))
                    result.Add(new Endpoint(id, name, isCapture, state));
            }
        }
        catch (Exception)
        {
            // Geen audio-API: niets gevonden.
        }

        return result;
    }

    /// <summary>Zet een endpoint aan of uit. Geeft de HRESULT terug (0 = gelukt).</summary>
    public static int SetEnabled(string id, bool enabled)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            return policy.SetEndpointVisibility(id, enabled ? 1 : 0);
        }
        catch (Exception ex)
        {
            return ex.HResult;
        }
    }

    /// <summary>Container-ID van een endpoint (voor <see cref="AudioDevices"/>), of null.</summary>
    internal static Guid? ReadContainerId(IMMDevice endpoint)
    {
        try
        {
            endpoint.OpenPropertyStore(0, out var store);
            return ReadGuid(store, ContainerIdKey);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsHandsFree(string name) =>
        name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Handsfree", StringComparison.OrdinalIgnoreCase);

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
