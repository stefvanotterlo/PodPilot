# PodPilot – technische documentatie

Stappenplan, projectstructuur, het protocol en hoe je bouwt. Voor het installeren van de app zelf:
zie de [README](../README.md).

Een Windows-app (Windows 10/11) die AirPods via Bluetooth verbindt en de batterijstatus
toont. Het ontwerp is eigen werk: er zitten geen logo's of afbeeldingen van Apple of MagicPods in.

## Stappenplan

| Stap | Inhoud | Status |
|---|---|---|
| 1 | **Core + consolescanner**: BLE-advertenties uitlezen, Proximity Pairing decoderen, model herkennen, filteren op RSSI, gepairde apparaten tonen | ✅ |
| 2 | **WPF-app-skelet**: systeemvak-icoon met tooltip, pop-up rechtsonder met linker oordop, rechter oordop en case (eigen vectoricoontjes), licht/donker thema | ✅ |
| 3 | **Verbinden/verbreken**: via de Kernel Streaming-"one-shot reconnect" op het audio-endpoint, met als fallback `ms-settings:bluetooth` | ✅ (zie beperking Intel Smart Sound) |
| 4 | **Automatische pop-up** als de case opengaat (deksel van dicht naar open, of een nieuwe advertentie met open deksel) | ✅ |
| 5 | **Meldingen bij lage batterij** (Windows-toastmelding, drempel instelbaar in `%AppData%\PodPilot\settings.json`; test: `PodPilot.exe --test-notification`) | ✅ |
| 6 | **Instellingenvenster**: starten met Windows, thema, drempel, keuze uit gepairde AirPods. Instellingen in JSON in `%AppData%\PodPilot` | ✅ |
| 7 | **Optioneel: automatisch pauzeren** als een oordop uit het oor gaat (via `GlobalSystemMediaTransportControlsSessionManager`) | ✅ |
| 8 | **Publiceren**: één losse .exe en een installer (Inno Setup) | ✅ |
| 9 | **ANC-modus wisselen** (ruisonderdrukking / transparantie / adaptief / uit) | ⛔ onderzocht, geblokkeerd (zie hieronder) |
| 10 | **Automatisch verbinden** als de case opengaat en/of de eerste oordop in gaat (twee schakelaars, standaard uit) | ✅ getest: "oordop in" ~7 s, "case open" ~3 s tot verbonden |
| 11 | **Oor-detectie in de UI**: "Links · in oor" in de pop-up, "(in oor)" in de tooltip; fix voor statusbit `0x40` | ✅ |
| 12 | **Sneltoets** om de pop-up te tonen/verbergen (aan/uit + eigen combinatie, standaard uit) | ✅ |
| 13 | **Resterende luistertijd** in de pop-up, uit 30 dagen batterijgeschiedenis | 🧪 gebouwd; schatting verschijnt pas na ≥ 2 gemeten stappen van 10% |
| 14 | **Hoofdvenster** (breed, navigatie links): Overzicht (grote pop-up + snelle instellingen), AirPods, Instellingen, Over; eigen kleur-illustratie; accent van Windows; thema hoofdvenster en pop-up apart | ✅ gebouwd, screenshots licht/donker gecontroleerd |

### Stap 13: resterende luistertijd
`BatteryHistory` (Core) schrijft bij elke wijziging (percentage, in oor, laden) een regel naar
`%LocalAppData%\PodPilot\battery-history.csv`, plus een onderbreking als het signaal wegvalt. Gemeten
wordt de duur van één stap van 10% tussen twee opeenvolgende dalingen, terwijl de oordop in het oor
zit en niet laadt (3–90 min, anders genegeerd). De schatting = mediaan van de laatste 20 stappen ×
het aantal resterende stappen, min de tijd die al op het huidige niveau is verstreken. In de pop-up
staat hij onder de batterijbalk ("nog ~2 u 10 min"), alleen bij een oordop in het oor die niet laadt.
Regels ouder dan 30 dagen worden bij het starten opgeruimd.

### Stap 10: automatisch verbinden
Twee instellingen (`AutoConnectOnLidOpen`, `AutoConnectOnEarInsertion`) gebruiken dezelfde verbindroute
als de knop. Er wordt niet verbonden als de audio al verbonden is of als er al een poging loopt. Ook
niet binnen 30 s na de vorige automatische poging, of nadat je zelf op "Verbinding verbreken" hebt
geklikt. Dat blijft zo tot de AirPods uit beeld zijn geweest (case dicht). Mislukt het automatisch
verbinden, dan opent de app de Bluetooth-instellingen niet. In het log staan regels met
`auto-verbinden (…)`.

