using Windows.Devices.Bluetooth;

namespace PodPilot.Core.Devices;

/// <summary>
/// Houdt bij of een gepaird (klassiek) Bluetooth-apparaat verbonden is met deze pc.
/// Events komen binnen op een achtergrondthread.
/// </summary>
public sealed class DeviceConnectionMonitor : IDisposable
{
    private readonly BluetoothDevice _device;

    private DeviceConnectionMonitor(BluetoothDevice device)
    {
        _device = device;
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    /// <summary>Opent het apparaat. Geeft null als het niet (meer) bestaat.</summary>
    public static async Task<DeviceConnectionMonitor?> CreateAsync(string deviceId)
    {
        var device = await BluetoothDevice.FromIdAsync(deviceId);
        return device is null ? null : new DeviceConnectionMonitor(device);
    }

    public bool IsConnected => _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    /// <summary>De verbinding is veranderd; true = verbonden.</summary>
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Wacht tot het apparaat de gewenste status heeft, maar niet langer dan de timeout.
    /// Geeft true als de status op tijd is bereikt.
    /// </summary>
    public async Task<bool> WaitForAsync(bool connected, TimeSpan timeout)
    {
        if (IsConnected == connected)
            return true;

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, bool isConnected)
        {
            if (isConnected == connected)
                reached.TrySetResult();
        }

        ConnectionChanged += Handler;
        try
        {
            // Opnieuw controleren: de status kan veranderd zijn vlak voordat we ons aanmeldden.
            if (IsConnected == connected)
                return true;

            var finished = await Task.WhenAny(reached.Task, Task.Delay(timeout));
            return finished == reached.Task;
        }
        finally
        {
            ConnectionChanged -= Handler;
        }
    }

    /// <summary>
    /// "Port aan" het apparaat: een SDP-query zonder cache dwingt Windows om een Bluetooth-
    /// (ACL-)verbinding op te zetten. Daarna slaagt een audioverbindingsverzoek vaak wel, ook bij
    /// drivers die een verzoek zonder actieve verbinding negeren (bijv. Intel Smart Sound).
    /// </summary>
    public async Task WakeAsync()
    {
        try
        {
            await _device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
        }
        catch (Exception)
        {
            // Mislukt als het apparaat buiten bereik is of slaapt; de aanroeper merkt dat aan de status.
        }
    }

    private void OnConnectionStatusChanged(BluetoothDevice sender, object args) =>
        ConnectionChanged?.Invoke(this, IsConnected);

    public void Dispose()
    {
        _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _device.Dispose();
    }
}
