using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PodPilot.App.Services;

/// <summary>
/// Tekent het tray-icoon: een oordop met een gekleurde stip voor de laagste batterij
/// (groen, oranje of rood). Bij een onbekende status is de oordop half doorzichtig en zonder stip.
/// </summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Maakt een icoon. Het handle moet later met <see cref="Destroy"/> worden vrijgegeven,
    /// want <see cref="Icon.FromHandle"/> doet dat niet.
    /// </summary>
    public static (Icon Icon, IntPtr Handle) Render(int? percent, bool lightTaskbar)
    {
        // De grootte van een klein systeemicoon hangt af van de schaal (16 px bij 100%, 24 px bij 150%).
        var size = Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);
        float s = size;

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var foreground = lightTaskbar ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 240, 240);
            if (percent is null)
                foreground = Color.FromArgb(130, foreground);

            using (var brush = new SolidBrush(foreground))
            using (var stem = RoundedRectangle(new RectangleF(s * 0.38f, s * 0.30f, s * 0.20f, s * 0.66f), s * 0.10f))
            {
                // Kop en steeltje van de oordop.
                g.FillEllipse(brush, s * 0.06f, s * 0.02f, s * 0.50f, s * 0.50f);
                g.FillPath(brush, stem);
            }

            if (percent is int p)
            {
                var color = p <= 20 ? Color.FromArgb(229, 72, 77)
                          : p <= 50 ? Color.FromArgb(224, 161, 0)
                          : Color.FromArgb(46, 173, 91);
                var diameter = s * 0.46f;
                var rect = new RectangleF(s - diameter - 0.5f, s - diameter - 0.5f, diameter, diameter);

                // Een rand in de kleur van de taakbalk houdt de stip los van de oordop.
                using var ring = new Pen(lightTaskbar ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32), s * 0.08f);
                using var fill = new SolidBrush(color);
                g.FillEllipse(fill, rect);
                g.DrawEllipse(ring, rect);
            }
        }

        var handle = bitmap.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            DestroyIcon(handle);
    }

    private static GraphicsPath RoundedRectangle(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