### Stap 9: waarom ANC wisselen (nog) niet kan
De ANC-modus gaat via Apple's AAP-protocol over een klassiek Bluetooth-**L2CAP**-kanaal (PSM `0x1001`),
niet via GATT. Windows laat gebruikerscode geen L2CAP-kanaal openen: Winsock kent alleen RFCOMM en
WinRT heeft geen L2CAP-API. Alle Windows-apps die het wel kunnen (MagicPods, Pods) gebruiken een
**kerneldriver** (`MagicAAP.sys`, afgeleid van Microsofts BTHECHO-voorbeeld). Die driver is niet
door Microsoft ondertekend: je hebt testmodus nodig of een "community-signed" versie.

Op deze laptop (Windows Enterprise, BitLocker, Secure Boot, Credential Guard/HVCI actief, geen
testsigning) kan dat niet zonder de beveiliging uit te zetten. Mogelijke routes voor later:
- Een eigen KMDF-driver (BTHECHO-basis) met **attestation signing** via Microsoft Partner Center.
  Daarvoor is een EV-codesigning-certificaat nodig (kost geld, op naam van een bedrijf). Dit is veel werk.
- Een losse USB-Bluetooth-dongle met een eigen stack in gebruikersmodus. Dan loopt de audio niet meer
  via Windows. Niet praktisch.

### Waarom WPF en niet WinUI 3?
- Een app die in het systeemvak draait, heeft een tray-icoon nodig. WPF heeft daar goede
  ondersteuning voor (`H.NotifyIcon.Wpf` of WinForms `NotifyIcon`). WinUI 3 heeft dat niet ingebouwd.
- WPF draait zonder MSIX-pakket en zonder Windows App SDK-runtime, dus het levert makkelijk één losse .exe op.
- Een modern uiterlijk (afgeronde hoeken, Mica-achtige achtergrond, licht/donker) is in WPF goed na te bouwen.
- De WinRT-Bluetooth-API's werken in WPF op dezelfde manier, via de target framework `net8.0-windows10.0.19041.0`.

## Projectstructuur

```
PodPilot/
├─ PodPilot.sln
├─ README.md
├─ installer/
│  ├─ build.ps1                      Stap 8: publiceren + installer bouwen
│  └─ PodPilot.iss                   Inno Setup-script
└─ src/
   ├─ PodPilot.Core/                 Klassenbibliotheek zonder UI (later ook gebruikt door de app)
   │  ├─ Protocol/
   │  │  ├─ AppleContinuity.cs       Constanten (company ID 0x004C, berichttype 0x07)
   │  │  └─ ProximityPairingMessage.cs  Decoderen van de ruwe bytes
   │  ├─ Models/
   │  │  ├─ AirPodsModels.cs         Model-ID → naam (AirPods 1/2/3/4, Pro 1/2, Max)
   │  │  └─ AirPodsStatus.cs         Status voor de UI: links/rechts/case, deksel, RSSI
   │  ├─ Scanning/
   │  │  └─ AirPodsScanner.cs        BluetoothLEAdvertisementWatcher + filtering + "onbekend"-timeout
   │  └─ Devices/
   │     └─ PairedDeviceService.cs   Gepairde Apple-audioapparaten + hun product-ID
   ├─ PodPilot.ConsoleScanner/       Stap 1: testtool voor de console
   │  └─ Program.cs
   └─ PodPilot.App/                  (stap 2+) WPF-app: tray, pop-up, instellingen
```

## Zo werkt het uitlezen van de batterij

AirPods zenden ongeveer elke 0,1 tot 1 seconde een BLE-advertentie uit met Apple-fabrikantdata
(company ID `0x004C`). Daarin zit een TLV-bericht van het type `0x07` (Proximity Pairing):

