using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PodPilot.App.Services;

public enum TrayIconStyle
{
    /// <summary>Oordop met een gekleurde stip.</summary>
    Icon,

    /// <summary>Het percentage als getal.</summary>
    Number,

    /// <summary>Een kleinere oordop met rechtsonder klein het percentage.</summary>
    BudWithNumber,
}

public enum TrayFontSize
{
    Small,
    Normal,
    Large,
}

/// <summary>Alles wat bepaalt hoe het tray-icoon eruitziet (uit de instellingen).</summary>
public sealed record TrayIconOptions(
    TrayIconStyle Style,
    string Font,
    TrayFontSize FontSize,
    string ColorLight,
    string ColorDark,
    int DimOpacity)
{
    public static TrayIconOptions From(AppSettings s) =>
        new(s.TrayStyle, s.TrayFont, s.TrayFontSize, s.TrayColorLight, s.TrayColorDark, Math.Clamp(s.TrayDimOpacity, 10, 90));
}

/// <summary>
/// Tekent het tray-icoon. Stijl "Icoon": een oordop met een gekleurde stip voor de laagste batterij
/// (groen, oranje of rood). Stijl "Getal": het percentage. Bij een onbekende status is het icoon
/// half doorzichtig; "gedimd" (geluid niet via de AirPods) maakt het hele icoon doorzichtiger.
/// </summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>De grootte van een klein systeemicoon (16 px bij 100%, 24 px bij 150%).</summary>
    public static int TraySize => Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);

    /// <summary>
    /// Maakt een icoon. Het handle moet later met <see cref="Destroy"/> worden vrijgegeven,
    /// want <see cref="Icon.FromHandle"/> doet dat niet.
    /// </summary>
    public static (Icon Icon, IntPtr Handle) Render(int? percent, bool lightTaskbar, TrayIconOptions options, bool dimmed)
    {
        using var bitmap = RenderBitmap(TraySize, percent, lightTaskbar, options, dimmed);
        var handle = bitmap.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    /// <summary>Tekent het icoon op een bitmap van <paramref name="size"/> pixels (ook voor het voorbeeld).</summary>
    public static Bitmap RenderBitmap(int size, int? percent, bool lightTaskbar, TrayIconOptions options, bool dimmed)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            if (options.Style == TrayIconStyle.Number && percent is int p)
            {
                DrawNumber(g, size, new RectangleF(0, 0, size, size), p, lightTaskbar, options);
            }
            else if (options.Style == TrayIconStyle.BudWithNumber && percent is int q)
            {
                // Kleine oordop linksboven als symbooltje; het getal krijgt bijna driekwart van het vlak,
                // anders is het bij 16 px niet te lezen.
                var state = g.Save();
                g.ScaleTransform(0.58f, 0.58f);
                DrawBud(g, size, percent, lightTaskbar, withDot: false);
                g.Restore(state);
                DrawNumber(g, size, new RectangleF(size * 0.24f, size * 0.26f, size * 0.76f, size * 0.74f), q, lightTaskbar,
                    options, halo: true);
            }
            else
            {
                DrawBud(g, size, percent, lightTaskbar, withDot: true);
            }
        }

        if (dimmed)
            ApplyOpacity(bitmap, options.DimOpacity / 100f);
        return bitmap;
    }

    private static void DrawBud(Graphics g, float s, int? percent, bool lightTaskbar, bool withDot)
    {
        var foreground = AutoColor(lightTaskbar);
        if (percent is null)
            foreground = Color.FromArgb(130, foreground);

        using (var brush = new SolidBrush(foreground))
        using (var stem = RoundedRectangle(new RectangleF(s * 0.38f, s * 0.30f, s * 0.20f, s * 0.66f), s * 0.10f))
        {
            // Kop en steeltje van de oordop.
            g.FillEllipse(brush, s * 0.06f, s * 0.02f, s * 0.50f, s * 0.50f);
            g.FillPath(brush, stem);
        }

        if (withDot && percent is int p)
        {
            var diameter = s * 0.46f;
            var rect = new RectangleF(s - diameter - 0.5f, s - diameter - 0.5f, diameter, diameter);

            // Een rand in de kleur van de taakbalk houdt de stip los van de oordop.
            using var ring = new Pen(lightTaskbar ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32), s * 0.08f);
            using var fill = new SolidBrush(LevelColor(p));
            g.FillEllipse(fill, rect);
            g.DrawEllipse(ring, rect);
        }
    }

    /// <summary>
    /// Het percentage als getal, zo groot mogelijk binnen <paramref name="area"/>. "Klein/Normaal/Groot"
    /// bepaalt hoe hoog de cijfers worden; is het getal dan te breed (bijv. 100), dan wordt het smaller.
    /// </summary>
    /// <param name="halo">Randje in de kleur van de taakbalk, als het getal over de oordop heen ligt.</param>
    private static void DrawNumber(Graphics g, int size, RectangleF area, int percent, bool lightTaskbar,
        TrayIconOptions options, bool halo = false)
    {
        g.TextRenderingHint = size <= 16 ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.AntiAlias;
        var text = percent.ToString();
        var color = ResolveColor(lightTaskbar ? options.ColorLight : options.ColorDark, lightTaskbar, percent);
        var heightFactor = options.FontSize switch
        {
            TrayFontSize.Small => 0.72f,
            TrayFontSize.Large => 1.0f,
            _ => 0.86f,
        };

        using var family = TryFontFamily(options.Font);
        using var path = new GraphicsPath();
        path.AddString(text, family, (int)FontStyle.Bold, size, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // Schalen: eerst naar de gekozen hoogte, en nooit breder dan het vlak. Midden in het vlak.
        var scale = Math.Min(area.Height * heightFactor / bounds.Height, area.Width / bounds.Width);
        using var matrix = new Matrix();
        matrix.Translate(area.X + area.Width / 2f, area.Y + area.Height / 2f);
        matrix.Scale(scale, scale);
        matrix.Translate(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));
        path.Transform(matrix);

        if (halo)
        {
            using var outline = new Pen(lightTaskbar ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32), size * 0.08f)
            {
                LineJoin = LineJoin.Round,
            };
            g.DrawPath(outline, path);
        }

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static FontFamily TryFontFamily(string name)
    {
        try
        {
            return new FontFamily(name);
        }
        catch (ArgumentException)
        {
            return new FontFamily("Segoe UI");
        }
    }

    /// <summary>"auto" = zwart of wit passend bij de taakbalk, "level" = batterijkleur, anders "#RRGGBB".</summary>
    private static Color ResolveColor(string value, bool lightTaskbar, int percent)
    {
        if (value == "level")
            return LevelColor(percent);
        if (value.StartsWith('#') && value.Length == 7
            && int.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return AutoColor(lightTaskbar);
    }

    private static Color AutoColor(bool lightTaskbar) =>
        lightTaskbar ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 240, 240);

    private static Color LevelColor(int p) =>
        p <= 20 ? Color.FromArgb(229, 72, 77)
        : p <= 50 ? Color.FromArgb(224, 161, 0)
        : Color.FromArgb(46, 173, 91);

    /// <summary>Vermenigvuldigt de doorzichtigheid (alfa) van elke pixel.</summary>
    private static void ApplyOpacity(Bitmap bitmap, float opacity)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var i = 3; i < bytes.Length; i += 4)
                bytes[i] = (byte)(bytes[i] * opacity);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
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
