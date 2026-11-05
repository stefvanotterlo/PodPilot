using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PodPilot.App.Services;

namespace PodPilot.App.Views;

/// <summary>
/// De bediening van één sneltoets in het hoofdvenster: een aan/uit-schakelaar en een knop waarmee
/// je de combinatie opneemt (klik, druk de combinatie; Esc = annuleren). De combinatie blijft
/// bewaard als de schakelaar uit staat.
/// </summary>
internal sealed class HotkeyEditor
{
    private readonly CheckBox _toggle;
    private readonly FrameworkElement _row;
    private readonly Button _button;
    private readonly TextBlock _hint;
    private readonly string _hintText;
    private readonly Hotkey _default;
    private readonly Func<bool> _getEnabled;
    private readonly Action<bool> _setEnabled;
    private readonly Func<string?> _getCombination;
    private readonly Action<string?> _setCombination;
    private readonly Func<Hotkey?, bool> _apply;
    private readonly Action _changed;
    private bool _isRecording;

    /// <param name="toggle">Schakelaar aan/uit.</param>
    /// <param name="row">Regel met de knop; alleen bruikbaar als de schakelaar aan staat.</param>
    /// <param name="button">Knop die de combinatie toont en opneemt.</param>
    /// <param name="hint">Uitleg onder de regel; toont ook "al in gebruik".</param>
    /// <param name="defaultHotkey">Combinatie als er nog niets gekozen is.</param>
    /// <param name="apply">Zet de sneltoets in Windows (null = uit); false = al in gebruik.</param>
    /// <param name="changed">Instellingen opslaan en toepassen.</param>
    public HotkeyEditor(CheckBox toggle, FrameworkElement row, Button button, TextBlock hint, Hotkey defaultHotkey,
        Func<bool> getEnabled, Action<bool> setEnabled, Func<string?> getCombination, Action<string?> setCombination,
        Func<Hotkey?, bool> apply, Action changed)
    {
        _toggle = toggle;
        _row = row;
        _button = button;
        _hint = hint;
        _hintText = hint.Text;
        _default = defaultHotkey;
        _getEnabled = getEnabled;
        _setEnabled = setEnabled;
        _getCombination = getCombination;
        _setCombination = setCombination;
        _apply = apply;
        _changed = changed;

        ShowCombination();
        toggle.IsChecked = getEnabled();
        row.IsEnabled = getEnabled();

        // Checked/Unchecked in plaats van Click: dan werkt het ook met het toetsenbord en een schermlezer.
        toggle.Checked += (_, _) => OnToggled();
        toggle.Unchecked += (_, _) => OnToggled();
        button.Click += (_, _) => StartRecording();
        button.PreviewKeyDown += OnKeyDown;
        button.LostKeyboardFocus += (_, _) =>
        {
            // Ergens anders geklikt tijdens het opnemen: de oude combinatie blijft staan.
            if (_isRecording)
                StopRecording(null);
        };
    }

    private Hotkey Saved => Hotkey.Parse(_getCombination()) ?? _default;

    private void ShowCombination() => _button.Content = Saved.ToString();

    private void OnToggled()
    {
        var enabled = _toggle.IsChecked == true;
        _setEnabled(enabled);
        if (_getCombination() is null)
            _setCombination(_default.ToString());
        _row.IsEnabled = enabled;
        _changed();

        ResetHint();
        if (enabled && !_apply(Saved))
            ShowInUse(Saved);
        else if (!enabled)
            _apply(null);
    }

    private void StartRecording()
    {
        if (_isRecording)
            return;

        // Tijdens het opnemen de huidige sneltoets uitzetten, anders vangt Windows hem weg.
        _isRecording = true;
        _apply(null);
        _button.Content = L.T("Druk een combinatie…");
        ResetHint();
        _hint.Text = L.T("Esc = annuleren.");
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording)
            return;

        // Met Alt erbij meldt WPF de toets als Key.System; de echte toets staat dan in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        e.Handled = true;

        if (Hotkey.IsModifierKey(key))
            return; // wacht op de echte toets

        if (key == Key.Escape && modifiers == 0)
        {
            StopRecording(null);
            return;
        }

        var hotkey = new Hotkey(modifiers, key);
        if (!hotkey.IsValid)
        {
            _hint.Text = L.T("Gebruik Ctrl, Alt of Win erbij (of een F-toets).");
            return;
        }

        StopRecording(hotkey);
    }

    /// <param name="newHotkey">De nieuwe combinatie, of null om de bewaarde te houden (annuleren).</param>
    private void StopRecording(Hotkey? newHotkey)
    {
        _isRecording = false;
        ResetHint();

        if (newHotkey is { } hotkey)
        {
            if (!_apply(hotkey))
            {
                // Bezet: de bewaarde combinatie terugzetten.
                _apply(Saved);
                ShowInUse(hotkey);
            }
            else if (_getCombination() != hotkey.ToString())
            {
                _setCombination(hotkey.ToString());
                _changed();
            }
        }
        else
        {
            _apply(Saved);
        }

        ShowCombination();
    }

    /// <summary>Venster dicht tijdens het opnemen: de bewaarde combinatie weer aanzetten.</summary>
    public void Cancel()
    {
        if (_isRecording)
            StopRecording(null);
    }

    private void ResetHint()
    {
        _hint.Text = _hintText;
        _hint.ClearValue(TextBlock.ForegroundProperty);
    }

    private void ShowInUse(Hotkey hotkey)
    {
        _hint.Text = L.T("{0} is al in gebruik (door een andere app of een andere sneltoets). Kies een andere combinatie.", hotkey);
        _hint.Foreground = (System.Windows.Media.Brush)_hint.FindResource("LowBrush");
    }
}
