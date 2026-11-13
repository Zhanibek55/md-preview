using System;
using System.Runtime.InteropServices;

namespace MdPreview
{
    /// <summary>
    /// Explorer preview handler for Markdown files. Explorer loads it into prevhost.exe,
    /// gives it a file path and a parent window, and asks it to draw a preview.
    /// COM may call these methods on an STA or an MTA thread, so every piece of UI work is
    /// forwarded to one dedicated STA thread (<see cref="UiThread"/>).
    /// </summary>
    [ComVisible(true)]
    [Guid(Clsid)]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("MdPreview.MarkdownPreviewHandler")]
    public sealed class MarkdownPreviewHandler : IPreviewHandler, IInitializeWithFile, IObjectWithSite, IOleWindow, IPreviewHandlerVisuals
    {
        public const string Clsid = "d6ef0d24-13b8-4841-b4ce-b190114f4b51";

        static MarkdownPreviewHandler()
        {
            DependencyResolver.Install();
        }

        private string _filePath;
        private IntPtr _parentHwnd;
        private RECT _rect;
        private object _site;
        private IPreviewHandlerFrame _frame;
        private uint? _backgroundColor;

        // Touched only on the UI thread.
        private PreviewControl _control;

        // IInitializeWithFile
        public void Initialize(string pszFilePath, uint grfMode)
        {
            _filePath = pszFilePath;
        }

        // IPreviewHandler
        public void SetWindow(IntPtr hwnd, ref RECT rect)
        {
            _parentHwnd = hwnd;
            _rect = rect;
            RECT bounds = rect;
            UiThread.Run(() =>
            {
                if (_control != null && _control.IsHandleCreated)
                {
                    Native.SetParent(_control.Handle, hwnd);
                    Move(bounds);
                }
            });
        }

        public void SetRect(ref RECT rect)
        {
            _rect = rect;
            RECT bounds = rect;
            UiThread.Run(() => Move(bounds));
        }

        public void DoPreview()
        {
            IntPtr parent = _parentHwnd;
            string file = _filePath;
            RECT bounds = _rect;
            uint? background = _backgroundColor;
            if (parent == IntPtr.Zero || string.IsNullOrEmpty(file))
            {
                return;
            }

            UiThread.Run(() =>
            {
                if (_control == null)
                {
                    bool dark = Theme.IsDark(background);
                    _control = new PreviewControl(parent, dark, Theme.Background(background, dark));
                    IntPtr unused = _control.Handle; // creates the child window under Explorer's pane
                }

                Move(bounds);
                _control.Display(file);
            });
        }

        public void Unload()
        {
            UiThread.Run(() =>
            {
                if (_control != null)
                {
                    _control.Dispose();
                    _control = null;
                }
            });
            _filePath = null;
        }

        public void SetFocus()
        {
            UiThread.Run(() => _control?.FocusContent());
        }

        public void QueryFocus(out IntPtr phwnd)
        {
            phwnd = UiThread.Run(Native.GetFocus);
        }

        public int TranslateAccelerator(ref MSG pmsg)
        {
            try
            {
                return _frame != null ? _frame.TranslateAccelerator(ref pmsg) : Native.S_FALSE;
            }
            catch
            {
                return Native.S_FALSE;
            }
        }

        // IObjectWithSite
        public void SetSite(object pUnkSite)
        {
            _site = pUnkSite;
            _frame = pUnkSite as IPreviewHandlerFrame;
        }

        public void GetSite(ref Guid riid, out object ppvSite)
        {
            ppvSite = _site;
        }

        // IOleWindow
        public void GetWindow(out IntPtr phwnd)
        {
            phwnd = UiThread.Run(() => _control != null && _control.IsHandleCreated ? _control.Handle : _parentHwnd);
        }

        public void ContextSensitiveHelp(bool fEnterMode)
        {
            throw new NotImplementedException();
        }

        // IPreviewHandlerVisuals
        public void SetBackgroundColor(uint color)
        {
            _backgroundColor = color;
        }

        public void SetFont(IntPtr plf)
        {
        }

        public void SetTextColor(uint color)
        {
        }

        private void Move(RECT bounds)
        {
            if (_control != null && _control.IsHandleCreated)
            {
                Native.MoveWindow(_control.Handle, bounds.Left, bounds.Top, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height), true);
            }
        }
    }
}
