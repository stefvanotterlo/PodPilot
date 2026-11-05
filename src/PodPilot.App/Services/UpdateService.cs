using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PodPilot.App.Services;

/// <summary>Een nieuwere versie op GitHub.</summary>
/// <param name="DownloadUrl">Directe link naar PodPilot-Setup.exe in de release.</param>
/// <param name="PageUrl">De releasepagina (voor als downloaden niet lukt).</param>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string PageUrl);

/// <summary>
/// Zoekt op GitHub naar een nieuwere versie van PodPilot en installeert die. Er wordt alleen de
/// openbare informatie van de nieuwste release opgevraagd; er gaat niets over de gebruiker mee.
/// </summary>
internal static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/stefvanotterlo/PodPilot/releases/latest";
    private const string InstallerName = "PodPilot-Setup.exe";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>De versie die nu draait (major.minor.build).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>
    /// Vraagt de nieuwste release op. Geeft die terug als hij nieuwer is dan de versie die draait,
    /// anders null. Gooit een exception als GitHub niet bereikbaar is.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var response = await Http.GetAsync(LatestReleaseApi);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        // Tags heten "v1.3.1".
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            return null;
        latest = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));

        var download = root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == InstallerName)
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault();
        var page = root.GetProperty("html_url").GetString() ?? "";

        DiagnosticLog.Write($"updates: nieuwste op GitHub {latest}, deze versie {CurrentVersion}");
        return latest > CurrentVersion && download is not null ? new UpdateInfo(latest, download, page) : null;
    }

    /// <summary>
    /// Downloadt de installer en start de update: een verborgen opdrachtregel installeert stil en
    /// start daarna de nieuwe PodPilot. De aanroeper moet de app daarna afsluiten, zodat de
    /// installer de exe kan vervangen.
    /// </summary>
    /// <param name="progress">Voortgang van de download, 0..1 (als de grootte bekend is).</param>
    public static async Task StartInstallAsync(UpdateInfo update, IProgress<double>? progress = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PodPilot-update");
        Directory.CreateDirectory(folder);
        var installer = Path.Combine(folder, $"PodPilot-Setup-{update.Version}.exe");

        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(installer);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total is > 0)
                    progress?.Report((double)done / total.Value);
            }
        }

        // De installer installeert per gebruiker in %LocalAppData%\Programs\PodPilot.
        var installedExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PodPilot", "PodPilot.exe");
        DiagnosticLog.Write($"updates: installeren van {update.Version} gestart");

        // Stil installeren (vorige keuzes zoals "starten met Windows" blijven) en daarna opnieuw starten.
        var command = $"/c \"\"{installer}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART && start \"\" \"{installedExe}\"\"";
        Process.Start(new ProcessStartInfo("cmd.exe", command)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub vraagt een User-Agent; een algemene naam, geen gegevens van de gebruiker.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PodPilot-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
