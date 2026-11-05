using System.Globalization;
using System.Windows.Markup;

namespace PodPilot.App.Services;

public enum AppLanguage
{
    /// <summary>Nederlands als Windows Nederlands is, anders Engels.</summary>
    System,
    Dutch,
    English,
}

/// <summary>
/// Vertalingen. De Nederlandse tekst is zelf de sleutel: <c>L.T("Verbinden")</c> geeft in het
/// Engels "Connect". Ontbreekt een vertaling, dan blijft de Nederlandse tekst staan (nooit leeg).
/// De taal wordt bij het opstarten gekozen; wisselen werkt na herstarten.
/// </summary>
public static class L
{
    /// <summary>True als de app in het Engels draait.</summary>
    public static bool IsEnglish { get; private set; }

    /// <summary>Kiest de taal; één keer bij het opstarten, vóór de vensters worden gemaakt.</summary>
    public static void Init(AppLanguage language)
    {
        IsEnglish = language switch
        {
            AppLanguage.Dutch => false,
            AppLanguage.English => true,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "nl",
        };
    }

    /// <summary>De tekst in de gekozen taal.</summary>
    public static string T(string dutch) =>
        IsEnglish && Translations.English.TryGetValue(dutch, out var english) ? english : dutch;

    /// <summary>Tekst met plekken voor waarden, bijv. <c>L.T("Geluid via: {0}", naam)</c>.</summary>
    public static string T(string dutch, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(dutch), args);
}

/// <summary>
/// Vertaalde tekst in XAML: <c>Text="{l:T Verbinden}"</c>, of met komma's
/// <c>Text="{l:T 'Klein, normaal of groot'}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
