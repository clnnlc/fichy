using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VolumeMixer.Model;
using VolumeMixer.Services;

namespace VolumeMixer.UI;

public partial class SettingsWindow : Window
{
    private readonly List<VolumeBinding> _bindings = new();
    private readonly List<string> _activeProcesses;

    public SettingsWindow()
    {
        InitializeComponent();

        var s = App.Instance.Settings.Current;

        // Work on copies so "Close" discards unsaved edits.
        foreach (var b in s.Bindings)
        {
            _bindings.Add(new VolumeBinding
            {
                TargetProcess = b.TargetProcess,
                DisplayName = b.DisplayName,
                VolumeUp = b.VolumeUp,
                VolumeDown = b.VolumeDown,
                Mute = b.Mute,
                Step = b.Step,
            });
        }

        OverlayHotkeyBox.Gesture = s.ToggleOverlay;
        AutostartCheck.IsChecked = AutostartService.IsEnabled();
        FocusLostCheck.IsChecked = s.CloseOverlayOnFocusLost;
        StepBox.Text = Math.Round(s.DefaultStep * 100).ToString(CultureInfo.InvariantCulture);

        _activeProcesses = GetActiveProcesses();

        BuildBindingRows();
    }

    private static List<string> GetActiveProcesses()
    {
        using var snap = App.Instance.Audio.GetSessions();
        return snap.Sessions
            .Select(x => x.ProcessName)
            .Where(x => !string.IsNullOrWhiteSpace(x)
                        && !x.Equals("System", StringComparison.OrdinalIgnoreCase)
                        && !x.Equals("audiodg", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void BuildBindingRows()
    {
        BindingsPanel.Children.Clear();
        foreach (var b in _bindings)
            BindingsPanel.Children.Add(BuildRow(b));

        NoBindingsHint.Visibility = _bindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildRow(VolumeBinding b)
    {
        var card = new Border
        {
            Background = (Brush)TryFindResource("CardBrush") ?? Brushes.DimGray,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10),
        };

        var root = new StackPanel();
        card.Child = root;

        // --- Target row ---
        var targetDock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };

        var removeBtn = new Button
        {
            Content = "✕",
            Style = (Style)TryFindResource("FlatButton"),
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Remove",
        };
        removeBtn.Click += (_, _) => { _bindings.Remove(b); BuildBindingRows(); };
        DockPanel.SetDock(removeBtn, Dock.Right);
        targetDock.Children.Add(removeBtn);

        var pickBtn = new Button
        {
            Content = "▼",
            Style = (Style)TryFindResource("FlatButton"),
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Pick an active program",
        };
        DockPanel.SetDock(pickBtn, Dock.Right);
        targetDock.Children.Add(pickBtn);

        var targetBox = new TextBox
        {
            Style = (Style)TryFindResource("DarkTextBox"),
            Text = b.TargetProcess,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        targetBox.TextChanged += (_, _) => b.TargetProcess = targetBox.Text.Trim();
        targetDock.Children.Add(targetBox);

        pickBtn.Click += (_, _) => ShowProcessMenu(pickBtn, targetBox);

        root.Children.Add(new TextBlock
        {
            Text = "Program (process name, e.g. “chrome”, “spotify”)",
            Foreground = (Brush)TryFindResource("SubtleBrush"),
            FontSize = 11.5,
            Margin = new Thickness(2, 0, 0, 4),
        });
        root.Children.Add(targetDock);

        // --- Hotkey controls row ---
        var grid = new Grid();
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(HotkeyCell("Louder", MakeHotkeyBox(b.VolumeUp, g => b.VolumeUp = g), 0));
        grid.Children.Add(HotkeyCell("Quieter", MakeHotkeyBox(b.VolumeDown, g => b.VolumeDown = g), 1));
        grid.Children.Add(HotkeyCell("Mute (optional)", MakeHotkeyBox(b.Mute ?? HotkeyGesture.Empty,
            g => b.Mute = g.IsEmpty ? null : g), 2));

        // Step cell
        var stepPanel = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        stepPanel.Children.Add(new TextBlock
        {
            Text = "Step %",
            Foreground = (Brush)TryFindResource("SubtleBrush"),
            FontSize = 11.5,
            Margin = new Thickness(2, 0, 0, 4),
        });
        var stepBox = new TextBox
        {
            Style = (Style)TryFindResource("DarkTextBox"),
            Text = Math.Round(b.Step * 100).ToString(CultureInfo.InvariantCulture),
            TextAlignment = TextAlignment.Center,
        };
        stepBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(stepBox.Text, out var pct))
                b.Step = Math.Clamp(pct, 1, 100) / 100f;
        };
        stepPanel.Children.Add(stepBox);
        Grid.SetColumn(stepPanel, 3);
        grid.Children.Add(stepPanel);

        root.Children.Add(grid);
        return card;
    }

    private UIElement HotkeyCell(string label, HotkeyBox box, int column)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)TryFindResource("SubtleBrush"),
            FontSize = 11.5,
            Margin = new Thickness(2, 0, 0, 4),
        });
        panel.Children.Add(box);
        Grid.SetColumn(panel, column);
        return panel;
    }

    private HotkeyBox MakeHotkeyBox(HotkeyGesture initial, Action<HotkeyGesture> onCommit)
    {
        var box = new HotkeyBox
        {
            Style = (Style)TryFindResource("FlatButton"),
            Gesture = initial,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.GestureCommitted += (_, _) => onCommit(box.Gesture);
        return box;
    }

    private void ShowProcessMenu(Button anchor, TextBox target)
    {
        var menu = new ContextMenu();
        if (_activeProcesses.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(no active programs)", IsEnabled = false });
        }
        else
        {
            foreach (var p in _activeProcesses)
            {
                var item = new MenuItem { Header = p };
                item.Click += (_, _) => target.Text = p;
                menu.Items.Add(item);
            }
        }
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void AddBinding_Click(object sender, RoutedEventArgs e)
    {
        var step = int.TryParse(StepBox.Text, out var p) ? Math.Clamp(p, 1, 100) / 100f : 0.05f;
        _bindings.Add(new VolumeBinding { Step = step });
        BuildBindingRows();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Instance.Settings.Current;

        if (!OverlayHotkeyBox.Gesture.IsEmpty)
            s.ToggleOverlay = OverlayHotkeyBox.Gesture;

        s.CloseOverlayOnFocusLost = FocusLostCheck.IsChecked == true;

        if (int.TryParse(StepBox.Text, out var pct))
            s.DefaultStep = Math.Clamp(pct, 1, 100) / 100f;

        // Keep only bindings that have a target and at least one hotkey.
        s.Bindings = _bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.TargetProcess)
                        && (!b.VolumeUp.IsEmpty || !b.VolumeDown.IsEmpty || (b.Mute is not null && !b.Mute.IsEmpty)))
            .ToList();

        bool wantAutostart = AutostartCheck.IsChecked == true;
        if (wantAutostart != AutostartService.IsEnabled())
            AutostartService.SetEnabled(wantAutostart);
        s.Autostart = AutostartService.IsEnabled();
        AutostartCheck.IsChecked = s.Autostart;

        App.Instance.Settings.Save();
        var failed = App.Instance.RebuildHotkeys();

        if (failed.Count > 0)
        {
            StatusText.Foreground = (Brush)TryFindResource("DangerBrush");
            StatusText.Text = $"Saved, but already in use: {string.Join(", ", failed)}";
        }
        else
        {
            StatusText.Foreground = (Brush)TryFindResource("SubtleBrush");
            StatusText.Text = $"Saved • {DateTime.Now:HH:mm:ss} • {s.Bindings.Count} per-app hotkey(s) active";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
