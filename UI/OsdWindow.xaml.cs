using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace VolumeMixer.UI;

/// <summary>
/// A transient on-screen display shown near the bottom of the screen when a
/// per-program volume hotkey fires. Auto-hides after a short delay.
/// </summary>
public partial class OsdWindow : Window
{
    private const double BarMaxWidth = 260;
    private readonly DispatcherTimer _hideTimer;

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
    }

    public void ShowOverlay(string label, int volumePercent, bool muted, bool notFound,
        string? detail = null)
    {
        LabelText.Text = label;

        // A profile has no single level to draw — show what it did instead.
        if (detail is not null)
        {
            Glyph.Text = "🎚";
            PercentText.Text = detail;
            Fill.Background = (Brush)Application.Current.Resources["AccentBrush"];
            Fill.Width = BarMaxWidth;
            Show();
            Reposition();
            _hideTimer.Stop();
            _hideTimer.Start();
            return;
        }

        if (notFound)
        {
            Glyph.Text = "⚠";
            PercentText.Text = "not playing";
            Fill.Width = 0;
        }
        else if (muted || volumePercent == 0)
        {
            Glyph.Text = "🔇";
            PercentText.Text = muted ? "Muted" : "0%";
            Fill.Width = muted ? BarMaxWidth : 0;
            Fill.Background = muted
                ? (Brush)Application.Current.Resources["DangerBrush"]
                : (Brush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            Glyph.Text = volumePercent > 50 ? "🔊" : "🔉";
            PercentText.Text = $"{volumePercent}%";
            Fill.Background = (Brush)Application.Current.Resources["AccentBrush"];
            Fill.Width = BarMaxWidth * (volumePercent / 100.0);
        }

        Show();
        Reposition();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void Reposition()
    {
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Bottom - ActualHeight - 80;
    }
}
