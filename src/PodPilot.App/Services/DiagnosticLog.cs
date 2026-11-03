using System.IO;

namespace PodPilot.App.Services;

/// <summary>
/// Eenvoudig logbestand voor het opsporen van problemen: %LocalAppData%\PodPilot\podpilot.log.
/// Het bestand wordt bij elke start van de app opnieuw begonnen, zodat het klein blijft.
/// </summary>
internal static class DiagnosticLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PodPilot", "podpilot.log");

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, $"PodPilot gestart {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Loggen is een hulpmiddel; de app moet ook zonder werken.
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }
}
