using System.Drawing;
using System.IO;

namespace MdPreview
{
    /// <summary>
    /// Public facade so test tools can render a page exactly as the preview does, without
    /// hosting the COM handler. Not used by Explorer.
    /// </summary>
    public static class PreviewApi
    {
        public static string Host => PreviewControl.Host;

        public static string PagePath => PreviewControl.PagePath;

        public static string AssetsPrefix => PreviewControl.AssetsPrefix;

        public static string AssetsDir => Path.Combine(DependencyResolver.BaseDir, "assets");

        public static string RenderFileToHtml(string path, bool dark)
        {
            Color background = dark ? Color.FromArgb(0x19, 0x19, 0x19) : Color.White;
            return MarkdownRenderer.RenderFile(path, dark, background);
        }
    }
}
