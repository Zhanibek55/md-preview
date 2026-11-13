using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using MdPreview;

namespace MdPreview.ClickTest
{
    /// <summary>
    /// Runs the real preview page (the same app.js) in a WebView2 and clicks every in-document
    /// link, checking that the page scrolls to the matching heading. Exercises the link-resolution
    /// and scroll code without hosting the COM handler.
    ///
    /// Usage: MdPreviewClickTest file.md [--theme dark|light]
    /// Exit 0 if every link scrolled to its target, 1 otherwise, 2 on setup failure.
    ///
    /// There is no Application.Run loop here, so every async WebView2 call is driven by a manual
    /// message pump (see <see cref="Wait"/>).
    /// </summary>
    internal static class Program
    {
        private static readonly List<string> Messages = new List<string>();
        private static CoreWebView2 _core;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: MdPreviewClickTest file.md [--theme dark|light]");
                return 2;
            }

            // Make WebView2's internal awaits resume on this STA thread (which PumpUntil drives),
            // not on a thread-pool thread where the controller cannot be touched.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            string file = Path.GetFullPath(args[0]);
            bool dark = Array.IndexOf(args, "dark") >= 0;
            string findTerm = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--find") findTerm = args[i + 1];
            }

            string temp = Path.Combine(Path.GetTempPath(), "mdp-clicktest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                return Run(file, dark, temp, findTerm);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex);
                return 2;
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private static int Run(string file, bool dark, string temp, string findTerm)
        {
            string page = PreviewApi.RenderFileToHtml(file, dark);
            File.WriteAllText(Path.Combine(temp, PreviewApi.PagePath.TrimStart('/')), page, new UTF8Encoding(false));
            // Copy, not a junction: WebView2's virtual host does not serve files through a junction.
            string assetsLink = Path.Combine(temp, PreviewApi.AssetsPrefix.Trim('/'));
            CopyDir(PreviewApi.AssetsDir, assetsLink);

            var form = new Form { ClientSize = new System.Drawing.Size(900, 1000), Text = "ClickTest" };
            form.Show();

            // Use the Core controller directly (not the WinForms wrapper, which needs Application.Run).
            CoreWebView2Environment env = Wait(CoreWebView2Environment.CreateAsync(null, Path.Combine(temp, "wv2"), null));
            CoreWebView2Controller controller = Wait(env.CreateCoreWebView2ControllerAsync(form.Handle));
            controller.Bounds = new System.Drawing.Rectangle(0, 0, form.ClientSize.Width, form.ClientSize.Height);
            controller.IsVisible = true;
            _core = controller.CoreWebView2;
            _core.SetVirtualHostNameToFolderMapping(PreviewApi.Host, temp, CoreWebView2HostResourceAccessKind.Allow);
            _core.WebMessageReceived += (s, e) => Messages.Add(e.WebMessageAsJson ?? "");
            string navError = null;
            _core.NavigationCompleted += (s, e) => { if (!e.IsSuccess) navError = e.WebErrorStatus.ToString(); };

            _core.Navigate("https://" + PreviewApi.Host + PreviewApi.PagePath);
            if (!PumpUntil(() => Messages.Exists(m => m.Contains("\"type\":\"rendered\"")), 20000))
            {
                Console.Error.WriteLine("page did not render. navError=" + (navError ?? "none"));
                Console.Error.WriteLine("readyState=" + Eval("document.readyState") +
                    " webview=" + Eval("String(!!(window.chrome&&window.chrome.webview))") +
                    " scripts=" + Eval("String(document.querySelectorAll('script').length)") +
                    " headings=" + Eval("String(document.querySelectorAll('article h2').length)") +
                    " rendered=" + Eval("String(document.documentElement.getAttribute('data-rendered'))"));
                Console.Error.WriteLine("appjs fetch=" + Eval(
                    "(function(){try{var x=new XMLHttpRequest();x.open('GET','/__mdp_assets__/app.js',false);x.send();" +
                    "return x.status+' ct='+x.getResponseHeader('content-type')+' len='+x.responseText.length;}catch(e){return 'err:'+e.message;}})()"));
                Console.Error.WriteLine("pagehtml tail=" + Eval("document.body.lastElementChild ? document.body.lastElementChild.outerHTML.slice(0,120) : 'none'"));
                Console.Error.WriteLine("messages: " + string.Join(" | ", Messages));
                return 2;
            }

            string hrefsJson = Eval("JSON.stringify(Array.prototype.map.call(" +
                "document.querySelectorAll('article a[href^=\\u0022#\\u0022]'), function(a){return a.getAttribute('href');}))");
            List<string> hrefs = ParseJsonStringArray(hrefsJson);

            var seen = new HashSet<string>();
            var distinct = new List<string>();
            foreach (string h in hrefs)
            {
                if (seen.Add(h)) distinct.Add(h);
            }

            Console.WriteLine("Document links: " + hrefs.Count + " (" + distinct.Count + " distinct targets)");

            int passed = 0, failed = 0;
            foreach (string href in distinct)
            {
                // Click the real link and let app.js resolve and scroll. Judge by the element app.js
                // reports it scrolled to (its "anchor" message), read back from the live DOM.
                Messages.Clear();
                Eval("window.scrollTo(0, 0)");
                bool clicked = Eval(ClickScript(href)).Contains("\"clicked\":true");
                PumpUntil(() => Messages.Exists(m => m.Contains("\"type\":\"anchor\"") || m.Contains("\"type\":\"anchor-missing\"")), 2000);

                string anchorMsg = Messages.Find(m => m.Contains("\"type\":\"anchor\""));
                double y = ReadNumber(Eval("String(Math.round(window.scrollY))"), null);

                bool ok;
                string note;
                if (!clicked)
                {
                    ok = false; note = "link not found in page";
                }
                else if (anchorMsg == null)
                {
                    ok = false; note = "app.js did not resolve the link (scrollY=" + (int)y + ")";
                }
                else
                {
                    // getBoundingClientRect().top of the element app.js scrolled to.
                    double top = ReadNumber(Eval(
                        "(function(){var e=document.querySelector('[data-mdp-anchored]');" +
                        "return e?String(Math.round(e.getBoundingClientRect().top)):'9999';})()"), null);
                    string targetId = ExtractString(anchorMsg, "target");
                    if (top >= -2 && top <= 40)
                    {
                        ok = true; note = "scrolled to \"" + targetId + "\" at top (scrollY=" + (int)y + ", top=" + (int)top + ")";
                    }
                    else
                    {
                        ok = false; note = "resolved \"" + targetId + "\" but it is not at the top (top=" + (int)top + ", scrollY=" + (int)y + ")";
                    }
                }

                Console.WriteLine((ok ? "  PASS " : "  FAIL ") + Shorten(href) + " — " + note);
                if (ok) passed++; else failed++;
            }

            // An unknown anchor must be reported, not silently matched to something.
            Messages.Clear();
            Eval("window.scrollTo(0, 0)");
            string probe = Eval(
                "(function(){var a=document.createElement('a');a.href='#zzz-does-not-exist-" + Guid.NewGuid().ToString("N") + "';" +
                "a.textContent='x';document.querySelector('article').appendChild(a);a.click();return String(window.scrollY);})()");
            PumpUntil(() => Messages.Exists(m => m.Contains("\"type\":\"anchor-missing\"")), 1000);
            bool reportedMissing = Messages.Exists(m => m.Contains("\"type\":\"anchor-missing\""));
            bool stayed = ReadNumber(probe, null) <= 2;
            Console.WriteLine((reportedMissing && stayed ? "  PASS " : "  FAIL ") +
                "#unknown-anchor — reported missing=" + reportedMissing + ", no scroll=" + stayed);
            if (!(reportedMissing && stayed)) failed++;

            // Find in page (Ctrl+F): the real app.js search must highlight the matches.
            if (findTerm != null)
            {
                string supported = Eval("String(!!(window.__mdpFind && window.__mdpFind.supported))");
                double hits = ReadNumber(Eval(
                    "(function(){try{return String(window.__mdpFind.search('" + JsForAttr(findTerm) + "'));}catch(e){return '-1';}})()"), null);
                bool ok = supported == "true" ? hits >= 1 : true; // older runtimes without the Highlight API: skip
                Console.WriteLine((ok ? "  PASS " : "  FAIL ") + "find \"" + findTerm + "\" — hits=" + (int)hits + ", highlightApi=" + supported);
                if (!ok) failed++;
            }

            // Outline: when present, clicking an entry scrolls to that heading.
            bool hasOutline = Eval("String(!!document.querySelector('.mdp-outline'))") == "true";
            if (hasOutline)
            {
                Messages.Clear();
                Eval("window.scrollTo(0, 0)");
                Eval("document.body.classList.add('mdp-outline-open')");
                Eval("(function(){var a=document.querySelectorAll('.mdp-outline a');return a.length>1?(a[a.length-1].click(),'ok'):'few';})()");
                PumpUntil(() => Messages.Exists(m => m.Contains("\"type\":\"anchor\"")), 2000);
                double y = ReadNumber(Eval("String(Math.round(window.scrollY))"), null);
                bool ok = Messages.Exists(m => m.Contains("\"type\":\"anchor\"")) && y > 2;
                Console.WriteLine((ok ? "  PASS " : "  FAIL ") + "outline — entry click scrolled (scrollY=" + (int)y + ")");
                if (!ok) failed++;
            }

            form.Close();
            Console.WriteLine("Result: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        // Click the first link that has this exact href (a real DOM click, so app.js handles it).
        private static string ClickScript(string href)
        {
            return
                "(function(){" +
                "var links=document.querySelectorAll('a[href=\\u0022" + JsForAttr(href) + "\\u0022]');" +
                "if(!links.length){return JSON.stringify({clicked:false});}" +
                "links[0].click();" +
                "return JSON.stringify({clicked:true});" +
                "})()";
        }

        private static string ExtractString(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\":\"", StringComparison.Ordinal);
            if (i < 0) return "";
            int start = i + key.Length + 4;
            int end = json.IndexOf('"', start);
            return end < 0 ? "" : json.Substring(start, end - start);
        }

        private static string Shorten(string href)
        {
            try { return "#" + Uri.UnescapeDataString(href.Substring(1)); } catch { return href; }
        }

        private static string Eval(string script)
        {
            Task<string> task = _core.ExecuteScriptAsync(script);
            PumpUntil(() => task.IsCompleted, 10000);
            string json = task.IsCompleted ? task.Result : "";
            if (json != null && json.Length >= 2 && json[0] == '"')
            {
                return System.Text.RegularExpressions.Regex.Unescape(json.Substring(1, json.Length - 2));
            }

            return json ?? "";
        }

        private static T Wait<T>(Task<T> task)
        {
            PumpUntil(() => task.IsCompleted, 30000);
            return task.Result;
        }

        private static void Wait(Task task)
        {
            PumpUntil(() => task.IsCompleted, 30000);
            task.GetAwaiter().GetResult();
        }

        private static bool PumpUntil(Func<bool> done, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                if (done()) return true;
                Thread.Sleep(10);
            }

            return done();
        }

        private static string JsForAttr(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\u0022");

        private static List<string> ParseJsonStringArray(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(json)) return list;
            var sb = new StringBuilder();
            bool inString = false, escape = false;
            foreach (char c in json)
            {
                if (escape) { sb.Append(c); escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { if (inString) { list.Add(sb.ToString()); sb.Clear(); } inString = !inString; continue; }
                if (inString) sb.Append(c);
            }

            return list;
        }

        private static double ReadNumber(string json, string key)
        {
            string text = json;
            if (key != null)
            {
                int i = json.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
                if (i < 0) return double.NaN;
                text = json.Substring(i + key.Length + 3);
            }

            var sb = new StringBuilder();
            foreach (char c in text.TrimStart('"'))
            {
                if (char.IsDigit(c) || c == '-' || c == '.') sb.Append(c);
                else break;
            }

            return double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
        }

        private static void Junction(string link, string target)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    CopyDir(target, link);
                }
            }
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(from, to));
            }

            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(f, f.Replace(from, to), true);
            }
        }
    }
}
