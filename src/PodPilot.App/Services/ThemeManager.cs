using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color; // niet System.Drawing.Color (WinForms zit ook in het project)

namespace PodPilot.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Wisselt tussen licht en donker en leest de Windows-instelling. Het hoofdvenster en de pop-up
/// hebben elk een eigen thema: de kleuren komen per venster in Window.Resources. Alle kleuren in de
/// UI zijn DynamicResource, dus een wissel is meteen zichtbaar.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Sleutel waarmee we in Window.Resources onthouden of dat venster licht is (voor de titelbalk).
    private const string IsLightKey = "PodPilot.IsLightTheme";

    /// <summary>Eigen achtergrondkleur voor alle vensters; null = licht/donker uit het thema.</summary>
    public static Color? CustomBackground { get; set; }

    /// <summary>Eigen accentkleur voor alle vensters; null = de accentkleur van Windows.</summary>
    public static Color? CustomAccent { get; set; }

    /// <summary>Leest "#RRGGBB" (of "RRGGBB"). Geeft null bij een ongeldige kleur.</summary>
    public static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return null;

        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>True als Windows-apps een licht thema gebruiken (Instellingen > Persoonlijke instellingen > Kleuren).</summary>
    public static bool IsSystemAppsLight() => ReadFlag("AppsUseLightTheme", defaultValue: true);

    /// <summary>True als de taakbalk licht is. Dan moet het tray-icoon donker zijn.</summary>
    public static bool IsTaskbarLight() => ReadFlag("SystemUsesLightTheme", defaultValue: false);

    /// <summary>
    /// Thema voor de hele app: de standaard voor alles wat geen eigen venster-thema heeft (zoals
    /// tooltips). Index 0 in App.xaml is altijd de kleurendictionary.
    /// </summary>
    public static void Apply(AppTheme theme) =>
        Application.Current.Resources.MergedDictionaries[0] = CreateColors(IsLight(theme));

    /// <summary>Geeft één venster zijn eigen thema (licht/donker) en past de titelbalk aan.</summary>
    public static void ApplyTo(Window window, AppTheme theme)
    {
        var light = IsLight(theme);
        var dictionaries = window.Resources.MergedDictionaries;
        var previous = dictionaries.FirstOrDefault(d => d.Contains(IsLightKey));
        if (previous is not null)
            dictionaries.Remove(previous);

        var colors = CreateColors(light);
        colors[IsLightKey] = light;
        dictionaries.Insert(0, colors);
        ApplyTitleBar(window);
    }

    // Met een eigen achtergrondkleur bepaalt die kleur of het venster licht of donker is.
    private static bool IsLight(AppTheme theme) => CustomBackground is Color background
        ? IsBright(background)
        : theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => IsSystemAppsLight(),
        };

    private static ResourceDictionary CreateColors(bool light)
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(light ? "Light" : "Dark")}.xaml"),
        };

        if (CustomBackground is Color background)
            ApplyBackground(dictionary, background, light);

        if (CustomAccent is Color accent)
            ApplyAccent(dictionary, accent, light ? Shade(accent, Colors.Black, 0.15) : Shade(accent, Colors.White, 0.15));
        else
            ApplyWindowsAccent(dictionary, light);
        return dictionary;
    }

    /// <summary>
    /// Leidt uit één achtergrondkleur de vlakken af: kaarten iets lichter, de navigatie iets
    /// donkerder, lijnen en knoppen gemengd met de tekstkleur. De tekstkleuren komen uit
    /// Light.xaml of Dark.xaml, passend bij hoe licht de achtergrond is.
    /// </summary>
    private static void ApplyBackground(ResourceDictionary d, Color background, bool light)
    {
        var text = ((SolidColorBrush)d["TextPrimary"]).Color;
        var card = light ? Shade(background, Colors.White, 0.65) : Shade(background, Colors.White, 0.06);

        d["PageBackground"] = Frozen(background);
        d["WindowBackground"] = Frozen(light ? Shade(background, Colors.White, 0.4) : Shade(background, Colors.White, 0.04));
        d["CardBackground"] = Frozen(card);
        d["NavBackground"] = Frozen(light ? Shade(background, Colors.Black, 0.04) : Shade(background, Colors.Black, 0.25));
        d["DividerBrush"] = Frozen(Shade(card, text, 0.10));
        d["IconFill"] = Frozen(Shade(card, text, light ? 0.22 : 0.80));
        d["TrackBrush"] = Frozen(Shade(card, text, 0.12));
        d["ButtonBackground"] = Frozen(Shade(card, text, 0.07));
        d["ButtonHoverBackground"] = Frozen(Shade(card, text, 0.12));
    }

    private static void ApplyAccent(ResourceDictionary d, Color accent, Color hover)
    {
        d["AccentBrush"] = Frozen(accent);
        d["AccentHoverBrush"] = Frozen(hover);
        d["AccentForeground"] = Frozen(IsBright(accent) ? Color.FromRgb(0x10, 0x12, 0x16) : Colors.White);
        // Zachte accentkleur voor achtergronden, bijv. het actieve tabblad in de navigatie.
        d["AccentSubtleBrush"] = Frozen(Color.FromArgb(0x2A, accent.R, accent.G, accent.B));
    }

    /// <summary>Mengt kleur a met b; amount 0 = a, 1 = b.</summary>
    private static Color Shade(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // Relatieve helderheid (sRGB, benaderd): boven de helft past zwarte tekst beter dan witte.
    private static bool IsBright(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 150;

    /// <summary>
    /// Maakt de titelbalk van een gewoon venster donker of licht, passend bij het thema van dat
    /// venster. Werkt op Windows 10 20H1+ en Windows 11; op oudere versies gebeurt er niets.
    /// </summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var light = window.TryFindResource(IsLightKey) as bool? ?? IsSystemAppsLight();
        var dark = light ? 0 : 1;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    /// <summary>
    /// Vervangt de vaste accentkleur door die van Windows (Instellingen > Persoonlijke instellingen >
    /// Kleuren). Net als Windows 11 zelf: in licht thema een donkerdere tint, in donker thema een
    /// lichtere, zodat de knop goed afsteekt. Lukt het uitlezen niet, dan blijft het blauw uit het thema.
    /// </summary>
    private static void ApplyWindowsAccent(ResourceDictionary dictionary, bool light)
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            Color Get(Windows.UI.ViewManagement.UIColorType type)
            {
                var c = ui.GetColorValue(type);
                return Color.FromArgb(c.A, c.R, c.G, c.B);
            }

            var accent = Get(light ? Windows.UI.ViewManagement.UIColorType.AccentDark1
                                   : Windows.UI.ViewManagement.UIColorType.AccentLight2);
            var hover = Get(light ? Windows.UI.ViewManagement.UIColorType.AccentDark2
                                  : Windows.UI.ViewManagement.UIColorType.AccentLight1);
            ApplyAccent(dictionary, accent, hover);
        }
        catch (Exception)
        {
            // Oudere Windows of geen WinRT: het vaste blauw uit Light/Dark.xaml blijft staan.
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static bool ReadFlag(string name, bool defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is int value ? value != 0 : defaultValue;
    }
}
