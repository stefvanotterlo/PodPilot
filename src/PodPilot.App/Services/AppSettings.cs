using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PodPilot.App.Services;

/// <summary>
/// Instellingen van de app, opgeslagen als JSON in %AppData%\PodPilot\settings.json.
/// In stap 6 komt er een venster bij om ze aan te passen.
/// </summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PodPilot", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Melding bij lage batterij (oordopjes, niet de case).</summary>
    public bool NotifyLowBattery { get; set; } = true;

    /// <summary>Drempel in procenten; AirPods geven stappen van 10%.</summary>
    public int LowBatteryThreshold { get; set; } = 20;

    /// <summary>Pop-up tonen als de case wordt geopend.</summary>
    public bool AutoPopupOnLidOpen { get; set; } = true;

    /// <summary>Media pauzeren als je een oordop uitdoet (alleen als de AirPods met deze pc verbonden zijn).</summary>
    public bool AutoPauseOnEarRemoval { get; set; } = true;

    /// <summary>Weer afspelen als je de oordop terug in je oor doet.</summary>
    public bool AutoResumeOnEarInsertion { get; set; } = true;

    /// <summary>Audio automatisch met deze pc verbinden als de case opengaat.</summary>
    public bool AutoConnectOnLidOpen { get; set; }

    /// <summary>Audio automatisch met deze pc verbinden als je een oordop indoet (vanuit "geen in het oor").</summary>
    public bool AutoConnectOnEarInsertion { get; set; }

    /// <summary>Sneltoets om de pop-up te tonen of te verbergen.</summary>
    public bool PopupHotkeyEnabled { get; set; }

    /// <summary>De combinatie, bijv. "Ctrl+Alt+P". Blijft bewaard als de sneltoets uit staat.</summary>
    public string? PopupHotkey { get; set; }

    /// <summary>Thema van het hoofdvenster.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Thema van de pop-up bij het systeemvak (los van het hoofdvenster te kiezen).</summary>
    public AppTheme PopupTheme { get; set; } = AppTheme.System;

    /// <summary>Eigen achtergrondkleur voor de hele app (in plaats van licht/donker).</summary>
    public bool UseCustomBackground { get; set; }

    /// <summary>De gekozen achtergrondkleur, "#RRGGBB". Blijft bewaard als de optie uit staat.</summary>
    public string CustomBackground { get; set; } = "#1E1B2E";

    /// <summary>Eigen accentkleur (in plaats van de accentkleur van Windows).</summary>
    public bool UseCustomAccent { get; set; }

    /// <summary>De gekozen accentkleur, "#RRGGBB". Blijft bewaard als de optie uit staat.</summary>
    public string CustomAccent { get; set; } = "#8B5CF6";

    /// <summary>Windows-apparaat-ID van de gekozen gepairde AirPods; null = automatisch.</summary>
    public string? PreferredDeviceId { get; set; }

    /// <summary>Laadt de instellingen. Als het bestand ontbreekt of kapot is, gelden de standaardwaarden.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception)
        {
            // Een onleesbaar bestand mag de app niet laten crashen.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
