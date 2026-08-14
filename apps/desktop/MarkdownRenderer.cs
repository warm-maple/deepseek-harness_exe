using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DeepSeekHarness.Desktop;

/// <summary>
/// 轻量 Markdown 渲染器（Markdown → FlowDocument），覆盖 Agent 回复中最常见的
/// 语法：标题、粗体、斜体、删除线、行内代码、代码块、列表、引用、分隔线、链接
/// 与表格（表格按代码块展示）。不依赖第三方库，保证离线可构建。
/// </summary>
public static class MarkdownRenderer
{
    private enum BlockKind { Code, Heading, Paragraph, Quote, List, Rule, Table }

    private sealed class MdBlock
    {
        public BlockKind Kind;
        public int Level;
        public bool Ordered;
        public string Text = string.Empty;
        public string Inline = string.Empty;
        public List<string> Items = [];
    }

    public static FlowDocument Render(string markdown, double fontSize = 14)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = fontSize,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Microsoft YaHei"),
            Foreground = Brush("#0F1115"),
            LineHeight = 1.42,
        };
        if (string.IsNullOrWhiteSpace(markdown)) return doc;

        foreach (var block in ParseBlocks(markdown))
        {
            switch (block.Kind)
            {
                case BlockKind.Code:
                case BlockKind.Table:
                    doc.Blocks.Add(BuildCodeBlock(block));
                    break;
                case BlockKind.Heading:
                    doc.Blocks.Add(BuildHeading(block));
                    break;
                case BlockKind.Paragraph:
                    doc.Blocks.Add(BuildParagraph(block.Inline, fontSize));
                    break;
                case BlockKind.Quote:
                    doc.Blocks.Add(BuildQuote(block.Inline, fontSize));
                    break;
                case BlockKind.List:
                    doc.Blocks.Add(BuildList(block, fontSize));
                    break;
                case BlockKind.Rule:
                    doc.Blocks.Add(BuildRule());
                    break;
            }
        }
        return doc;
    }

    /// <summary>Markdown 围栏代码块标记（反引号 x3，避免模板字符串中的反引号）。</summary>
    private static readonly string CodeFence = new string((char)96, 3);

    private static List<MdBlock> ParseBlocks(string markdown)
    {
        var blocks = new List<MdBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // 围栏代码块
            if (trimmed.StartsWith(CodeFence, StringComparison.Ordinal))
            {
                var lang = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
                var sb = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith(CodeFence, StringComparison.Ordinal))
                {
                    sb.AppendLine(lines[i]);
                    i++;
                }
                i++; // 跳过闭合围栏
                blocks.Add(new MdBlock { Kind = BlockKind.Code, Text = sb.ToString().TrimEnd(), Inline = lang });
                continue;
            }

            // 标题
            if (trimmed.Length > 1 && trimmed[0] == '#' && trimmed[1] == ' ')
            {
                var level = trimmed.TakeWhile(c => c == '#').Count();
                blocks.Add(new MdBlock { Kind = BlockKind.Heading, Level = level, Inline = trimmed[level..].Trim() });
                i++;
                continue;
            }

            // 分隔线
            if (trimmed is "---" or "***" or "___")
            {
                blocks.Add(new MdBlock { Kind = BlockKind.Rule });
                i++;
                continue;
            }

            // 引用
            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                var sb = new StringBuilder();
                while (i < lines.Length)
                {
                    var t = lines[i].TrimStart();
                    if (!t.StartsWith(">", StringComparison.Ordinal)) break;
                    sb.AppendLine(t.Length > 1 ? t[1..].Trim() : string.Empty);
                    i++;
                }
                blocks.Add(new MdBlock { Kind = BlockKind.Quote, Inline = sb.ToString().TrimEnd() });
                continue;
            }

            // 列表
            if (IsListMarker(trimmed, out var ordered, out var content))
            {
                var items = new List<string> { content };
                i++;
                while (i < lines.Length)
                {
                    var t = lines[i].Trim();
                    if (string.IsNullOrEmpty(t)) break;
                    if (IsListMarker(t, out var nextOrdered, out var nextContent))
                    {
                        if (nextOrdered != ordered) break;
                        items.Add(nextContent);
                        i++;
                    }
                    else
                    {
                        items[^1] += " " + t;
                        i++;
                    }
                }
                blocks.Add(new MdBlock { Kind = BlockKind.List, Ordered = ordered, Items = items });
                continue;
            }

            // 表格（表头行 + 分隔行）
            if (line.Contains('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var sb = new StringBuilder();
                sb.AppendLine(line);
                sb.AppendLine(lines[i + 1]);
                i += 2;
                while (i < lines.Length && lines[i].Contains('|'))
                {
                    sb.AppendLine(lines[i]);
                    i++;
                }
                blocks.Add(new MdBlock { Kind = BlockKind.Table, Text = sb.ToString().TrimEnd() });
                continue;
            }

            // 普通段落（合并连续非空行）
            var para = new StringBuilder();
            while (i < lines.Length)
            {
                var t = lines[i].Trim();
                if (string.IsNullOrEmpty(t)) break;
                if (t.StartsWith(CodeFence, StringComparison.Ordinal) || t.StartsWith("#", StringComparison.Ordinal) || IsListMarker(t, out _, out _)) break;
                if (para.Length > 0) para.Append(' ');
                para.Append(t);
                i++;
            }
            if (para.Length > 0)
            {
                blocks.Add(new MdBlock { Kind = BlockKind.Paragraph, Inline = para.ToString() });
            }
            else
            {
                i++;
            }
        }
        return blocks;
    }

    private static bool IsListMarker(string trimmed, out bool ordered, out string content)
    {
        ordered = false;
        content = string.Empty;
        if (trimmed.Length < 2) return false;
        if (trimmed[0] is '-' or '*' or '+')
        {
            if (trimmed[1] == ' ')
            {
                content = trimmed[2..].Trim();
                return true;
            }
            return false;
        }
        if (char.IsDigit(trimmed[0]))
        {
            var idx = trimmed.IndexOf('.');
            if (idx is > 0 and < 4 && idx + 1 < trimmed.Length && trimmed[idx + 1] == ' ')
            {
                ordered = true;
                content = trimmed[(idx + 2)..].Trim();
                return true;
            }
        }
        return false;
    }

    private static bool IsTableSeparator(string line)
    {
        var t = line.Trim();
        return t.Contains('|') && t.Replace("|", string.Empty).Replace("-", string.Empty).Replace(":", string.Empty).Trim().Length == 0;
    }

    private static Block BuildCodeBlock(MdBlock block)
    {
        var label = string.IsNullOrWhiteSpace(block.Inline) ? string.Empty : block.Inline + "  ";
        var border = new Border
        {
            Background = Brush("#F9FAFB"),
            BorderBrush = Brush("#E6E6E6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new TextBlock
            {
                Text = label + block.Text,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei"),
                FontSize = 12.5,
                Foreground = Brush("#0F1115"),
                TextWrapping = TextWrapping.Wrap,
            },
        };
        return new BlockUIContainer(border) { Margin = new Thickness(0, 4, 0, 12) };
    }

    private static Paragraph BuildHeading(MdBlock block)
    {
        var size = block.Level switch
        {
            1 => 21.0,
            2 => 18.0,
            3 => 16.0,
            _ => 15.0,
        };
        var para = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 8),
        };
        para.Inlines.Add(new Run(block.Inline));
        return para;
    }

    private static Paragraph BuildParagraph(string inline, double fontSize)
    {
        var para = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        FillInlines(para.Inlines, inline, fontSize);
        return para;
    }

    private static Paragraph BuildQuote(string inline, double fontSize)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0, 2, 0, 10),
            Padding = new Thickness(12, 4, 8, 4),
            BorderBrush = Brush("#D6D6D6"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Foreground = Brush("#61666B"),
        };
        FillInlines(para.Inlines, inline, fontSize);
        return para;
    }

    private static List BuildList(MdBlock block, double fontSize)
    {
        var list = new List { MarkerOffset = 18, Margin = new Thickness(2, 0, 0, 10) };
        list.MarkerStyle = block.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
        foreach (var item in block.Items)
        {
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            FillInlines(para.Inlines, item, fontSize);
            list.ListItems.Add(new ListItem { Blocks = { para } });
        }
        return list;
    }

    private static Paragraph BuildRule()
    {
        return new Paragraph
        {
            Margin = new Thickness(0, 8, 0, 12),
            BorderBrush = Brush("#E1E5EE"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>行内语法：行内代码、**粗体**、*斜体*、~~删除线~~、[文字](链接)。</summary>
    private static void FillInlines(InlineCollection inlines, string text, double fontSize)
    {
        var i = 0;
        var plain = new StringBuilder();
        while (i < text.Length)
        {
            var c = text[i];
            // 行内代码（反引号）
            if (c == (char)96)
            {
                var end = text.IndexOf((char)96, i + 1);
                if (end > i)
                {
                    FlushPlain(inlines, plain);
                    inlines.Add(new Run(text[(i + 1)..end])
                    {
                        FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei"),
                        FontSize = fontSize - 1,
                        Background = Brush("#EBEEF2"),
                        Foreground = Brush("#1E40AF"),
                    });
                    i = end + 1;
                    continue;
                }
            }
            // 链接 [label](url)
            if (c == '[')
            {
                var close = text.IndexOf(']', i + 1);
                if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var urlEnd = text.IndexOf(')', close + 2);
                    if (urlEnd > close)
                    {
                        FlushPlain(inlines, plain);
                        var label = text[(i + 1)..close];
                        var url = text[(close + 2)..urlEnd];
                        var link = new Hyperlink(new Run(label)) { Foreground = Brush("#4176E6") };
                        if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) link.NavigateUri = uri;
                        inlines.Add(link);
                        i = urlEnd + 1;
                        continue;
                    }
                }
            }
            // 粗体 **
            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    FlushPlain(inlines, plain);
                    var bold = new Bold();
                    FillInlines(bold.Inlines, text[(i + 2)..end], fontSize);
                    inlines.Add(bold);
                    i = end + 2;
                    continue;
                }
            }
            // 斜体 *
            if (c == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i && (end + 1 >= text.Length || text[end + 1] != '*'))
                {
                    FlushPlain(inlines, plain);
                    var italic = new Italic();
                    FillInlines(italic.Inlines, text[(i + 1)..end], fontSize);
                    inlines.Add(italic);
                    i = end + 1;
                    continue;
                }
            }
            // 删除线 ~~
            if (c == '~' && i + 1 < text.Length && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    FlushPlain(inlines, plain);
                    var strike = new Span { TextDecorations = TextDecorations.Strikethrough };
                    strike.Inlines.Add(new Run(text[(i + 2)..end]));
                    inlines.Add(strike);
                    i = end + 2;
                    continue;
                }
            }
            plain.Append(c);
            i++;
        }
        FlushPlain(inlines, plain);
    }

    private static void FlushPlain(InlineCollection inlines, StringBuilder plain)
    {
        if (plain.Length == 0) return;
        inlines.Add(new Run(plain.ToString()));
        plain.Clear();
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