| Offset (payload) | Inhoud |
|---|---|
| 0 | prefix (meestal `0x01`) |
| 1–2 | model-ID, little endian (`0x2014` = AirPods Pro 2) |
| 3 | status: `0x20` = advertentie komt van de **linker** oordop, `0x02` = deze oordop in het oor, `0x08` = de andere in het oor. `0x40` = de uitzendende oordop ligt in de case; dan zijn `0x02` en `0x08` omgedraaid |
| 4 | batterij: lage nibble = uitzendende oordop, hoge nibble = de andere (0–10 → 0–100 %, 15 = onbekend) |
| 5 | hoge nibble = laadvlaggen (deze oordop / andere / case), lage nibble = batterij case |
| 6 | deksel: bit `0x08` = dicht |
| 7 | kleur |
| 8+ | versleuteld (hierin staan exacte percentages, maar zonder de sleutel van het apparaat zijn die niet te lezen) |

De twee oordopjes zenden om de beurt uit. Daarom wordt de "links/rechts-vlag" (`0x20`) gebruikt om
"deze/andere" om te zetten naar links/rechts.

**Andermans AirPods filteren:** in de buurt zijn misschien meer AirPods. De scanner:
1. negeert advertenties met een RSSI onder de drempel (standaard −70 dBm),
2. accepteert alleen het model-ID van de gepairde AirPods. Het product-ID (PID) van een gepaird
   apparaat staat in de Windows-hardware-ID (`BTHENUM\…_VID&0001004C_PID&2014`) en is hetzelfde
   als het model-ID in de advertentie,
3. blijft bij één set en wisselt alleen als een andere set duidelijk sterker is of de huidige een
   paar seconden stil blijft.

Een rechtstreekse koppeling op MAC-adres kan niet: AirPods adverteren met een wisselend, willekeurig
BLE-adres dat niets met het klassieke Bluetooth-adres te maken heeft. Twee identieke modellen naast
elkaar zijn daarom alleen op signaalsterkte uit elkaar te houden.

## Verbinden: aanpak en beperkingen (stap 3)

Windows heeft geen openbare "verbind dit audioapparaat nu"-API voor desktopapps.
`BluetoothDevice.FromIdAsync` opent alleen een object. `AudioPlaybackConnection` werkt de andere
kant op (dan is de pc de luidspreker). De opties, van meest naar minst betrouwbaar:

1. **KS one-shot reconnect (gekozen)**: via `IMMDeviceEnumerator` de audio-endpoints van de
   AirPods zoeken, dan via `IDeviceTopology` naar het Bluetooth-KS-filter, en daarop met
   `IKsControl` de property `KSPROPERTY_ONESHOT_RECONNECT` of `KSPROPERTY_ONESHOT_DISCONNECT` zetten.
   Dit doet Windows zelf ook als je in het geluidsmenu op "Verbinden" klikt.
   - Er zijn geen beheerdersrechten voor nodig.
   - Het werkt alleen voor apparaten die al gepaird zijn en waarvan Windows een audio-endpoint
     heeft. Na "Apparaat verwijderen" moet je opnieuw pairen.
   - De AirPods moeten wakker zijn: uit de case of met de deksel open. Als ze in een gesloten
     case zitten, mislukt het verbinden.
   - Als de AirPods actief met een iPhone/Mac verbonden zijn, kan het even duren of mislukken.
     Apple's automatisch wisselen werkt niet met Windows.
2. **`BluetoothSetServiceState`** (A2DP/HFP-service uit- en aanzetten): dit werkt, maar het is traag.
   De driver wordt opnieuw geïnstalleerd en soms verdwijnen de geluidsinstellingen.
   Deze optie gebruiken we niet.
3. **Fallback**: als 1 mislukt, opent de app `ms-settings:bluetooth`.

**Hoe de app het doet:** eerst forceert de app met een SDP-query zonder cache een Bluetooth-verbinding
(`DeviceConnectionMonitor.WakeAsync`). Daarna stuurt hij de KS one-shot reconnect en wacht hij
maximaal 10 seconden tot het audio-endpoint **actief** wordt. "Verbonden" in de app betekent dus dat
er geluid kan worden afgespeeld, niet alleen dat er een Bluetooth-verbinding is. Lukt het niet, dan
opent de app de Bluetooth-instellingen.

