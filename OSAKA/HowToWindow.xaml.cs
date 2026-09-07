using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace OSAKA
{
    public partial class HowToWindow : Window
    {
        public HowToWindow()
        {
            InitializeComponent();
            Loaded += HowToWindow_Loaded;
        }

        private async void HowToWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "OSAKA使い方.md");

                // Markdownファイルの存在確認
                if (!File.Exists(path))
                {
                    MessageBox.Show(
                        $"OSAKA使い方.md が見つかりません。\n\n" +
                        $"探した場所:\n{path}",
                        "OSAKA使い方",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                // WebView2を初期化
                string userDataFolder = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData),
    "OSAKA",
    "WebView2");

                var environment =
                    await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        null,
                        userDataFolder);

                await MarkdownViewer.EnsureCoreWebView2Async(environment);

                // Markdownを読み込む
                string markdown = await File.ReadAllTextAsync(
                    path,
                    Encoding.UTF8);

                // HTMLへ変換
                string html = MarkdownToHtml(markdown);

                // 表示
                MarkdownViewer.NavigateToString(html);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "OSAKA使い方 読み込みエラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string MarkdownToHtml(string markdown)
        {
            var sb = new StringBuilder();

            sb.Append("""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
    html, body {
        background: #202020;
        color: #EEEEEE;
        margin: 0;
        padding: 0;
    }

    body {
        font-family: "Yu Gothic UI", "Meiryo", sans-serif;
        font-size: 16px;
        line-height: 1.8;
        padding: 35px 50px 50px 50px;
    }

    h1 {
        font-size: 30px;
        color: #FFFFFF;
        border-bottom: 1px solid #555555;
        padding-bottom: 10px;
        margin-top: 0;
        margin-bottom: 25px;
    }

    h2 {
        font-size: 22px;
        color: #FFFFFF;
        border-left: 4px solid #007ACC;
        padding-left: 12px;
        margin-top: 35px;
        margin-bottom: 18px;
    }

    p {
        margin: 12px 0;
    }

    strong {
        color: #FFFFFF;
        font-weight: bold;
    }

    code {
        background: #303030;
        padding: 2px 6px;
        border-radius: 3px;
        font-family: Consolas, monospace;
    }

    .example {
        background: #2B2B2B;
        border: 1px solid #444444;
        padding: 12px 16px;
        margin: 10px 0;
        border-radius: 4px;
        font-family: Consolas, monospace;
    }

    ul {
        padding-left: 25px;
    }

    li {
        margin: 5px 0;
    }

    hr {
        border: 0;
        border-top: 1px solid #444444;
        margin: 25px 0;
    }
</style>
</head>
<body>
""");

            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');

            bool inParagraph = false;
            var paragraph = new StringBuilder();

            void FlushParagraph()
            {
                if (!inParagraph)
                    return;

                string text = paragraph.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.Append("<p>");
                    sb.Append(FormatInlineMarkdown(text));
                    sb.Append("</p>");
                }

                paragraph.Clear();
                inParagraph = false;
            }

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph();
                    continue;
                }

                if (line.StartsWith("# "))
                {
                    FlushParagraph();

                    sb.Append("<h1>");
                    sb.Append(FormatInlineMarkdown(line[2..]));
                    sb.Append("</h1>");
                    continue;
                }

                if (line.StartsWith("## "))
                {
                    FlushParagraph();

                    sb.Append("<h2>");
                    sb.Append(FormatInlineMarkdown(line[3..]));
                    sb.Append("</h2>");
                    continue;
                }

                if (line == "---")
                {
                    FlushParagraph();
                    sb.Append("<hr>");
                    continue;
                }

                if (line.StartsWith("* "))
                {
                    FlushParagraph();

                    sb.Append("<ul>");
                    sb.Append("<li>");
                    sb.Append(FormatInlineMarkdown(line[2..]));
                    sb.Append("</li>");
                    sb.Append("</ul>");

                    continue;
                }

                if (inParagraph)
                    paragraph.Append("<br>");

                paragraph.Append(line);
                inParagraph = true;
            }

            FlushParagraph();

            sb.Append("""
</body>
</html>
""");

            return sb.ToString();
        }

        private static string FormatInlineMarkdown(string text)
        {
            text = WebUtility.HtmlEncode(text);

            // **太字**
            text = Regex.Replace(
                text,
                @"\*\*(.+?)\*\*",
                "<strong>$1</strong>");

            // `コード`
            text = Regex.Replace(
                text,
                @"`(.+?)`",
                "<code>$1</code>");

            return text;
        }

        private static string CreateErrorHtml(string message)
        {
            return """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
    body {
        background: #202020;
        color: #EEEEEE;
        font-family: "Yu Gothic UI", "Meiryo", sans-serif;
        padding: 40px;
        font-size: 16px;
    }
</style>
</head>
<body>
    <h2>使い方を表示できません</h2>
    <p>
""" + WebUtility.HtmlEncode(message) + """
    </p>
</body>
</html>
""";
        }
    }
}