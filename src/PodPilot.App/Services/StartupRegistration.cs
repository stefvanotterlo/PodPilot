using Microsoft.Win32;

namespace PodPilot.App.Services;

/// <summary>
/// "Starten met Windows" via de Run-sleutel van de huidige gebruiker. Daarvoor zijn geen
/// beheerdersrechten nodig, en de instelling staat ook bij Taakbeheer > Opstarten.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PodPilot";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>True als PodPilot bij het aanmelden start. De registry is hier de bron, niet settings.json.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Is de exe verplaatst (bijv. na een update), dan wijst de opstartregel weer naar de juiste plek.</summary>
    public static void RefreshPathIfEnabled()
    {
        if (IsEnabled())
            Set(true);
    }
}
