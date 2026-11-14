using System;
using System.Windows.Forms;

namespace Caplib
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            NativeMethods.EnablePerMonitorDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
