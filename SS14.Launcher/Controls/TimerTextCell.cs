using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace SS14.Launcher.Controls;

/// <summary>
/// Displays the elapsed time since <see cref="Value"/>, refreshing itself as the clock advances.
/// </summary>
/// <remarks>
/// Ported from the official SS14.Launcher (round time column). Localization was replaced with plain
/// formatting since SGLoader has no localization system.
/// </remarks>
public class TimerTextCell : TemplatedControl
{
    public static readonly DirectProperty<TimerTextCell, DateTime?> ValueProperty =
        AvaloniaProperty.RegisterDirect<TimerTextCell, DateTime?>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v
        );

    public static readonly DirectProperty<TimerTextCell, string> TextProperty =
        AvaloniaProperty.RegisterDirect<TimerTextCell, string>(
            nameof(Text),
            o => o.Text,
            (o, v) => o.Text = v
        );

    private DateTime? _value;
    private bool _attached;

    public DateTime? Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    private string _text = "";

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    private IDisposable? _timer;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            UpdateText();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _attached = true;
        StartTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _attached = false;
        _timer?.Dispose();
    }

    // Trigger an update when the visible timer will roll over to the next minute.
    private void StartTimer()
    {
        _timer?.Dispose();

        // Only start a new timer if we have a DateTime and we're on the visual tree.
        if (_attached && Value is { } dt)
        {
            var ts = DateTime.UtcNow.Subtract(dt);
            // Guard against a round start time in the future (negative elapsed).
            _timer = DispatcherTimer.RunOnce(UpdateText, TimeSpan.FromSeconds(ts.Seconds >= 0 ? ts.Seconds : 60));
        }
    }

    private void UpdateText()
    {
        Text = Value is { } dt ? GetTimeStringSince(dt) : "";
        StartTimer();
    }

    private static string GetTimeStringSince(DateTime dateTime)
    {
        var ts = DateTime.UtcNow.Subtract(dateTime);
        // Math.Floor on total hours so timers gracefully surpass 24h instead of wrapping.
        var hours = (int) Math.Floor(ts.TotalHours);
        var mins = ts.Minutes.ToString().PadLeft(2, '0');
        return hours == 0 ? $"{mins}M" : $"{hours}H {mins}M";
    }
}
