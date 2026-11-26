using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace ScrollKeeper.Presentation.Behaviors;

/// <summary>
/// Attached behavior that turns a <see cref="TextBox"/> into an integer box:
/// only digits can be typed or pasted (and a leading minus sign when <see cref="MinimumProperty"/> is negative), and
/// Up/Down arrow keys change the value by one (by 100 with Shift).
/// </summary>
public static class NumericInput
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(NumericInput));

    /// <summary>Upper limit used by the Up arrow key.</summary>
    public static readonly AttachedProperty<int> MaximumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int>("Maximum", typeof(NumericInput), int.MaxValue);

    /// <summary>Lower limit used by the Down arrow key; a negative value allows a minus sign.</summary>
    public static readonly AttachedProperty<int> MinimumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int>("Minimum", typeof(NumericInput));

    static NumericInput()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(TextBox element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TextBox element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static int GetMaximum(TextBox element) => element.GetValue(MaximumProperty);

    public static void SetMaximum(TextBox element, int value) => element.SetValue(MaximumProperty, value);

    public static int GetMinimum(TextBox element) => element.GetValue(MinimumProperty);

    public static void SetMinimum(TextBox element, int value) => element.SetValue(MinimumProperty, value);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            textBox.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            textBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            textBox.AddHandler(TextBox.PastingFromClipboardEvent, OnPasting);
            InputMethod.SetIsInputMethodEnabled(textBox, false);
        }
        else
        {
            textBox.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            textBox.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            textBox.RemoveHandler(TextBox.PastingFromClipboardEvent, OnPasting);
            textBox.ClearValue(InputMethod.IsInputMethodEnabledProperty);
        }
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox textBox || e.Text is not { } text)
            return;

        e.Handled = !(IsDigits(text) || (text == "-" && CanTypeMinus(textBox)));
    }

    /// <summary>A minus sign is allowed at the start of a box that takes negative values, once.</summary>
    private static bool CanTypeMinus(TextBox textBox)
    {
        var (start, end) = Selection(textBox);
        var text = textBox.Text ?? string.Empty;
        return GetMinimum(textBox) < 0
               && start == 0
               && !text.Remove(start, end - start).Contains('-');
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                break;

            case Key.Up or Key.Down:
                var current = int.TryParse(textBox.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0;
                var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 100L : 1L;
                var next = (int)Math.Clamp(e.Key == Key.Up ? current + step : current - step, GetMinimum(textBox), GetMaximum(textBox));
                textBox.Text = next.ToString(CultureInfo.InvariantCulture);
                textBox.CaretIndex = textBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The clipboard is read asynchronously, so the stock paste is cancelled and a valid number is inserted here instead.
    /// </summary>
    private static async void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        e.Handled = true;

        try
        {
            if (TopLevel.GetTopLevel(textBox)?.Clipboard is not { } clipboard
                || await clipboard.TryGetTextAsync() is not { } pasted)
                return;

            var trimmed = pasted.Trim();
            var valid = IsDigits(trimmed)
                        || (trimmed.StartsWith('-') && IsDigits(trimmed[1..]) && CanTypeMinus(textBox));
            if (!valid)
                return;

            var (start, end) = Selection(textBox);
            var text = textBox.Text ?? string.Empty;
            var result = string.Concat(text.AsSpan(0, start), trimmed, text.AsSpan(end));
            if (textBox.MaxLength > 0 && result.Length > textBox.MaxLength)
                return;

            textBox.Text = result;
            textBox.CaretIndex = start + trimmed.Length;
            textBox.ClearSelection();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The clipboard is busy (another application holds it): ignore the paste.
        }
    }

    private static (int Start, int End) Selection(TextBox textBox)
    {
        var length = textBox.Text?.Length ?? 0;
        var start = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, length);
        var end = Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), 0, length);
        return (start, end);
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
