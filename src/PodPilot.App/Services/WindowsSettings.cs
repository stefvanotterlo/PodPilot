using System.Diagnostics;

namespace PodPilot.App.Services;

/// <summary>Opent pagina's van de Windows-instellingen.</summary>
public static class WindowsSettings
{
    /// <summary>Opent Instellingen > Bluetooth en apparaten.</summary>
    public static void OpenBluetooth() =>
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
}
