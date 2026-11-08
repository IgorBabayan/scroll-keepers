using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScrollKeeper.Presentation.Documents;

/// <summary>Renders ESOUI BBCode as a themed <see cref="FlowDocument"/>.</summary>
internal static class BbCodeDocumentBuilder
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "quote", "code", "php", "html", "center", "left", "right", "indent", "h1", "h2", "h3", "spoiler"
    };

    /// <summary>vBulletin [size=1..7] → points.</summary>
    private static readonly double[] FontSizes = [10, 11, 13, 15, 18, 22, 28];

    public static FlowDocument Build(string? bbcode)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = 13,
            LineHeight = double.NaN,
            TextAlignment = TextAlignment.Left
        };
        document.SetResourceReference(FlowDocument.FontFamilyProperty, "BodyFont");
        document.SetResourceReference(FlowDocument.ForegroundProperty, "WindowForegroundBrush");

        RenderBlocks(BbCodeParser.Parse(bbcode).Children, document.Blocks, TextAlignment.Left);
        Tidy(document.Blocks);

        if (document.Blocks.Count == 0)
        {
            var empty = new Paragraph(new Run("No information."));
            empty.SetResourceReference(TextElement.ForegroundProperty, "SecondaryForegroundBrush");
            document.Blocks.Add(empty);
        }

        return document;
    }

    // ===== Blocks =====

    private static void RenderBlocks(IEnumerable<BbNode> nodes, BlockCollection blocks, TextAlignment alignment)
    {
        Paragraph? paragraph = null;

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
                paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8), TextAlignment = alignment };
                blocks.Add(paragraph);
            }

            RenderInline(node, paragraph.Inlines);
        }
    }

    private static Block RenderBlock(BbTag tag, TextAlignment alignment)
    {
        switch (tag.Name)
        {
            case "list":
            {
                var list = new List
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(22, 0, 0, 0),
                    MarkerStyle = tag.Argument?.ToLowerInvariant() switch
                    {
                        "1" => TextMarkerStyle.Decimal,
                        "a" => TextMarkerStyle.LowerLatin,
                        "i" => TextMarkerStyle.LowerRoman,
                        _ => TextMarkerStyle.Disc
                    }
                };

                foreach (var child in tag.Children)
                {
                    // Whitespace between "[list]" and the first "[*]".
                    if (child is BbText text && string.IsNullOrWhiteSpace(text.Text))
                        continue;

                    var item = new ListItem();
                    var content = child is BbTag { Name: BbCodeParser.ListItemName } li ? li.Children : new List<BbNode> { child };
                    RenderBlocks(content, item.Blocks, alignment);
                    Tidy(item.Blocks);
                    foreach (var block in item.Blocks)
                        block.Margin = new Thickness(0, 0, 0, 2);
                    list.ListItems.Add(item);
                }

                return list;
            }

            case "quote":
            {
                var section = new Section
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(12, 6, 8, 6),
                    BorderThickness = new Thickness(3, 0, 0, 0)
                };
                section.SetResourceReference(Block.BorderBrushProperty, "AccentBrush");
                section.SetResourceReference(TextElement.BackgroundProperty, "CardBackgroundBrush");

                if (!string.IsNullOrWhiteSpace(tag.Argument))
                {
                    var author = new Paragraph(new Italic(new Run($"{tag.Argument} wrote:"))) { Margin = new Thickness(0, 0, 0, 4) };
                    author.SetResourceReference(TextElement.ForegroundProperty, "SecondaryForegroundBrush");
                    section.Blocks.Add(author);
                }

                RenderBlocks(tag.Children, section.Blocks, alignment);
                Tidy(section.Blocks);
                return section;
            }

            case "code" or "php" or "html":
            {
                var code = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10, 8, 10, 8),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                    FontSize = 12,
                    TextAlignment = TextAlignment.Left
                };
                code.SetResourceReference(TextElement.BackgroundProperty, "ControlBackgroundBrush");
                AddText(code.Inlines, string.Concat(tag.Children.OfType<BbText>().Select(t => t.Text)).Trim('\n'), decode: false);
                return code;
            }

            case "h1" or "h2" or "h3":
            {
                var heading = new Paragraph
                {
                    Margin = new Thickness(0, 6, 0, 6),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = tag.Name switch { "h1" => 22, "h2" => 18, _ => 15 },
                    TextAlignment = alignment
                };
                heading.SetResourceReference(TextElement.FontFamilyProperty, "HeadingFont");
                foreach (var child in tag.Children)
                    RenderInline(child, heading.Inlines);
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

                var section = new Section
                {
                    Margin = new Thickness(tag.Name == "indent" ? 24 : 0, 0, 0, 0),
                    TextAlignment = childAlignment
                };
                RenderBlocks(tag.Children, section.Blocks, childAlignment);
                Tidy(section.Blocks);
                return section;
            }
        }
    }

    // ===== Inlines =====

    private static void RenderInline(BbNode node, InlineCollection inlines)
    {
        if (node is BbText text)
        {
            AddText(inlines, text.Text, decode: true);
            return;
        }

        var tag = (BbTag)node;
        switch (tag.Name)
        {
            case "b":
                inlines.Add(Wrap(new Bold(), tag));
                break;
            case "i":
                inlines.Add(Wrap(new Italic(), tag));
                break;
            case "u":
                inlines.Add(Wrap(new Underline(), tag));
                break;
            case "s":
                inlines.Add(Wrap(new Span { TextDecorations = TextDecorations.Strikethrough }, tag));
                break;
            case "color":
            {
                var span = new Span();
                if (TryParseBrush(tag.Argument) is { } brush)
                    span.Foreground = brush;
                inlines.Add(Wrap(span, tag));
                break;
            }
            case "size":
            {
                var span = new Span();
                if (ParseFontSize(tag.Argument) is { } size)
                    span.FontSize = size;
                inlines.Add(Wrap(span, tag));
                break;
            }
            case "font":
            {
                var span = new Span();
                if (!string.IsNullOrWhiteSpace(tag.Argument))
                    span.FontFamily = new FontFamily(tag.Argument + ", Segoe UI");
                inlines.Add(Wrap(span, tag));
                break;
            }
            case "highlight":
            {
                var span = new Span();
                span.SetResourceReference(TextElement.BackgroundProperty, "AccentSubtleBackgroundBrush");
                inlines.Add(Wrap(span, tag));
                break;
            }
            case "url" or "email" or "video" or "youtube":
                inlines.Add(CreateLink(tag));
                break;
            case "img":
                if (CreateImage(PlainText(tag)) is { } image)
                    inlines.Add(image);
                break;
            case BbCodeParser.ListItemName:
                inlines.Add(new LineBreak());
                inlines.Add(new Run("• "));
                foreach (var child in tag.Children)
                    RenderInline(child, inlines);
                break;
            default:
                // Block tags nested in inline formatting (e.g. "[b][list]...") are flattened onto their own lines.
                if (BlockTags.Contains(tag.Name))
                    inlines.Add(new LineBreak());
                foreach (var child in tag.Children)
                    RenderInline(child, inlines);
                if (BlockTags.Contains(tag.Name))
                    inlines.Add(new LineBreak());
                break;
        }
    }

    private static Span Wrap(Span span, BbTag tag)
    {
        foreach (var child in tag.Children)
            RenderInline(child, span.Inlines);
        return span;
    }

    private static Inline CreateLink(BbTag tag)
    {
        var display = PlainText(tag).Trim();
        var target = string.IsNullOrWhiteSpace(tag.Argument) ? display : tag.Argument.Trim();
        if (tag.Name == "email" && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            target = "mailto:" + target;
        else if (tag.Name != "email" && target.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            target = "https://" + target;

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
            return Wrap(new Span(), tag);

        var link = new Hyperlink { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
        link.RequestNavigate += (_, e) =>
        {
            OpenInBrowser(e.Uri);
            e.Handled = true;
        };

        if (string.IsNullOrEmpty(display) || tag.Children.Count == 0)
            link.Inlines.Add(new Run(uri.AbsoluteUri));
        else
            foreach (var child in tag.Children)
                RenderInline(child, link.Inlines);

        return link;
    }

    private static InlineUIContainer? CreateImage(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;

        try
        {
            // WPF downloads http(s) images itself, in the background.
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = uri;
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            source.EndInit();

            var image = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth = 560,
                MaxHeight = 420,
                Margin = new Thickness(0, 4, 0, 4)
            };
            return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Bottom };
        }
        catch (Exception ex) when (ex is UriFormatException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static void AddText(InlineCollection inlines, string text, bool decode)
    {
        if (decode)
            text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                inlines.Add(new LineBreak());
            if (lines[i].Length > 0)
                inlines.Add(new Run(lines[i]));
        }
    }

    private static string PlainText(BbNode node) => node switch
    {
        BbText text => WebUtility.HtmlDecode(text.Text),
        BbTag tag => string.Concat(tag.Children.Select(PlainText)),
        _ => string.Empty
    };

    /// <summary>Removes line breaks at the start and end of paragraphs and drops paragraphs left empty.</summary>
    private static void Tidy(BlockCollection blocks)
    {
        foreach (var paragraph in blocks.OfType<Paragraph>().ToList())
        {
            while (paragraph.Inlines.FirstInline is LineBreak first)
                paragraph.Inlines.Remove(first);
            while (paragraph.Inlines.LastInline is LineBreak last)
                paragraph.Inlines.Remove(last);
            while (paragraph.Inlines.FirstInline is Run { Text: var t } run && string.IsNullOrWhiteSpace(t) && paragraph.Inlines.Count > 1)
            {
                paragraph.Inlines.Remove(run);
                while (paragraph.Inlines.FirstInline is LineBreak lb)
                    paragraph.Inlines.Remove(lb);
            }

            if (paragraph.Inlines.Count == 0 || paragraph.Inlines.All(i => i is Run { Text: var s } && string.IsNullOrWhiteSpace(s)))
                blocks.Remove(paragraph);
        }
    }

    private static Brush? TryParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            if (new BrushConverter().ConvertFromInvariantString(value.Trim()) is Brush brush)
            {
                brush.Freeze();
                return brush;
            }
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            // Unknown color names are ignored, like browsers do.
        }

        return null;
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
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not open {uri}: {ex.Message}");
        }
    }
}
