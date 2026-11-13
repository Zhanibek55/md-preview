using System;
using System.Threading;
using System.Windows.Forms;

namespace MdPreview
{
    /// <summary>
    /// One background STA thread with a WinForms message loop per process. WebView2 and the
    /// preview windows live here, whatever apartment COM uses to call the handler.
    /// </summary>
    internal static class UiThread
    {
        private static readonly object Gate = new object();
        private static Control _marshaller;

        public static void Run(Action action)
        {
            Run<object>(() =>
            {
                action();
                return null;
            });
        }

        public static T Run<T>(Func<T> func)
        {
            try
            {
                Control marshaller = Marshaller();
                return marshaller.InvokeRequired ? (T)marshaller.Invoke(func) : func();
            }
            catch (Exception ex)
            {
                Log.Error("UI call", ex);
                return default(T);
            }
        }

        private static Control Marshaller()
        {
            lock (Gate)
            {
                if (_marshaller != null)
                {
                    return _marshaller;
                }

                using (var ready = new ManualResetEventSlim())
                {
                    var thread = new Thread(() =>
                    {
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                        var control = new Control();
                        IntPtr unused = control.Handle;
                        _marshaller = control;
                        ready.Set();

                        // Start Chromium booting now, overlapping with the handler's first parse.
                        PreviewControl.Prewarm();

                        Application.Run();
                    })
                    {
                        IsBackground = true,
                        Name = "MdPreview UI",
                    };
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    ready.Wait();
                }

                return _marshaller;
            }
        }
    }
}
