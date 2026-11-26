using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;

namespace ScrollKeeper.Presentation.Documents;

/// <summary>
/// Shows ESOUI BBCode as themed, selectable text (put it in a <see cref="ScrollViewer"/>):
/// <c>&lt;documents:BbCodeView Text="{Binding Description}" /&gt;</c>.
/// </summary>
public sealed class BbCodeView : Decorator
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<BbCodeView, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
            Child = Text is { } text ? BbCodeDocumentBuilder.Build(text, this) : null;
    }
}

/// <summary>
/// A paragraph of a BBCode document: selectable text whose links open in the browser when clicked
/// (Avalonia text has no hyperlink element, so links are spans found by hit testing).
/// </summary>
internal sealed class BbParagraph : SelectableTextBlock
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly List<(Span Span, Uri Uri)> _links = [];
    private Span? _hoveredLink;

    /// <summary>Keeps the stock look and behavior of <see cref="SelectableTextBlock"/>.</summary>
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public void AddLink(Span span, Uri uri) => _links.Add((span, uri));

    /// <summary>Lays the text out again, e.g. once an inline picture has been downloaded.</summary>
    public void Refresh() => InvalidateTextLayout();

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_links.Count == 0)
            return;

        var link = HitLink(e.GetPosition(this));
        if (ReferenceEquals(link?.Span, _hoveredLink))
            return;

        // Underline on hover, like a web link.
        if (_hoveredLink is not null)
            _hoveredLink.TextDecorations = null;
        _hoveredLink = link?.Span;

        if (link is { } hovered)
        {
            hovered.Span.TextDecorations = Avalonia.Media.TextDecorations.Underline;
            Cursor = HandCursor;
            ToolTip.SetTip(this, hovered.Uri.AbsoluteUri);
        }
        else
        {
            ClearValue(CursorProperty);
            ClearValue(ToolTip.TipProperty);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredLink is null)
            return;

        _hoveredLink.TextDecorations = null;
        _hoveredLink = null;
        ClearValue(CursorProperty);
        ClearValue(ToolTip.TipProperty);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // A drag selects text; a plain click opens the link under the pointer.
        if (e.InitialPressMouseButton != MouseButton.Left || SelectionStart != SelectionEnd)
            return;

        if (HitLink(e.GetPosition(this)) is { } link)
        {
            BbCodeDocumentBuilder.OpenInBrowser(link.Uri);
            e.Handled = true;
        }
    }

    private (Span Span, Uri Uri)? HitLink(Point point)
    {
        if (Inlines is null)
            return null;

        var hit = TextLayout.HitTestPoint(new Point(point.X - Padding.Left, point.Y - Padding.Top));
        if (!hit.IsInside)
            return null;

        foreach (var link in _links)
        {
            var offset = 0;
            if (TryLocate(Inlines, link.Span, ref offset, out var start, out var length)
                && hit.TextPosition >= start && hit.TextPosition < start + length)
                return link;
        }

        return null;
    }

    /// <summary>Character range of <paramref name="target"/> in the text built from <paramref name="inlines"/>.</summary>
    private static bool TryLocate(InlineCollection inlines, Inline target, ref int offset, out int start, out int length)
    {
        foreach (var inline in inlines)
        {
            if (ReferenceEquals(inline, target))
            {
                start = offset;
                length = LengthOf(inline);
                return true;
            }

            if (inline is Span span)
            {
                if (TryLocate(span.Inlines, target, ref offset, out start, out length))
                    return true;
            }
            else
            {
                offset += LengthOf(inline);
            }
        }

        start = length = 0;
        return false;
    }

    /// <summary>Text runs count their characters; an embedded control (picture) counts as one character.</summary>
    private static int LengthOf(Inline inline) => inline switch
    {
        Run run => run.Text?.Length ?? 0,
        Span span => span.Inlines.Sum(LengthOf),
        LineBreak => Environment.NewLine.Length,
        _ => 1
    };
}
