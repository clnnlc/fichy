using System.Drawing;
using System.Drawing.Drawing2D;

namespace VolumeMixer.Services;

/// <summary>Draws the app/tray icon at runtime so no image asset is needed.</summary>
public static class IconFactory
{
    public static Icon CreateAppIcon(int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Rounded accent background
            using var bg = new SolidBrush(Color.FromArgb(124, 92, 255));
            using var path = Rounded(new Rectangle(0, 0, size - 1, size - 1), size / 5);
            g.FillPath(bg, path);

            // Headphones (white) — fits "finally I can hear you"
            using var white = new SolidBrush(Color.White);
            float s = size / 32f;

            // Headband arc
            using var band = new Pen(Color.White, 2.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(band, 7 * s, 8 * s, 18 * s, 17 * s, 180, 180);

            // Ear cups (rounded rectangles at each end of the band)
            using var cupL = Rounded(new Rectangle((int)(6 * s), (int)(16 * s), (int)(5.5f * s), (int)(9 * s)), (int)(2 * s));
            using var cupR = Rounded(new Rectangle((int)(20.5f * s), (int)(16 * s), (int)(5.5f * s), (int)(9 * s)), (int)(2 * s));
            g.FillPath(white, cupL);
            g.FillPath(white, cupR);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
