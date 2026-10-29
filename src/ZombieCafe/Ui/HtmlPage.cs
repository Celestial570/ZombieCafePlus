using System.Net;
using System.Numerics;
using System.Text;
using Raylib_cs;
using ZombieCafe.Core;
using ZombieCafe.Gfx;

namespace ZombieCafe.Ui;

/// <summary>
/// The help pages the game bundles (help.html, about.html, legal.html), which the original shows in a web view
/// on the help screen's chalkboard: read into headings, paragraphs, lists, definition lists, rules, bold text,
/// links and bundle images, wrapped to a width and drawn as a scrolling column. Links inside the page
/// (href="#name") scroll to their anchor; links out of the game are shown but not followed.
/// </summary>
public sealed class HtmlPage
{
    // the pages' own stylesheet: body text #6A883B, links #944A40
    static readonly Color Body = new(0x6a, 0x88, 0x3b, 0xff), Link = new(0xc4, 0x6a, 0x5a, 0xff);

    sealed record Run(string Text, bool Bold, string? Href);

    sealed class Block
    {
        public readonly List<Run> Runs = new();
        public float Size, Indent, Before, After;
        public string Prefix = "";
        public readonly List<string> Anchors = new();
        public bool Rule;
        public string? Image;
        public bool Empty => Runs.All(r => r.Text.Trim().Length == 0) && !Rule && Image == null;
    }

    sealed record Segment(string Text, float X, bool Bold, string? Href);

    sealed class Line
    {
        public float Y, Height, Size;
        public readonly List<Segment> Segments = new();
        public bool Rule;
        public SubImage? Image;
        public float Indent;
    }

    readonly List<Block> blocks = new();
    readonly Func<string, SubImage?> image;
    List<Line> lines = new();
    readonly Dictionary<string, float> anchorY = new();
    float laidOutWidth = -1, laidOutScale = -1;
    float scroll, height;
    bool dragging;
    float dragged;
    Vector2 lastMouse;

    /// <param name="image">Looks up an image the page refers to (by file name in the bundle).</param>
    public HtmlPage(string html, Func<string, SubImage?> image)
    {
        this.image = image;
        Parse(html);
    }

    // ---------------------------------------------------------------- reading the page

