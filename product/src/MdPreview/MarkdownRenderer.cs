using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace MdPreview
{
    internal static class MarkdownRenderer
    {
        private const int MaxBytes = 16 * 1024 * 1024;
        private const int MaxChars = 4 * 1024 * 1024;

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAlertBlocks()
            .UseAbbreviations()
            .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras()
            .UseFootnotes()
            .UseGridTables()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseEmojiAndSmiley(false) // :rocket: -> 🚀, but leave ASCII smileys like :) alone
            .UseMathematics()
            .UseDiagrams()
            .UseAutoLinks()
            .Build();

        private static readonly Regex FrontMatter = new Regex(
            @"\A---[ \t]*\r?\n(?<yaml>.*?)\r?\n(?:---|\.\.\.)[ \t]*(?:\r?\n|\z)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        public static string RenderFile(string path, bool dark, Color background, bool allowRemoteImages = false)
        {
            bool truncated;
            string text = ReadText(path, out truncated);
            return RenderDocument(text, truncated, dark, background, allowRemoteImages);
        }

        public static string RenderDocument(string markdown, bool truncated, bool dark, Color background, bool allowRemoteImages = false)
        {
            string frontMatter = null;
            Match m = FrontMatter.Match(markdown);
            if (m.Success)
            {
                frontMatter = m.Groups["yaml"].Value;
                markdown = markdown.Substring(m.Length);
            }

            string body = Markdown.ToHtml(markdown, Pipeline);
            string theme = dark ? "dark" : "light";
            string assets = PreviewControl.AssetsPrefix;

            var sb = new StringBuilder(body.Length + 2048);
            sb.Append("<!DOCTYPE html><html lang=\"en\" data-theme=\"").Append(theme)
              .Append("\" data-remote-images=\"").Append(allowRemoteImages ? "1" : "0")
              .Append("\" style=\"--mdp-bg:").Append(Theme.ToCss(background)).Append("\"><head><meta charset=\"utf-8\">")
              .Append("<meta name=\"color-scheme\" content=\"").Append(theme).Append("\">")
              .Append("<link rel=\"stylesheet\" href=\"").Append(assets).Append("preview.css\">")
              .Append("</head><body><article class=\"markdown-body\">");

            if (frontMatter != null)
            {
                sb.Append("<details class=\"mdp-frontmatter\"><summary>Front matter</summary><pre><code class=\"language-yaml\">")
                  .Append(WebUtility.HtmlEncode(frontMatter))
                  .Append("</code></pre></details>");
            }

            sb.Append(body);

            if (truncated)
            {
                sb.Append("<p class=\"mdp-note\">This file is large. The preview shows its beginning only.</p>");
            }

            sb.Append("</article><script src=\"").Append(assets).Append("app.js\"></script></body></html>");
            return sb.ToString();
        }

        private static string ReadText(string path, out bool truncated)
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long length = Math.Min(stream.Length, MaxBytes);
                bytes = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = stream.Read(bytes, read, (int)(length - read));
                    if (n <= 0)
                    {
                        break;
                    }

                    read += n;
                }

                truncated = stream.Length > MaxBytes;
                if (read < length)
                {
                    Array.Resize(ref bytes, read);
                }
            }

            string text = Decode(bytes);
            if (text.Length > MaxChars)
            {
                text = text.Substring(0, MaxChars);
                truncated = true;
            }

            return text;
        }

        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            var strict = new UTF8Encoding(false, true);

            // A truncated read can cut a multi-byte character; drop up to 3 trailing bytes before giving up on UTF-8.
            for (int cut = 0; cut <= 3 && bytes.Length - offset - cut >= 0; cut++)
            {
                try
                {
                    return strict.GetString(bytes, offset, bytes.Length - offset - cut);
                }
                catch (DecoderFallbackException)
                {
                }
            }

            // Legacy files saved in the system ANSI code page.
            return Encoding.Default.GetString(bytes);
        }
    }
}
