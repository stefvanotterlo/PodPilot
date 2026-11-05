using PodPilot.App.Services;
using PodPilot.Core.Devices;

namespace PodPilot.App.ViewModels;

/// <summary>Eén kaart op het tabblad Apparaten: een gepaird Bluetooth-audioapparaat.</summary>
public sealed class DeviceCardViewModel : ObservableObject
{
    private AudioDevice _device;
    private string _batteryText = "";
    private bool _isBusy;
    private string? _message;

    public DeviceCardViewModel(AudioDevice device, bool isPodPilotDevice)
    {
        _device = device;
        IsPodPilotDevice = isPodPilotDevice;
    }

    public AudioDevice Device => _device;

    public string Name => _device.Name;

    /// <summary>True voor de AirPods die PodPilot gebruikt (label "PodPilot" op de kaart).</summary>
    public bool IsPodPilotDevice { get; }

    public bool IsConnected => _device.IsConnected;

    public string StatusText => _message ?? (_device.IsConnected ? L.T("Verbonden met deze pc") : L.T("Niet verbonden"));

    /// <summary>Bijv. "Batterij 80%" of "L 80% · R 70% · Case 50%"; leeg als onbekend.</summary>
    public string BatteryText
    {
        get => _batteryText;
        set
        {
            if (SetField(ref _batteryText, value))
                OnPropertyChanged(nameof(HasBattery));
        }
    }

    public bool HasBattery => _batteryText.Length > 0;

    public bool CanToggle => !_isBusy;

    public string ButtonText => _isBusy ? L.T("Bezig…") : _device.IsConnected ? L.T("Verbreken") : L.T("Verbinden");

    /// <summary>Nieuwe gegevens na het verversen van de lijst.</summary>
    public void Update(AudioDevice device)
    {
        _device = device;
        Raise();
    }

    public void SetBusy(bool busy, string? message)
    {
        _isBusy = busy;
        _message = message;
        Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ButtonText));
    }
}
