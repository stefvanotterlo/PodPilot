using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace PodPilot.Core.Devices;

/// <summary>Een met deze pc gepaird (klassiek) Bluetooth-audioapparaat van Apple.</summary>
/// <param name="DeviceId">Windows-apparaat-ID voor <c>BluetoothDevice.FromIdAsync</c>.</param>
/// <param name="Name">Naam zoals in de Bluetooth-instellingen, bijv. "Mijn AirPods Pro".</param>
/// <param name="BluetoothAddress">Klassiek Bluetooth-adres (dit is NIET het BLE-advertentieadres).</param>
/// <param name="IsConnected">True als het apparaat nu verbonden is.</param>
/// <param name="ProductId">Apple-product-ID (= model-ID in de advertentie), als dat bekend is.</param>
/// <param name="ContainerId">Container-ID. Hiermee vinden we later de bijbehorende audio-endpoints.</param>
public sealed record PairedAppleDevice(
    string DeviceId,
    string Name,
    ulong BluetoothAddress,
    bool IsConnected,
    ushort? ProductId,
    Guid? ContainerId);

/// <summary>
/// Zoekt de gepairde AirPods op. Het product-ID komt uit de hardware-ID's van de
/// Bluetooth-driver, bijv. <c>BTHENUM\{0000110b-...}_VID&amp;0001004c_PID&amp;2014</c>.
/// </summary>
public static partial class PairedDeviceService
{
    private const string ContainerIdProperty = "System.Devices.Aep.ContainerId";
    private const string HardwareIdsProperty = "System.Devices.HardwareIds";

    // VID 004C = Apple. De "0001" ervoor geeft aan dat het een Bluetooth SIG-vendor-ID is.
    [GeneratedRegex(@"VID&(?:0001|0002)?004C_PID&([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex AppleVidPidRegex();

    /// <summary>
    /// Geeft alle gepairde Bluetooth-apparaten die van Apple zijn (op basis van VID) of
    /// "AirPods" in hun naam hebben.
    /// </summary>
    public static async Task<IReadOnlyList<PairedAppleDevice>> GetPairedAppleDevicesAsync()
    {
        var result = new List<PairedAppleDevice>();

        // Klassieke (BR/EDR) Bluetooth-apparaten die gepaird zijn. AirPods-audio loopt via klassiek BT.
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(
            selector, new[] { ContainerIdProperty }, DeviceInformationKind.AssociationEndpoint);

        foreach (var info in infos)
        {
            Guid? containerId = info.Properties.TryGetValue(ContainerIdProperty, out var c) && c is Guid g
                ? g
                : null;

            var productId = containerId is Guid cid ? await TryGetAppleProductIdAsync(cid) : null;
            if (productId is null && !info.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase))
                continue;

            using var device = await BluetoothDevice.FromIdAsync(info.Id);
            if (device is null)
                continue;

            result.Add(new PairedAppleDevice(
                info.Id,
                device.Name,
                device.BluetoothAddress,
                device.ConnectionStatus == BluetoothConnectionStatus.Connected,
                productId,
                containerId));
        }

        return result;
    }

    /// <summary>
    /// Zoekt de apparaatknopen (devnodes) in dezelfde container en leest het Apple-PID uit hun
    /// hardware-ID's.
    /// </summary>
    private static async Task<ushort?> TryGetAppleProductIdAsync(Guid containerId)
    {
        try
        {
            var aqs = $"System.Devices.ContainerId:=\"{{{containerId}}}\"";
            var nodes = await DeviceInformation.FindAllAsync(
                aqs, new[] { HardwareIdsProperty }, DeviceInformationKind.Device);

            foreach (var node in nodes)
            {
                if (!node.Properties.TryGetValue(HardwareIdsProperty, out var value) || value is not string[] ids)
                    continue;

                foreach (var id in ids)
                {
                    var match = AppleVidPidRegex().Match(id);
                    if (match.Success)
                        return Convert.ToUInt16(match.Groups[1].Value, 16);
                }
            }
        }
        catch (Exception)
        {
            // Opzoeken is "best effort". Zonder PID werkt de app ook, maar dan filteren we
            // alleen op signaalsterkte.
        }

        return null;
    }
}
