using Microsoft.Toolkit.Uwp.Notifications;
using PodPilot.Core.Scanning;
using Windows.UI.Notifications;

namespace PodPilot.App.Services;

/// <summary>
/// Windows-toastmeldingen, verstuurd onder de vaste app-ID van de Startmenu-snelkoppeling
/// (<see cref="StartMenuShortcut"/>). Zo staat PodPilot bij Instellingen > Meldingen en toont
/// Windows ook banners. Het Toolkit-pakket gebruiken we alleen om de melding op te bouwen.
/// </summary>
internal static class NotificationService
{
    /// <summary>Klik op een melding terwijl de app draait. Komt binnen op een achtergrondthread.</summary>
    public static event EventHandler? Clicked;

    /// <summary>Melding "batterij bijna leeg", bijv. "Links 20% · Rechts 10%".</summary>
    public static void ShowLowBattery(string deviceName, IReadOnlyList<LowBatteryItem> items)
    {
        var detail = string.Join(" · ", items.Select(i => $"{L.T(i.Label)} {i.Percent}%"));

        var content = new ToastContentBuilder()
            .AddText(L.T("{0}: batterij bijna leeg", deviceName))
            .AddText(L.T("{0}. Leg ze in de case om op te laden.", detail))
            .GetToastContent();

        Show(content);
    }

    /// <summary>Melding "nieuwe versie beschikbaar". Klik = <paramref name="onClick"/> (achtergrondthread).</summary>
    public static void ShowUpdateAvailable(Version version, Action onClick)
    {
        var content = new ToastContentBuilder()
            .AddText(L.T("PodPilot {0} is beschikbaar", version))
            .AddText(L.T("Klik hier, of ga naar het tabblad Over, om bij te werken."))
            .GetToastContent();

        Show(content, onClick);
    }

    /// <param name="onClick">Wat een klik doet; standaard het <see cref="Clicked"/>-event (pop-up).</param>
    private static void Show(ToastContent content, Action? onClick = null)
    {
        try
        {
            var toast = new ToastNotification(content.GetXml());
            toast.Activated += (_, _) =>
            {
                if (onClick is not null)
                    onClick();
                else
                    Clicked?.Invoke(null, EventArgs.Empty);
            };
            ToastNotificationManager.CreateToastNotifier(StartMenuShortcut.AppUserModelId).Show(toast);
        }
        catch (Exception)
        {
            // Bijv. als meldingen door beleid zijn uitgeschakeld; de pop-up vangt dat op.
        }
    }
}
