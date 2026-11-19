using System;
using System.Reflection;
using System.Windows.Forms;

// Executable properties use the same name as the window and package.
[assembly: AssemblyTitle("Quick Trigger")]
[assembly: AssemblyProduct("Quick Trigger")]
[assembly: AssemblyDescription("Screen-region color detection and input testing")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace QuickTrigger
{
    internal static class Program
    {
        // STA is required by WinForms dialogs, clipboard support and window controls.
        [STAThread]
        private static int Main(string[] arguments)
        {
            WindowsApi.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (arguments.Length > 0 && arguments[0] == "--self-test")
            {
                string report = arguments.Length > 1 ? arguments[1] : "test-results.txt";
                return SelfTests.Run(report);
            }
            Application.Run(new MainForm());
            return 0;
        }
    }
}