**Intel Smart Sound Technology for Bluetooth audio (offload).** Op laptops waar Bluetooth-audio via
de Intel-audiodriver loopt (endpoint-filter `intcbttoporender`), neemt die driver een
**verbind**verzoek aan, maar doet er niets mee. De app stuurt het verzoek daarom ook rechtstreeks naar
het KS-filter van de Microsoft-A2DP-driver (`\\?\BTHENUM#{0000110b-…}…\SRC`, zie
`KsFilterInterfaces.cs`). Op een HP-laptop met Intel SST is getest dat verbinden en verbreken zo allebei
werken. Verbreken loopt via het Intel-filter. Dat filter reageert daar wel op.

Diagnose: `dotnet run --project src/PodPilot.ConsoleScanner -- --connect --wake` toont per endpoint welk
filter is aangesproken en met welk resultaat.

## Bouwen en draaien

Vereisten: Windows 10 (19041 of nieuwer) of Windows 11, Bluetooth 4.0+ (BLE) en de
**.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`), of Visual Studio 2022 met de workload
".NET desktop development".

```bash
cd PodPilot
dotnet run --project src/PodPilot.ConsoleScanner
```

De app zelf (icoon in het systeemvak; `--show` opent de pop-up meteen):

```bash
dotnet run --project src/PodPilot.App -- --show
```

Opties voor de scanner:

| Optie | Betekenis |
|---|---|
| `--rssi -80` | andere signaaldrempel (dichter bij 0 = strenger) |
| `--any-model` | niet filteren op het model van de gepairde AirPods |
| `--verbose` | ook genegeerde advertenties tonen, met reden en ruwe bytes |

Testen: open de case naast de pc, of doe de AirPods in. Binnen een paar seconden verschijnt een
regel met model, percentages en deksel- en oorstatus.

### Rechten en capabilities
- **Gewone desktop-exe (onze standaard):** er zijn geen manifest-capabilities en geen
  beheerdersrechten nodig. Bluetooth moet aan staan.
- **Als je later toch MSIX gebruikt:** zet `<DeviceCapability Name="bluetooth" />` in
  `Package.appxmanifest` en laat de gebruiker toegang geven bij Privacy > Andere apparaten.
- Toastmeldingen van een niet-verpakte app hebben een AUMID nodig **met een snelkoppeling in het
  Startmenu**. Zonder snelkoppeling komen meldingen alleen in het Meldingencentrum: de app staat dan
  niet bij Instellingen > Meldingen en er komen geen banners. PodPilot maakt de snelkoppeling
  `PodPilot.lnk` (AUMID `PodPilot.App`) bij het opstarten zelf aan (`StartMenuShortcut.cs`). De
  eerste melding direct na het aanmaken kan nog stil blijven. Daarna werkt het.

### Losse .exe en installer (stap 8)
Alles in één keer (Inno Setup 6 nodig: `winget install JRSoftware.InnoSetup`):
```bash
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```
Dat doet twee dingen:
1. **Losse exe** in `publish\PodPilot.exe` (ongeveer 75 MB, gecomprimeerd; er is geen
   .NET-installatie nodig). Alleen deze stap: `build.ps1 -SkipInstaller`, of zelf
   `dotnet publish src/PodPilot.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish`.
   Met `--self-contained false` is hij een paar MB, maar dan moet de .NET 8 Desktop Runtime
   geïnstalleerd zijn.
2. **Installer** `installer\output\PodPilot-Setup-<versie>.exe` (ongeveer 70 MB). De versie komt uit
   `<Version>` in `PodPilot.App.csproj`.

Wat de installer doet:
- Installeert per gebruiker in `%LocalAppData%\Programs\PodPilot`, zonder beheerdersrechten.
- Maakt de Startmenu-snelkoppeling `PodPilot.lnk` met AUMID `PodPilot.App` (dezelfde die de app zelf
  bijwerkt), optioneel een bureaubladsnelkoppeling.
- Taak "starten bij het aanmelden": dezelfde HKCU Run-waarde als het instellingenvenster. Bij een
  update wordt de vorige keuze onthouden.
- Sluit een draaiende PodPilot bij updaten en verwijderen.
- Verwijderen ruimt de exe, de snelkoppeling, de opstartregel en het diagnoselog op. De instellingen in
  `%AppData%\PodPilot` blijven staan.

Stil installeren: `PodPilot-Setup-1.0.0.0.exe /VERYSILENT /TASKS=startup`.

De exe is niet ondertekend, dus SmartScreen waarschuwt bij de eerste keer ("Meer info" >
"Toch uitvoeren").
