using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace PodPilot.App.Services;

/// <summary>Een apparaat (bijv. een telefoon) dat geluid naar deze pc kan sturen.</summary>
public sealed record StreamSource(string Id, string Name);

/// <summary>
/// "Pc als Bluetooth-speaker": geluid van een gekoppelde telefoon via deze pc afspelen, met de
/// Windows-functie AudioPlaybackConnection (Windows 10 2004 en nieuwer). De verbinding blijft
/// bestaan zolang het verbindingsobject leeft; <see cref="Disconnect"/> sluit hem.
/// </summary>
internal sealed class StreamingService : IDisposable
{
    private AudioPlaybackConnection? _connection;

    /// <summary>De bron waarvan nu gestreamd wordt, of null.</summary>
    public StreamSource? Active { get; private set; }

    /// <summary>De verbinding is veranderd (ook als de telefoon hem zelf verbreekt). Achtergrondthread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Alle gekoppelde apparaten die geluid naar de pc kunnen sturen.</summary>
    public static async Task<IReadOnlyList<StreamSource>> GetSourcesAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(AudioPlaybackConnection.GetDeviceSelector());
            return devices.Select(d => new StreamSource(d.Id, d.Name)).ToList();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"streamen: bronnen opzoeken mislukt: {ex.Message}");
            return [];
        }
    }

    /// <summary>Start het streamen van deze bron. Geeft true als de verbinding open is.</summary>
    public async Task<bool> ConnectAsync(StreamSource source)
    {
        Disconnect();
        try
        {
            var connection = AudioPlaybackConnection.TryCreateFromId(source.Id);
            if (connection is null)
                return false;

            connection.StateChanged += OnStateChanged;
            await connection.StartAsync();
            var result = await connection.OpenAsync();
            DiagnosticLog.Write($"streamen: {source.Name} → {result.Status}");
            if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                connection.StateChanged -= OnStateChanged;
                connection.Dispose();
                return false;
            }

            _connection = connection;
            Active = source;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"streamen: verbinden met {source.Name} mislukt: {ex.Message}");
            return false;
        }
    }

    /// <summary>Stopt het streamen (de telefoon speelt daarna weer zelf af).</summary>
    public void Disconnect()
    {
        if (_connection is null)
            return;

        _connection.StateChanged -= OnStateChanged;
        _connection.Dispose();
        _connection = null;
        DiagnosticLog.Write($"streamen: {Active?.Name} gestopt");
        Active = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>De telefoon verbreekt zelf (buiten bereik, Bluetooth uit): opruimen.</summary>
    private void OnStateChanged(AudioPlaybackConnection sender, object args)
    {
        DiagnosticLog.Write($"streamen: status {sender.State}");
        if (sender.State == AudioPlaybackConnectionState.Closed && sender == _connection)
            Disconnect();
    }

    public void Dispose() => Disconnect();
}
