using System.Text.RegularExpressions;

namespace ScrollKeeper.Presentation.Documents;

internal abstract record BbNode;

internal sealed record BbText(string Text) : BbNode;

internal sealed record BbTag(string Name, string? Argument, List<BbNode> Children) : BbNode;

/// <summary>
/// Parses the BBCode ESOUI (vBulletin) uses in addon descriptions and change logs into a tree.
/// Forgiving like a forum: unknown tags stay as text, unclosed tags close at the end, stray closing tags are text.
/// </summary>
internal static partial class BbCodeParser
{
    public const string RootName = "root";
    public const string ListItemName = "*";

    private static readonly HashSet<string> KnownTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "i", "u", "s", "color", "size", "font", "highlight", "url", "email", "img", "video", "youtube",
        "list", ListItemName, "quote", "code", "php", "html", "noparse", "center", "left", "right", "indent",
        "h1", "h2", "h3", "spoiler"
    };

    /// <summary>Content of these tags is not parsed.</summary>
    private static readonly HashSet<string> RawTags = new(StringComparer.OrdinalIgnoreCase) { "code", "php", "html", "noparse" };

    public static BbTag Parse(string? input)
    {
        var root = new BbTag(RootName, null, []);
        if (string.IsNullOrEmpty(input))
            return root;

        var text = input.Replace("\r\n", "\n").Replace('\r', '\n');
        var stack = new List<BbTag> { root };
        var position = 0;

        while (position < text.Length)
        {
            var match = TagRegex().Match(text, position);
            if (!match.Success)
            {
                AddText(stack[^1], text[position..]);
                break;
            }

            if (match.Index > position)
                AddText(stack[^1], text[position..match.Index]);
            position = match.Index + match.Length;

            var isClosing = match.Groups[1].Success;
            var name = match.Groups[2].Value.ToLowerInvariant();
            var argument = match.Groups[3].Success ? Unquote(match.Groups[3].Value) : null;

            if (!KnownTags.Contains(name))
            {
                AddText(stack[^1], match.Value);
                continue;
            }

            if (isClosing)
            {
                var index = stack.FindLastIndex(t => t.Name == name);
                if (index > 0)
                    stack.RemoveRange(index, stack.Count - index);
                else if (name != ListItemName)
                    AddText(stack[^1], match.Value);
                continue;
            }

            if (name == ListItemName)
            {
                var listIndex = stack.FindLastIndex(t => t.Name == "list");
                if (listIndex < 0)
                {
                    // A bullet outside of a list: keep it as a bullet character.
                    AddText(stack[^1], "• ");
                    continue;
                }

                // A new item closes the previous one (and anything still open inside it).
                stack.RemoveRange(listIndex + 1, stack.Count - listIndex - 1);
                var item = new BbTag(ListItemName, null, []);
                stack[^1].Children.Add(item);
                stack.Add(item);
                continue;
            }

            var tag = new BbTag(name, argument, []);
            stack[^1].Children.Add(tag);

            if (RawTags.Contains(name))
            {
                var close = text.IndexOf($"[/{name}]", position, StringComparison.OrdinalIgnoreCase);
                var end = close < 0 ? text.Length : close;
                if (end > position)
                    tag.Children.Add(new BbText(text[position..end]));
                position = close < 0 ? text.Length : close + name.Length + 3;
                continue;
            }

            stack.Add(tag);
        }

        return root;
    }

    private static void AddText(BbTag parent, string text)
    {
        if (text.Length == 0)
            return;

        // Merge with the previous text so "[unknown]" tags do not split words into many runs.
        if (parent.Children.Count > 0 && parent.Children[^1] is BbText previous)
            parent.Children[^1] = new BbText(previous.Text + text);
        else
            parent.Children.Add(new BbText(text));
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\'') && trimmed[^1] == trimmed[0])
            trimmed = trimmed[1..^1];
        return trimmed.Trim();
    }

    [GeneratedRegex(@"\[(/)?([a-zA-Z]+[1-6]?|\*)(?:=([^\]\n]{0,512}))?\]")]
    private static partial Regex TagRegex();
}
