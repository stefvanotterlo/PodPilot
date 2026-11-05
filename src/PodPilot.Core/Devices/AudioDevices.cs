using PodPilot.Core.Interop;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace PodPilot.Core.Devices;

/// <summary>Een gepaird Bluetooth-audioapparaat (koptelefoon, oordopjes, speaker), van elk merk.</summary>
/// <param name="DeviceId">Windows-apparaat-ID (zoals bij <see cref="PairedAppleDevice"/>).</param>
/// <param name="ContainerId">Container-ID: koppelt het apparaat aan zijn audio-endpoints.</param>
/// <param name="IsConnected">True als de audio nu met deze pc verbonden is (actief endpoint).</param>
/// <param name="BatteryPercent">Batterij volgens Windows (via het belprofiel), of null als onbekend.</param>
public sealed record AudioDevice(string DeviceId, string Name, Guid ContainerId, bool IsConnected, int? BatteryPercent);

/// <summary>
/// Zoekt alle gepairde Bluetooth-apparaten die audio-endpoints hebben, met hun verbindingsstatus
/// en (als Windows die kent) de batterij.
/// </summary>
public static class AudioDevices
{
    private const string ContainerIdProperty = "System.Devices.Aep.ContainerId";

    // DEVPKEY_Bluetooth_Battery: de batterij die Windows via het belprofiel (HFP) binnenkrijgt.
    // Dezelfde waarde als in Instellingen > Bluetooth. Staat op een apparaatknoop in de container.
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public static async Task<IReadOnlyList<AudioDevice>> GetAsync()
    {
        // Container-ID's van alle audio-endpoints (ook niet-verbonden) en welke daarvan actief zijn.
        var (allContainers, activeContainers) = await Task.Run(ReadEndpointContainers);

        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(
            selector, [ContainerIdProperty], DeviceInformationKind.AssociationEndpoint);

        var result = new List<AudioDevice>();
        foreach (var info in infos)
        {
            if (!info.Properties.TryGetValue(ContainerIdProperty, out var c) || c is not Guid containerId
                || !allContainers.Contains(containerId))
                continue;

            var connected = activeContainers.Contains(containerId);
            var battery = connected ? await TryGetBatteryAsync(containerId) : null;
            result.Add(new AudioDevice(info.Id, info.Name, containerId, connected, battery));
        }

        return result.OrderByDescending(d => d.IsConnected).ThenBy(d => d.Name).ToList();
    }

    private static (HashSet<Guid> All, HashSet<Guid> Active) ReadEndpointContainers()
    {
        var all = new HashSet<Guid>();
        var active = new HashSet<Guid>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(EDataFlow.Render,
                DeviceState.Active | DeviceState.Unplugged | DeviceState.NotPresent, out var endpoints);
            endpoints.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                endpoints.Item(i, out var endpoint);
                if (AudioOutputs.ReadContainerId(endpoint) is not Guid container)
                    continue;
                all.Add(container);
                endpoint.GetState(out var state);
                if (state == DeviceState.Active)
                    active.Add(container);
            }
        }
        catch (Exception)
        {
            // Geen audio-API: dan vinden we geen apparaten.
        }

        return (all, active);
    }

    private static async Task<int?> TryGetBatteryAsync(Guid containerId)
    {
        try
        {
            var aqs = $"System.Devices.ContainerId:=\"{{{containerId}}}\"";
            var nodes = await DeviceInformation.FindAllAsync(aqs, [BatteryProperty], DeviceInformationKind.Device);
            foreach (var node in nodes)
            {
                if (node.Properties.TryGetValue(BatteryProperty, out var value) && value is byte level && level <= 100)
                    return level;
            }
        }
        catch (Exception)
        {
            // Niet elk apparaat (en niet elke driver) geeft de batterij door.
        }

        return null;
    }
}
