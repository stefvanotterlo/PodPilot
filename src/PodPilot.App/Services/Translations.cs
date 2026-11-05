namespace PodPilot.App.Services;

/// <summary>
/// Engelse vertalingen. Sleutel = de Nederlandse tekst zoals die in de code of XAML staat
/// (inclusief {0}-plekken). Ontbreekt een tekst, dan toont <see cref="L"/> het Nederlands.
/// Een nieuwe tekst in de app? Zet hem hier ook bij.
/// </summary>
internal static class Translations
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        // --- Algemeen / navigatie ---
        ["Overzicht"] = "Overview",
        ["AirPods"] = "AirPods",
        ["Apparaten"] = "Devices",
        ["Instellingen"] = "Settings",
        ["Sneltoetsen"] = "Hotkeys",
        ["Over"] = "About",
        ["Over PodPilot"] = "About PodPilot",
        ["PodPilot"] = "PodPilot",
        ["Sluiten"] = "Close",
        ["Algemeen"] = "General",
        ["Overig"] = "Other",

        // --- Status en verbinden ---
        ["Verbinden"] = "Connect",
        ["Verbreken"] = "Disconnect",
        ["Verbinding verbreken"] = "Disconnect",
        ["Verbinden…"] = "Connecting…",
        ["Verbinding verbreken…"] = "Disconnecting…",
        ["Automatisch verbinden…"] = "Connecting automatically…",
        ["Bezig…"] = "Working…",
        ["Koppelen…"] = "Pair…",
        ["Verbonden met deze pc"] = "Connected to this PC",
        ["Niet verbonden"] = "Not connected",
        ["Niet gekoppeld met deze pc"] = "Not paired with this PC",
        ["Verbinden met {0}"] = "Connect {0}",
        ["Verbinding verbreken met {0}"] = "Disconnect {0}",
        ["Verbinden met {0}…"] = "Connecting to {0}…",
        ["Automatisch verbinden lukt niet"] = "Automatic connection failed",
        ["Lukt niet; verbind via Instellingen"] = "Failed; connect via Settings",
        ["Verbreken lukt niet; gebruik Instellingen"] = "Disconnect failed; use Settings",
        ["Verbreken lukt niet"] = "Disconnect failed",
        ["Open de case of doe de AirPods in, en probeer opnieuw"] = "Open the case or put the AirPods in, then try again",
        ["Verbinden lukt niet. Staat het apparaat aan en in de buurt?"] = "Couldn't connect. Is the device on and nearby?",
        ["Zoeken… open de case of doe de AirPods in"] = "Searching… open the case or put the AirPods in",
        ["Geen signaal: status onbekend"] = "No signal: status unknown",
        ["Bluetooth staat uit of is niet beschikbaar"] = "Bluetooth is off or not available",
        ["{0}: status onbekend"] = "{0}: status unknown",
        ["Case open · "] = "Case open · ",
        ["bijgewerkt {0:HH:mm:ss}"] = "updated {0:HH:mm:ss}",
        ["Model onbekend"] = "Unknown model",
        ["AirPods (1e generatie)"] = "AirPods (1st generation)",
        ["AirPods (2e generatie)"] = "AirPods (2nd generation)",
        ["AirPods (3e generatie)"] = "AirPods (3rd generation)",

        // --- Onderdelen en batterij ---
        ["Links"] = "Left",
        ["Rechts"] = "Right",
        ["Case"] = "Case",
        ["Koptelefoon"] = "Headphones",
        ["L"] = "L",
        ["R"] = "R",
        ["{0} · in oor"] = "{0} · in ear",
        [" (in oor)"] = " (in ear)",
        ["Laatst bekende waarde"] = "Last known value",
        ["nog < 10 min"] = "< 10 min left",
        ["nog ~{0} min"] = "~{0} min left",
        ["nog ~{0} u"] = "~{0} h left",
        ["nog ~{0} u {1} min"] = "~{0} h {1} min left",
        ["Schatting op basis van je eerdere luistersessies"] = "Estimate based on your earlier listening sessions",
        ["Batterij {0}"] = "Battery {0}",
        ["Batterij {0}%"] = "Battery {0}%",
        ["Links {0} · Rechts {1} · Case {2}"] = "Left {0} · Right {1} · Case {2}",

        // --- Overzicht ---
        ["Snelle instellingen"] = "Quick settings",
        ["Nu aan het afspelen"] = "Now playing",
        // Case- en oordopmeldingen
        ["{0}: case bijna leeg"] = "{0}: case almost empty",
        ["De case staat op {0}%. Leg hem aan de lader."] = "The case is at {0}%. Put it on the charger.",
        ["{0}: case volledig opgeladen"] = "{0}: case fully charged",
        ["De case staat op 100%. Je kunt hem van de lader halen."] = "The case is at 100%. You can take it off the charger.",
        ["De linker oordop ligt al {0} minuten niet in je oor en niet in de case."] =
            "The left earbud has been out of your ear and out of the case for {0} minutes.",
        ["De rechter oordop ligt al {0} minuten niet in je oor en niet in de case."] =
            "The right earbud has been out of your ear and out of the case for {0} minutes.",
        ["Je oordopjes liggen al {0} minuten niet in je oor en niet in de case."] =
            "Your earbuds have been out of your ears and out of the case for {0} minutes.",
        ["{0}: leg je oordopjes terug"] = "{0}: put your earbuds back",
        ["Zo laden ze niet op. Klikt de oordop wel goed vast in de case?"] =
            "They won't charge like this. Does the earbud click into the case properly?",
        ["Melding als de case bijna leeg is"] = "Notify when the case is almost empty",
        ["Zelfde drempel als hierboven. De case is alleen te zien met een oordop erin en de deksel open."] =
            "Same threshold as above. The case is only visible with an earbud inside and the lid open.",
        ["Melding als de case vol is"] = "Notify when the case is full",
        ["Als de case tijdens het opladen op 100% komt."] = "When the case reaches 100% while charging.",
        ["Melding als een oordop los ligt"] = "Notify when an earbud is left out",
        ["Als een oordop 10 minuten niet in je oor en niet in de case zit (dan laadt hij niet op)."] =
            "When an earbud has been out of your ear and out of the case for 10 minutes (it won't charge).",
        // Mediasneltoetsen en profielen
        ["Afspelen/pauzeren"] = "Play/pause",
        ["Voor de speler die bij Nu aan het afspelen staat."] = "For the player shown under Now playing.",
        ["Volgend nummer."] = "Next track.",
        ["Vorig nummer."] = "Previous track.",
        ["Toetscombinatie voor afspelen/pauzeren"] = "Key combination for play/pause",
        ["Sneltoets voor afspelen/pauzeren"] = "Shortcut for play/pause",
        ["Toetscombinatie voor volgende"] = "Key combination for next",
        ["Sneltoets voor volgende"] = "Shortcut for next",
        ["Toetscombinatie voor vorige"] = "Key combination for previous",
        ["Sneltoets voor vorige"] = "Shortcut for previous",
        ["Profielen"] = "Profiles",
        ["Profiel wisselen"] = "Switch profile",
        ["Wisselt tussen Werk en Thuis (zie Instellingen › Profielen)."] = "Switches between Work and Home (see Settings › Profiles).",
        ["Toetscombinatie voor profiel wisselen"] = "Key combination for switching profile",
        ["Sneltoets voor profiel wisselen"] = "Shortcut for switching profile",
        ["Actief profiel"] = "Active profile",
        ["Wat je aanzet bij automatisch verbinden, pauzeren, afspelen, de pop-up en de batterijmelding, onthoudt PodPilot per profiel. Wisselen kan ook via het menu bij het systeemvak of een sneltoets."] =
            "PodPilot remembers your choices for automatic connecting, pausing, playing, the pop-up and the battery notification per profile. You can also switch from the tray menu or with a shortcut.",
        ["Werk"] = "Work",
        ["Thuis"] = "Home",
        ["Profiel: {0}"] = "Profile: {0}",
        ["Profiel: {0} → {1}"] = "Profile: {0} → {1}",
        // Exporteren en importeren
        ["Instellingen exporteren en importeren"] = "Export and import settings",
        ["Bewaar al je instellingen in een bestand, en zet ze terug op deze of een andere pc."] =
            "Save all your settings to a file, and restore them on this or another PC.",
        ["Exporteren"] = "Export",
        ["Importeren"] = "Import",
        ["PodPilot-instellingen"] = "PodPilot settings",
        ["Opgeslagen in {0}"] = "Saved to {0}",
        ["Opslaan lukt niet: {0}"] = "Could not save: {0}",
        ["Dit is geen bestand met PodPilot-instellingen."] = "This is not a PodPilot settings file.",
        ["om {0:HH:mm}"] = "at {0:HH:mm}",
        ["gisteren {0:HH:mm}"] = "yesterday {0:HH:mm}",
        ["{0:d MMM HH:mm}"] = "{0:d MMM HH:mm}",
        ["Laatst bekende waarde: de case geeft zijn batterij alleen door als er een oordop in ligt"] =
            "Last known value: the case only reports its battery when an earbud is inside",
        ["Pop-up op het hoofdscherm"] = "Pop-up on the main display",
        ["Aan: altijd op het hoofdscherm. Uit: op het scherm waar je muis is."] =
            "On: always on the main display. Off: on the display where your mouse is.",
        ["Gepauzeerd"] = "Paused",
        ["Pauzeren"] = "Pause",
        ["Afspelen"] = "Play",
        ["Vorige"] = "Previous",
        ["Volgende"] = "Next",
        ["Case open"] = "Case open",
        ["Oordop in"] = "Earbud in",
        ["Oordop uit"] = "Earbud out",
        ["Automatisch verbinden"] = "Connect automatically",
        ["Media pauzeren"] = "Pause media",

        // --- Tabblad AirPods ---
        ["Verbinden als de case opengaat"] = "Connect when the case opens",
        ["Haalt de audio naar deze pc, ook als de AirPods met je telefoon verbonden waren."] =
            "Brings the audio to this PC, even if the AirPods were connected to your phone.",
        ["Verbinden als je ze indoet"] = "Connect when you put them in",
        ["Zodra de eerste oordop in je oor gaat. Na zelf verbreken pas weer als de case dicht is geweest."] =
            "As soon as the first earbud goes in. After you disconnect yourself, only again once the case has been closed.",
        ["Oordetectie"] = "Ear detection",
        ["Pauzeren als een oordop uitgaat"] = "Pause when an earbud comes out",
        ["Alleen als de AirPods met deze pc verbonden zijn."] = "Only when the AirPods are connected to this PC.",
        ["Hervatten als hij weer in gaat"] = "Resume when it goes back in",
        ["Binnen 5 minuten, en alleen wat PodPilot zelf heeft gepauzeerd."] = "Within 5 minutes, and only what PodPilot paused itself.",
        ["Media"] = "Media",
        ["Afspelen na verbinden"] = "Play after connecting",
        ["Start de speler die het laatst actief was zodra de AirPods verbonden zijn. Speelt er al iets, dan gebeurt er niets."] =
            "Starts the most recently used player once the AirPods are connected. If something is already playing, nothing happens.",
        ["Pauzeren na verbreken"] = "Pause after disconnecting",
        ["Pauzeert alles wat speelt zodra de AirPods loskomen, zodat de muziek niet uit de luidsprekers van de pc komt."] =
            "Pauses everything that is playing as soon as the AirPods disconnect, so the music doesn't come out of the PC speakers.",
        ["Geluid naar AirPods na verbinden"] = "Switch sound to AirPods after connecting",
        ["Maakt de AirPods het standaard-afspeelapparaat van Windows zodra ze verbonden zijn."] =
            "Makes the AirPods the default Windows playback device as soon as they are connected.",
        ["Welke gepairde AirPods gebruikt PodPilot?"] = "Which paired AirPods does PodPilot use?",
        ["Automatisch"] = "Automatic",
        ["Het eerste gepairde AirPods-apparaat"] = "The first paired AirPods",
        ["Er zijn geen gepairde AirPods gevonden. Koppel ze eerst via de Bluetooth-instellingen van Windows."] =
            "No paired AirPods found. Pair them first in the Windows Bluetooth settings.",

        // --- Tabblad Apparaten ---
        ["Alle Bluetooth-koptelefoons, oordopjes en speakers die met deze pc gekoppeld zijn, ook van andere merken."] =
            "All Bluetooth headphones, earbuds and speakers paired with this PC, including other brands.",
        ["Deze AirPods gebruikt PodPilot voor de batterij en de pop-up"] = "PodPilot uses these AirPods for the battery and the pop-up",
        ["Er zijn geen gekoppelde Bluetooth-audioapparaten gevonden. Koppel ze eerst via de Bluetooth-instellingen van Windows."] =
            "No paired Bluetooth audio devices found. Pair them first in the Windows Bluetooth settings.",
        ["Nieuw apparaat koppelen…"] = "Pair a new device…",
        ["Pc als speaker"] = "PC as speaker",
        ["Speel het geluid van je telefoon af via deze pc. Kies je telefoon en klik op Verbinden; de telefoon moet al via Bluetooth met de pc gekoppeld zijn."] =
            "Play your phone's audio through this PC. Choose your phone and click Connect; the phone must already be paired with the PC via Bluetooth.",
        ["Geen telefoon gevonden. Koppel je telefoon via \"Nieuw apparaat koppelen…\" hierboven, en kom dan terug."] =
            "No phone found. Pair your phone with \"Pair a new device…\" above, then come back.",
        ["Speelt af via deze pc: {0}"] = "Playing through this PC: {0}",
        ["Verbinden met {0} lukt niet. Staat Bluetooth op de telefoon aan?"] = "Couldn't connect to {0}. Is Bluetooth on on the phone?",
        ["Streamen van {0} stoppen"] = "Stop streaming from {0}",

        // --- Tabblad Instellingen ---
        ["Starten met Windows"] = "Start with Windows",
        ["PodPilot start bij het aanmelden in het systeemvak."] = "PodPilot starts in the system tray when you sign in.",
        ["Pop-up als de case opengaat"] = "Pop-up when the case opens",
        ["Toont de batterijen als je de case naast de pc opent."] = "Shows the batteries when you open the case near the PC.",
        ["Pop-up onthoudt zijn plek"] = "Pop-up remembers its position",
        ["Aan: blijft staan waar je hem naartoe sleept. Uit: de volgende keer weer rechtsonder."] =
            "On: stays where you drag it. Off: back to the bottom right next time.",
        ["Plek van de pop-up"] = "Pop-up position",
        ["Terug naar rechtsonder"] = "Back to bottom right",
        ["Je hebt de pop-up zelf verplaatst. PodPilot onthoudt die plek."] = "You moved the pop-up yourself. PodPilot remembers that position.",
        ["Rechtsonder. Sleep de pop-up aan de achtergrond om hem ergens anders neer te zetten."] =
            "Bottom right. Drag the pop-up by its background to put it somewhere else.",
        ["Altijd rechtsonder. Slepen kan, maar alleen voor die keer."] = "Always bottom right. You can drag it, but only for that time.",
        ["Systeemvak-icoon"] = "System tray icon",
        ["Stijl"] = "Style",
        ["Oordop met stip"] = "Earbud with dot",
        ["Oordop met getal"] = "Earbud with number",
        ["Getal"] = "Number",
        ["Voorbeeld bij 85%, zoals in de taakbalk (2x vergroot)"] = "Preview at 85%, as in the taskbar (2x enlarged)",
        ["Lettertype"] = "Font",
        ["Grootte"] = "Size",
        ["Klein"] = "Small",
        ["Normaal"] = "Normal",
        ["Groot"] = "Large",
        ["Kleur bij een lichte taakbalk"] = "Color on a light taskbar",
        ["Kleur bij een donkere taakbalk"] = "Color on a dark taskbar",
        ["Automatisch (zwart of wit)"] = "Automatic (black or white)",
        ["Batterijkleur (groen, oranje, rood)"] = "Battery color (green, orange, red)",
        ["Doorzichtig als het geluid niet via de AirPods gaat"] = "Transparent when the sound isn't going through the AirPods",
        ["Zo zie je in één oogopslag dat het geluid bijvoorbeeld uit de laptopluidspreker komt."] =
            "So you can see at a glance that the sound is coming from, for example, the laptop speaker.",
        ["Zichtbaarheid"] = "Visibility",
        ["Zichtbaarheid van het icoon"] = "Icon visibility",
        ["Meldingen"] = "Notifications",
        ["Melding bij lage batterij"] = "Low battery notification",
        ["Eén melding per oordop zodra die op of onder de drempel komt."] = "One notification per earbud as soon as it reaches the threshold.",
        ["Drempel"] = "Threshold",
        ["Thema"] = "Theme",
        ["Hoofdvenster"] = "Main window",
        ["Pop-up bij het systeemvak"] = "Pop-up near the system tray",
        ["Zoals Windows"] = "Like Windows",
        ["Licht"] = "Light",
        ["Donker"] = "Dark",
        ["Eigen achtergrondkleur"] = "Custom background color",
        ["Voor het hoofdvenster en de pop-up. De kleur bepaalt ook of de tekst licht of donker is."] =
            "For the main window and the pop-up. The color also decides whether the text is light or dark.",
        ["Achtergrondkleur als hexcode"] = "Background color as hex code",
        ["Eigen accentkleur"] = "Custom accent color",
        ["Voor schakelaars, knoppen en het actieve tabblad, in plaats van de accentkleur van Windows."] =
            "For switches, buttons and the active tab, instead of the Windows accent color.",
        ["Accentkleur als hexcode"] = "Accent color as hex code",
        ["\"{0}\" is geen geldige kleur. Gebruik zes tekens, bijv. #8B5CF6."] = "\"{0}\" is not a valid color. Use six characters, e.g. #8B5CF6.",
        ["Standaardinstellingen"] = "Default settings",
        ["Zet alle instellingen terug zoals na een nieuwe installatie. \"Starten met Windows\" blijft zoals het is."] =
            "Resets all settings as after a fresh install. \"Start with Windows\" stays as it is.",
        ["Terugzetten"] = "Reset",
        ["Ja, terugzetten"] = "Yes, reset",
        ["Weet je het zeker? Klik nog een keer om alles terug te zetten."] = "Are you sure? Click again to reset everything.",

        // --- Tabblad Sneltoetsen ---
        ["Werken overal in Windows, ook als PodPilot niet op de voorgrond staat. Klik op een combinatie en druk een nieuwe; Esc = annuleren."] =
            "Work anywhere in Windows, even when PodPilot isn't in front. Click a combination and press a new one; Esc = cancel.",
        ["Pop-up"] = "Pop-up",
        ["Pop-up tonen of verbergen"] = "Show or hide the pop-up",
        ["Opent of sluit de pop-up."] = "Opens or closes the pop-up.",
        ["Sneltoets voor de pop-up"] = "Hotkey for the pop-up",
        ["Toetscombinatie"] = "Key combination",
        ["Klik op de knop en druk een nieuwe combinatie."] = "Click the button and press a new combination.",
        ["Toetscombinatie voor de pop-up"] = "Key combination for the pop-up",
        ["Verbinden / verbreken"] = "Connect / disconnect",
        ["Verbindt de AirPods met deze pc, of verbreekt de verbinding als ze al verbonden zijn."] =
            "Connects the AirPods to this PC, or disconnects them if they are already connected.",
        ["Sneltoets voor verbinden en verbreken"] = "Hotkey for connecting and disconnecting",
        ["Toetscombinatie voor verbinden en verbreken"] = "Key combination for connecting and disconnecting",
        ["Geluid"] = "Sound",
        ["Geluidsuitgang wisselen"] = "Switch sound output",
        ["Elke druk gaat naar de volgende geluidsuitgang (AirPods, luidsprekers, ...). Onderin het scherm zie je even welke."] =
            "Each press goes to the next sound output (AirPods, speakers, ...). The bottom of the screen briefly shows which one.",
        ["Sneltoets voor geluidsuitgang wisselen"] = "Hotkey for switching sound output",
        ["Toetscombinatie voor geluidsuitgang wisselen"] = "Key combination for switching sound output",
        ["Druk een combinatie…"] = "Press a combination…",
        ["Esc = annuleren."] = "Esc = cancel.",
        ["Gebruik Ctrl, Alt of Win erbij (of een F-toets)."] = "Add Ctrl, Alt or Win (or use an F key).",
        ["{0} is al in gebruik (door een andere app of een andere sneltoets). Kies een andere combinatie."] =
            "{0} is already in use (by another app or another hotkey). Choose a different combination.",
        ["Geluid via: {0}"] = "Sound via: {0}",
        ["Wisselen naar {0} lukt niet"] = "Couldn't switch to {0}",

        // --- Tabblad Over en updates ---
        ["Versie {0}"] = "Version {0}",
        ["Toont de batterij van je AirPods en regelt de verbinding met deze pc. Geen account: alles blijft op deze computer. Alleen voor het zoeken naar updates vraagt PodPilot bij GitHub de nieuwste versie op."] =
            "Shows your AirPods battery and manages the connection with this PC. No account: everything stays on this computer. Only to look for updates does PodPilot ask GitHub for the latest version.",
        ["Diagnoselog openen"] = "Open diagnostic log",
        ["Map met instellingen"] = "Settings folder",
        ["Bluetooth-instellingen"] = "Bluetooth settings",
        ["Bluetooth-instellingen openen"] = "Open Bluetooth settings",
        ["Updates"] = "Updates",
        ["Nog niet gezocht."] = "Not checked yet.",
        ["Nu controleren"] = "Check now",
        ["Bijwerken"] = "Update",
        ["Bij het opstarten naar updates zoeken"] = "Check for updates at startup",
        ["Is er een nieuwe versie, dan krijg je één melding. Bijwerken installeert hem en start PodPilot opnieuw."] =
            "If there is a new version, you get one notification. Update installs it and restarts PodPilot.",
        ["Zoeken naar updates…"] = "Checking for updates…",
        ["Zoeken lukt niet. Is er internet?"] = "Couldn't check. Is there an internet connection?",
        ["Versie {0} is beschikbaar (je hebt {1})."] = "Version {0} is available (you have {1}).",
        ["Je hebt de nieuwste versie ({0})."] = "You have the latest version ({0}).",
        ["Downloaden…"] = "Downloading…",
        ["Downloaden… {0:P0}"] = "Downloading… {0:P0}",
        ["Bijwerken lukt niet. Probeer het later opnieuw."] = "Update failed. Please try again later.",
        ["Installeren… PodPilot start zo opnieuw."] = "Installing… PodPilot will restart shortly.",

        // --- Tray-menu en meldingen ---
        ["Status tonen"] = "Show status",
        ["PodPilot openen"] = "Open PodPilot",
        ["Afsluiten"] = "Exit",
        ["{0}: batterij bijna leeg"] = "{0}: battery almost empty",
        ["{0}. Leg ze in de case om op te laden."] = "{0}. Put them in the case to charge.",
        ["PodPilot {0} is beschikbaar"] = "PodPilot {0} is available",
        ["Klik hier, of ga naar het tabblad Over, om bij te werken."] = "Click here, or go to the About tab, to update.",

        // --- Taal ---
        ["Taal"] = "Language",
        ["Nederlands"] = "Nederlands",
        ["English"] = "English",
        ["Wordt actief nadat je PodPilot opnieuw start."] = "Takes effect after you restart PodPilot.",
        ["Herstart PodPilot om de nieuwe taal te zien."] = "Restart PodPilot to see the new language.",
    };
}
