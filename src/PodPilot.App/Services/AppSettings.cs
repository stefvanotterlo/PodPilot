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

    /// <summary>"Afspelen" sturen zodra de audio van de AirPods met deze pc verbonden is.</summary>
    public bool AutoPlayOnConnect { get; set; }

    /// <summary>Alles pauzeren zodra de AirPods niet meer verbonden zijn (geen muziek uit de luidsprekers).</summary>
    public bool AutoPauseOnDisconnect { get; set; }

    /// <summary>De AirPods het standaard-afspeelapparaat maken zodra ze verbonden zijn.</summary>
    public bool SwitchOutputOnConnect { get; set; }

    /// <summary>Sneltoets om naar de volgende geluidsuitgang te wisselen.</summary>
    public bool SwitchOutputHotkeyEnabled { get; set; }

    /// <summary>De combinatie voor het wisselen van de geluidsuitgang. Blijft bewaard als hij uit staat.</summary>
    public string? SwitchOutputHotkey { get; set; }

    /// <summary>Audio automatisch met deze pc verbinden als de case opengaat.</summary>
    public bool AutoConnectOnLidOpen { get; set; }

    /// <summary>Audio automatisch met deze pc verbinden als je een oordop indoet (vanuit "geen in het oor").</summary>
    public bool AutoConnectOnEarInsertion { get; set; }

    /// <summary>Sneltoets om de pop-up te tonen of te verbergen.</summary>
    public bool PopupHotkeyEnabled { get; set; }

    /// <summary>De combinatie, bijv. "Ctrl+Alt+P". Blijft bewaard als de sneltoets uit staat.</summary>
    public string? PopupHotkey { get; set; }

    /// <summary>Sneltoets om te verbinden of de verbinding te verbreken.</summary>
    public bool ConnectHotkeyEnabled { get; set; }

    /// <summary>De combinatie voor verbinden/verbreken. Blijft bewaard als de sneltoets uit staat.</summary>
    public string? ConnectHotkey { get; set; }

    /// <summary>Taal van de app; wisselen werkt na herstarten.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Laatst gekozen telefoon voor "pc als speaker" (apparaat-ID van Windows).</summary>
    public string? StreamSourceId { get; set; }

    /// <summary>Bij het opstarten op GitHub kijken of er een nieuwere versie is.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Voor deze versie is al een melding getoond (niet bij elke start opnieuw).</summary>
    public string? UpdateNotifiedVersion { get; set; }

    /// <summary>Hoe het tray-icoon de batterij toont.</summary>
    public TrayIconStyle TrayStyle { get; set; } = TrayIconStyle.Icon;

    /// <summary>Lettertype voor de stijl "Getal".</summary>
    public string TrayFont { get; set; } = "Segoe UI";

    public TrayFontSize TrayFontSize { get; set; } = TrayFontSize.Normal;

    /// <summary>Kleur van het getal bij een lichte taakbalk: "auto", "level" (batterijkleur) of "#RRGGBB".</summary>
    public string TrayColorLight { get; set; } = "auto";

    /// <summary>Kleur van het getal bij een donkere taakbalk: "auto", "level" of "#RRGGBB".</summary>
    public string TrayColorDark { get; set; } = "auto";

    /// <summary>Icoon doorzichtig als het geluid niet via de AirPods gaat.</summary>
    public bool TrayDimWhenNotAirPods { get; set; }

    /// <summary>Zichtbaarheid van het icoon in procenten als het doorzichtig is (10–90).</summary>
    public int TrayDimOpacity { get; set; } = 40;

    /// <summary>Thema van het hoofdvenster.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Thema van de pop-up bij het systeemvak (los van het hoofdvenster te kiezen).</summary>
    public AppTheme PopupTheme { get; set; } = AppTheme.System;

    /// <summary>
    /// True: een versleepte pop-up blijft op die plek (ook na herstarten). False: slepen kan wel,
    /// maar de volgende keer staat hij weer rechtsonder. De bewaarde plek blijft dan staan.
    /// </summary>
    public bool RememberPopupPosition { get; set; } = true;

    /// <summary>
    /// True (standaard): de pop-up komt altijd op het hoofdscherm. False: bij een klik of sneltoets
    /// op het scherm waar de muis is (automatisch tonen bij case open blijft het hoofdscherm).
    /// </summary>
    public bool PopupOnMainScreen { get; set; } = true;

    /// <summary>Laatst bekende case-batterij (zonder oordop in de case is die niet te zien).</summary>
    public int? LastCasePercent { get; set; }

    /// <summary>Wanneer de case-batterij voor het laatst is gezien.</summary>
    public DateTimeOffset? LastCaseSeenAt { get; set; }

    /// <summary>Model (PID) waar die waarde bij hoort.</summary>
    public int? LastCaseModelId { get; set; }

    /// <summary>Zelf gekozen plek van de pop-up (linkerbovenhoek, schermpixels); null = rechtsonder.</summary>
    public int? PopupX { get; set; }

    public int? PopupY { get; set; }

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
