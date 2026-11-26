using System.Diagnostics;
using System.Globalization;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ScrollKeeper.Services.Images;

namespace ScrollKeeper.Presentation.Documents;

/// <summary>
/// Renders ESOUI BBCode as themed Avalonia controls: a stack of selectable paragraphs (<see cref="BbParagraph"/>),
/// lists, quotes and code blocks. Theme colors are resource references, so the document follows theme changes.
/// </summary>
internal sealed class BbCodeDocumentBuilder
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "quote", "code", "php", "html", "center", "left", "right", "indent", "h1", "h2", "h3", "spoiler"
    };

    /// <summary>vBulletin [size=1..7] → points.</summary>
    private static readonly double[] FontSizes = [10, 11, 13, 15, 18, 22, 28];

    /// <summary>Pictures inside descriptions are shown at most this large.</summary>
    private const double MaxImageWidth = 560;
    private const double MaxImageHeight = 420;

    /// <summary>Control whose resources (palette, fonts) the document uses.</summary>
    private readonly Control _host;

    private BbCodeDocumentBuilder(Control host)
    {
        _host = host;
    }

    public static Control Build(string? bbcode, Control host) => new BbCodeDocumentBuilder(host).BuildDocument(bbcode);

    private Control BuildDocument(string? bbcode)
    {
        var document = new StackPanel();

        RenderBlocks(BbCodeParser.Parse(bbcode).Children, document.Children, TextAlignment.Left);
        Tidy(document.Children);

        if (document.Children.Count == 0)
        {
            var empty = NewParagraph(TextAlignment.Left);
            empty.Inlines!.Add(new Run("No information."));
            UseResource(empty, TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
            document.Children.Add(empty);
        }

        return document;
    }

    // ===== Blocks =====

    private void RenderBlocks(IEnumerable<BbNode> nodes, Avalonia.Controls.Controls blocks, TextAlignment alignment)
    {
        BbParagraph? paragraph = null;

        foreach (var node in nodes)
        {
            if (node is BbTag tag && BlockTags.Contains(tag.Name))
            {
                paragraph = null;
                blocks.Add(RenderBlock(tag, alignment));
                continue;
            }

            if (paragraph is null)
            {
                paragraph = NewParagraph(alignment);
                blocks.Add(paragraph);
            }

            RenderInline(node, paragraph.Inlines!, paragraph);
        }
    }

    private Control RenderBlock(BbTag tag, TextAlignment alignment)
    {
        switch (tag.Name)
        {
            case "list":
            {
                var list = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                var number = 0;

                foreach (var child in tag.Children)
                {
                    // Whitespace between "[list]" and the first "[*]".
                    if (child is BbText text && string.IsNullOrWhiteSpace(text.Text))
                        continue;

                    var item = new StackPanel();
                    var content = child is BbTag { Name: BbCodeParser.ListItemName } li ? li.Children : new List<BbNode> { child };
                    RenderBlocks(content, item.Children, alignment);
                    Tidy(item.Children);
                    foreach (var block in item.Children)
                        block.Margin = new Thickness(0, 0, 0, 2);

                    var marker = new TextBlock
                    {
                        Text = ListMarker(tag.Argument, ++number),
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
                    Grid.SetColumn(marker, 0);
                    Grid.SetColumn(item, 1);
                    row.Children.Add(marker);
                    row.Children.Add(item);
                    list.Children.Add(row);
                }

                return list;
            }

            case "quote":
            {
                var content = new StackPanel();
                var section = new Border
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(12, 6, 8, 6),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Child = content
                };
                UseResource(section, Border.BorderBrushProperty, "AccentBrush");
                UseResource(section, Border.BackgroundProperty, "CardBackgroundBrush");

                if (!string.IsNullOrWhiteSpace(tag.Argument))
                {
                    var author = NewParagraph(alignment);
                    author.Margin = new Thickness(0, 0, 0, 4);
                    author.Inlines!.Add(new Italic { Inlines = { new Run($"{tag.Argument} wrote:") } });
                    UseResource(author, TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
                    content.Children.Add(author);
                }

                RenderBlocks(tag.Children, content.Children, alignment);
                Tidy(content.Children);
                return section;
            }

            case "code" or "php" or "html":
            {
                var code = new SelectableTextBlock
                {
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Text = Normalize(string.Concat(tag.Children.OfType<BbText>().Select(t => t.Text))).Trim('\n')
                };
                UseResource(code, TextBlock.FontFamilyProperty, "MonospaceFont");

                var block = new Border
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10, 8, 10, 8),
                    CornerRadius = new CornerRadius(4),
                    Child = code
                };
                UseResource(block, Border.BackgroundProperty, "ControlBackgroundBrush");
                return block;
            }

            case "h1" or "h2" or "h3":
            {
                var heading = NewParagraph(alignment);
                heading.Margin = new Thickness(0, 6, 0, 6);
                heading.FontWeight = FontWeight.SemiBold;
                heading.FontSize = tag.Name switch { "h1" => 22, "h2" => 18, _ => 15 };
                UseResource(heading, TextBlock.FontFamilyProperty, "HeadingFont");
                foreach (var child in tag.Children)
                    RenderInline(child, heading.Inlines!, heading);
                return heading;
            }

            default:
            {
                // center / left / right / indent / spoiler: a section around the content.
                var childAlignment = tag.Name switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    "left" => TextAlignment.Left,
                    _ => alignment
                };

                var section = new StackPanel { Margin = new Thickness(tag.Name == "indent" ? 24 : 0, 0, 0, 0) };
                RenderBlocks(tag.Children, section.Children, childAlignment);
                Tidy(section.Children);
                return section;
            }
        }
    }

    private static BbParagraph NewParagraph(TextAlignment alignment) => new()
    {
        Margin = new Thickness(0, 0, 0, 8),
        TextAlignment = alignment,
        TextWrapping = TextWrapping.Wrap,
        Inlines = new InlineCollection()
    };

    /// <summary>Bullet for [list], "1." / "a." / "i." for [list=1] / [list=a] / [list=i].</summary>
    private static string ListMarker(string? style, int number) => style?.Trim().ToLowerInvariant() switch
    {
        "1" => $"{number}.",
        "a" => $"{ToLatin(number)}.",
        "i" => $"{ToRoman(number)}.",
        _ => "•"
    };

    private static string ToLatin(int number)
    {
        var text = string.Empty;
        while (number > 0)
        {
            number--;
            text = (char)('a' + number % 26) + text;
            number /= 26;
        }

        return text;
    }

    private static string ToRoman(int number)
    {
        ReadOnlySpan<(int Value, string Text)> numerals =
        [
            (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
            (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")
        ];

        var text = new System.Text.StringBuilder();
        foreach (var (value, numeral) in numerals)
        {
            while (number >= value)
            {
                text.Append(numeral);
                number -= value;
            }
        }

        return text.ToString();
    }

    // ===== Inlines =====

    private void RenderInline(BbNode node, InlineCollection inlines, BbParagraph paragraph)
    {
        if (node is BbText text)
        {
            AddText(inlines, text.Text);
            return;
        }

        var tag = (BbTag)node;
        switch (tag.Name)
        {
            case "b":
                inlines.Add(Wrap(new Bold(), tag, paragraph));
                break;
            case "i":
                inlines.Add(Wrap(new Italic(), tag, paragraph));
                break;
            case "u":
                inlines.Add(Wrap(new Underline(), tag, paragraph));
                break;
            case "s":
                inlines.Add(Wrap(new Span { TextDecorations = TextDecorations.Strikethrough }, tag, paragraph));
                break;
            case "color":
            {
                var span = new Span();
                if (TryParseBrush(tag.Argument) is { } brush)
                    span.Foreground = brush;
                inlines.Add(Wrap(span, tag, paragraph));
                break;
            }
            case "size":
            {
                var span = new Span();
                if (ParseFontSize(tag.Argument) is { } size)
                    span.FontSize = size;
                inlines.Add(Wrap(span, tag, paragraph));
                break;
            }
            case "font":
            {
                var span = new Span();
                // Missing fonts fall back to the default font.
                if (!string.IsNullOrWhiteSpace(tag.Argument))
                    span.FontFamily = new FontFamily(tag.Argument.Trim());
                inlines.Add(Wrap(span, tag, paragraph));
                break;
            }
            case "highlight":
            {
                var span = new Span();
                UseResource(span, Inline.BackgroundProperty, "AccentSubtleBackgroundBrush");
                inlines.Add(Wrap(span, tag, paragraph));
                break;
            }
            case "url" or "email" or "video" or "youtube":
                inlines.Add(CreateLink(tag, paragraph));
                break;
            case "img":
                if (CreateImage(PlainText(tag), paragraph) is { } image)
                    inlines.Add(image);
                break;
            case BbCodeParser.ListItemName:
                inlines.Add(new Run("\n• "));
                foreach (var child in tag.Children)
                    RenderInline(child, inlines, paragraph);
                break;
            default:
                // Block tags nested in inline formatting (e.g. "[b][list]...") are flattened onto their own lines.
                if (BlockTags.Contains(tag.Name))
                    inlines.Add(new Run("\n"));
                foreach (var child in tag.Children)
                    RenderInline(child, inlines, paragraph);
                if (BlockTags.Contains(tag.Name))
                    inlines.Add(new Run("\n"));
                break;
        }
    }

    private Span Wrap(Span span, BbTag tag, BbParagraph paragraph)
    {
        foreach (var child in tag.Children)
            RenderInline(child, span.Inlines, paragraph);
        return span;
    }

    private Inline CreateLink(BbTag tag, BbParagraph paragraph)
    {
        var display = PlainText(tag).Trim();
        var target = string.IsNullOrWhiteSpace(tag.Argument) ? display : tag.Argument.Trim();
        if (tag.Name == "email" && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            target = "mailto:" + target;
        else if (tag.Name != "email" && target.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            target = "https://" + target;

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
            return Wrap(new Span(), tag, paragraph);

        var link = new Span();
        UseResource(link, Inline.ForegroundProperty, "AccentBrush");

        if (string.IsNullOrEmpty(display) || tag.Children.Count == 0)
            link.Inlines.Add(new Run(uri.AbsoluteUri));
        else
            foreach (var child in tag.Children)
                RenderInline(child, link.Inlines, paragraph);

        paragraph.AddLink(link, uri);
        return link;
    }

    /// <summary>An inline picture, downloaded in the background (and cached) by <see cref="IImageCache"/>.</summary>
    private static InlineUIContainer? CreateImage(string url, BbParagraph paragraph)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = MaxImageWidth,
            MaxHeight = MaxImageHeight,
            Margin = new Thickness(0, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        _ = LoadImageAsync(image, uri, paragraph);
        return new InlineUIContainer(image);
    }

    private static async Task LoadImageAsync(Image image, Uri uri, BbParagraph paragraph)
    {
        var bitmap = await App.Services.GetRequiredService<IImageCache>().GetAsync(uri.AbsoluteUri, 0);
        if (bitmap is null)
            return;

        // Sized explicitly so the paragraph can lay the picture out (scaled down, never up).
        var size = bitmap.Size;
        var scale = Math.Min(1, Math.Min(MaxImageWidth / Math.Max(size.Width, 1), MaxImageHeight / Math.Max(size.Height, 1)));
        image.Width = size.Width * scale;
        image.Height = size.Height * scale;
        image.Source = bitmap;
        paragraph.Refresh();
    }

    private static void AddText(InlineCollection inlines, string text)
    {
        // Line breaks stay inside the runs, so the paragraph's text offsets match the runs (see BbParagraph).
        text = Normalize(WebUtility.HtmlDecode(text));
        if (text.Length > 0)
            inlines.Add(new Run(text));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string PlainText(BbNode node) => node switch
    {
        BbText text => WebUtility.HtmlDecode(text.Text),
        BbTag tag => string.Concat(tag.Children.Select(PlainText)),
        _ => string.Empty
    };

    /// <summary>Removes line breaks at the start and end of paragraphs and drops paragraphs left empty.</summary>
    private static void Tidy(Avalonia.Controls.Controls blocks)
    {
        foreach (var paragraph in blocks.OfType<BbParagraph>().ToList())
        {
            var inlines = paragraph.Inlines!;

            // Leading line breaks and whitespace-only lines.
            while (inlines.Count > 0 && inlines[0] is Run first)
            {
                var trimmed = (first.Text ?? string.Empty).TrimStart('\n');
                if (string.IsNullOrWhiteSpace(trimmed) && inlines.Count > 1)
                {
                    inlines.RemoveAt(0);
                    continue;
                }

                first.Text = trimmed;
                break;
            }

            // Trailing line breaks.
            while (inlines.Count > 0 && inlines[^1] is Run last)
            {
                var trimmed = (last.Text ?? string.Empty).TrimEnd('\n');
                if (trimmed.Length == 0)
                {
                    inlines.RemoveAt(inlines.Count - 1);
                    continue;
                }

                last.Text = trimmed;
                break;
            }

            if (inlines.Count == 0 || inlines.All(i => i is Run { Text: var s } && string.IsNullOrWhiteSpace(s)))
                blocks.Remove(paragraph);
        }
    }

    /// <summary>Binds <paramref name="property"/> to a resource of the host, so it follows theme changes.</summary>
    private void UseResource(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target.Bind(property, _host.GetResourceObservable(key));

    private static IBrush? TryParseBrush(string? value)
    {
        // Named colors (red, gold...) and #rgb / #rrggbb; unknown values are ignored, like browsers do.
        if (string.IsNullOrWhiteSpace(value) || !Color.TryParse(value.Trim(), out var color))
            return null;

        return new ImmutableSolidColorBrush(color);
    }

    private static double? ParseFontSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Trim().TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
            return null;

        return number <= FontSizes.Length && number == Math.Floor(number)
            ? FontSizes[(int)number - 1]
            : Math.Clamp(number, 9, 32);
    }

    public static void OpenInBrowser(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https" or "mailto"))
            return;

        try
        {
            // Shell execute opens the default browser / mail app on every platform (xdg-open on Linux, open on macOS).
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not open {uri}: {ex.Message}");
        }
    }
}