    void Parse(string html)
    {
        var lists = new Stack<(bool Ordered, int Count)>();
        int bold = 0, skip = 0;
        string? href = null;
        var pendingAnchors = new List<string>();
        Block current = NewBlock(22);

        Block NewBlock(float size, float indent = 0)
        {
            var b = new Block { Size = size, Indent = indent };
            b.Anchors.AddRange(pendingAnchors);
            pendingAnchors.Clear();
            blocks.Add(b);
            return b;
        }
        float ListIndent() => lists.Count * 26;
        void Break(float size = 22, float before = 0, float after = 0)
        {
            current = NewBlock(size, ListIndent());
            current.Before = before;
            current.After = after;
        }

        int i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                int end = html.IndexOf('<', i);
                if (end < 0)
                    end = html.Length;
                if (skip == 0)
                {
                    string text = WebUtility.HtmlDecode(html[i..end]);
                    text = CollapseSpaces(text);
                    if (text.Length > 0 && !(text == " " && current.Runs.Count == 0))
                        current.Runs.Add(new Run(text, bold > 0, href));
                }
                i = end;
                continue;
            }
            if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
            {
                int end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }
            int close = html.IndexOf('>', i);
            if (close < 0)
                break;
            string tag = html[(i + 1)..close].Trim();
            i = close + 1;
            bool closing = tag.StartsWith('/');
            if (closing)
                tag = tag[1..];
            string name = new string(tag.TakeWhile(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
            if (name is "head" or "style" or "script" or "title")
            {
                skip += closing ? -1 : 1;
                skip = Math.Max(0, skip);
                continue;
            }
            if (skip > 0)
                continue;
            if (Attribute(tag, "name") is { } anchor && !closing)
                pendingAnchors.Add(anchor);
            if (Attribute(tag, "id") is { } id && !closing)
                pendingAnchors.Add(id);
            if (pendingAnchors.Count > 0 && current.Empty)
            {
                current.Anchors.AddRange(pendingAnchors);
                pendingAnchors.Clear();
            }
            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4":
                    if (!closing)
                    {
                        float size = name switch { "h1" => 34, "h2" => 30, "h3" => 26, _ => 23 };
                        Break(size, before: 10, after: 4);
                        bold++;
                    }
                    else
                    {
                        bold = Math.Max(0, bold - 1);
                        Break();
                    }
                    break;
                case "p" or "div" or "tr" or "center" or "blockquote":
                    Break(before: closing ? 0 : 6);
                    break;
                case "br":
                    if (current.Empty)
                        current.Runs.Add(new Run(" ", false, null));
                    Break();
                    break;
                case "ol" or "ul":
                    if (!closing)
                        lists.Push((name == "ol", 0));
                    else if (lists.Count > 0)
                        lists.Pop();
                    Break(before: closing ? 6 : 0);
                    break;
                case "li":
                    if (!closing)
                    {
                        string prefix = "- ";
                        if (lists.Count > 0)
                        {
                            var (ordered, count) = lists.Pop();
                            lists.Push((ordered, count + 1));
                            prefix = ordered ? $"{count + 1}. " : "- ";
                        }
                        Break(before: 3);
                        current.Prefix = prefix;
                    }
                    else
                    {
                        Break();
                    }
                    break;
                case "dt":
                    Break(before: 6);
                    if (!closing)
                        bold++;
                    else
                        bold = Math.Max(0, bold - 1);
                    break;
                case "dd":
                    Break();
                    if (!closing)
                        current.Indent += 26;
                    else
                        current.Before = 10;
                    break;
                case "hr":
                    Break();
                    current.Rule = true;
                    current.Before = current.After = 12;
                    Break();
                    break;
                case "img":
                    if (!closing && Attribute(tag, "src") is { } src)
                    {
                        Break();
                        current.Image = src;
                        Break();
                    }
                    break;
                case "b" or "strong":
                    bold = closing ? Math.Max(0, bold - 1) : bold + 1;
                    break;
                case "a":
                    href = closing ? null : Attribute(tag, "href");
                    break;
            }
        }
    }

    static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) && c != ' ')
            {
                if (!space)
                    sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(c == ' ' ? ' ' : c);
                space = false;
            }
        }
        return sb.ToString();
    }

    static string? Attribute(string tag, string name)
    {
        int at = 0;
        while ((at = tag.IndexOf(name, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool startsWord = at > 0 && char.IsWhiteSpace(tag[at - 1]);
            int p = at + name.Length;
            while (p < tag.Length && char.IsWhiteSpace(tag[p]))
                p++;
            if (startsWord && p < tag.Length && tag[p] == '=')
            {
                p++;
                while (p < tag.Length && char.IsWhiteSpace(tag[p]))
                    p++;
                if (p < tag.Length && (tag[p] == '"' || tag[p] == '\''))
                {
                    char q = tag[p];
                    int end = tag.IndexOf(q, p + 1);
                    return end < 0 ? tag[(p + 1)..] : tag[(p + 1)..end];
                }
                int stop = p;
                while (stop < tag.Length && !char.IsWhiteSpace(tag[stop]) && tag[stop] != '/')
                    stop++;
                return tag[p..stop];
            }
            at += name.Length;
        }
        return null;
    }

    // ---------------------------------------------------------------- laying it out

    void LayOut(float width, float scale)
    {
        lines = new List<Line>();
        anchorY.Clear();
        float y = 0;
        foreach (var b in blocks)
        {
            if (b.Empty && b.Anchors.Count == 0)
                continue;
            y += b.Before * scale;
            foreach (var a in b.Anchors)
                anchorY.TryAdd(a, y);
            if (b.Rule)
            {
                lines.Add(new Line { Y = y, Height = 3 * scale, Rule = true });
                y += 3 * scale + b.After * scale;
                continue;
            }
            if (b.Image != null)
            {
                if (image(b.Image) is { } img)
                {
                    float h = img.FullHeight * scale * 0.6f;
                    lines.Add(new Line { Y = y, Height = h, Image = img, Indent = b.Indent * scale });
                    y += h + 4 * scale;
                }
                continue;
            }
            if (b.Empty)
                continue;
            float size = b.Size * scale;
            float lineH = size * 1.2f;
            float left = b.Indent * scale;
            var line = new Line { Y = y, Height = lineH, Size = size };
            float x = left;
            if (b.Prefix.Length > 0)
            {
                line.Segments.Add(new Segment(b.Prefix, x, false, null));
                x += Text.Measure(b.Prefix, size).W;
                left = x;
            }
            foreach (var run in b.Runs)
            {
                // words keep their trailing space; a run may start mid-line
                foreach (var word in SplitWords(run.Text))
                {
                    float w = Text.Measure(word, size).W;
                    if (x + w > width && x > left && word.Trim().Length > 0)
                    {
                        lines.Add(line);
                        y += lineH;
                        line = new Line { Y = y, Height = lineH, Size = size };
                        x = left;
                        if (word.Trim().Length == 0)
                            continue;
                    }
                    string shown = x == left ? word.TrimStart() : word;
                    line.Segments.Add(new Segment(shown, x, run.Bold, run.Href));
                    x += Text.Measure(shown, size).W;
                }
            }
            lines.Add(line);
            y += lineH + b.After * scale;
        }
        height = y;
        laidOutWidth = width;
        laidOutScale = scale;
    }

    static IEnumerable<string> SplitWords(string s)
    {
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ')
            {
                yield return s[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < s.Length)
            yield return s[start..];
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>Back to the top (another page was picked).</summary>
    public void Reset() => scroll = 0;

    /// <summary>
    /// Draws the page into <paramref name="area"/> (virtual screen points), handling the wheel, dragging, the
    /// arrow and page keys, and in-page links.
    /// </summary>
    public void Draw(Rectangle area, float scale)
    {
        if (MathF.Abs(laidOutWidth - area.Width) > 0.5f || MathF.Abs(laidOutScale - scale) > 0.001f)
            LayOut(area.Width, scale);
        float max = Math.Max(0, height - area.Height);
        bool over = Widgets.Hover(area);
        if (over)
            scroll -= Input.GetMouseWheelMove() * 60 * scale;
        if (Input.IsKeyDown(KeyboardKey.Down)) scroll += 8 * scale;
        if (Input.IsKeyDown(KeyboardKey.Up)) scroll -= 8 * scale;
        if (Input.IsKeyPressed(KeyboardKey.PageDown)) scroll += area.Height * 0.9f;
        if (Input.IsKeyPressed(KeyboardKey.PageUp)) scroll -= area.Height * 0.9f;
        if (Input.IsKeyPressed(KeyboardKey.Home)) scroll = 0;
        if (Input.IsKeyPressed(KeyboardKey.End)) scroll = max;
        // a drag on the page scrolls it
        var mouse = Widgets.Mouse;
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            dragging = over;
            dragged = 0;
        }
        else if (dragging && Input.IsMouseButtonDown(MouseButton.Left))
        {
            scroll -= mouse.Y - lastMouse.Y;
            dragged += MathF.Abs(mouse.Y - lastMouse.Y);
        }
        else if (!Input.IsMouseButtonDown(MouseButton.Left))
        {
            dragging = false;
        }
        lastMouse = mouse;
        scroll = Math.Clamp(scroll, 0, max);

        // clip to the board
        var tl = Vector2.Transform(new Vector2(area.X, area.Y), Gfx.Draw.Transform);
        var br = Vector2.Transform(new Vector2(area.X + area.Width, area.Y + area.Height), Gfx.Draw.Transform);
        Raylib.BeginScissorMode((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
        string? clicked = null;
        foreach (var line in lines)
        {
            float y = area.Y + line.Y - scroll;
            if (y + line.Height < area.Y || y > area.Y + area.Height)
                continue;
            if (line.Rule)
            {
                Gfx.Draw.Rect(new Rectangle(area.X + 10 * scale, y, area.Width - 20 * scale, line.Height), Body);
                continue;
            }
            if (line.Image != null)
            {
                float s = 0.6f * scale;
                line.Image.Draw(area.X + line.Indent, y, Anchor.Top | Anchor.Left, Color.White, Flip.None, s, s);
                continue;
            }
            foreach (var seg in line.Segments)
            {
                var color = seg.Href != null ? Link : Body;
                float x = area.X + seg.X;
                Text.Draw(seg.Text, x, y, line.Size, color);
                if (seg.Bold)
                    Text.Draw(seg.Text, x + 0.8f * scale, y, line.Size, color);
                if (seg.Href != null)
                {
                    var r = new Rectangle(x, y, Text.Measure(seg.Text, line.Size).W, line.Height);
                    // a drag that scrolled the page is not a tap on the link it ends on
                    if (Widgets.Clicked(r) && dragged < 8)
                        clicked = seg.Href;
                }
            }
        }
        Raylib.EndScissorMode();

        if (clicked != null && clicked.StartsWith('#') && anchorY.TryGetValue(clicked[1..], out float target))
            scroll = Math.Clamp(target, 0, max);
    }
}
