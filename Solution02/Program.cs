using System;
using System.IO;
using System.Windows.Forms;

namespace FoundryChatApp
{
    internal static class Program
    {
        public static readonly string LogFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                string msg = e.ExceptionObject?.ToString() ?? "Unknown error";
                File.WriteAllText(LogFile, msg);
                MessageBox.Show(msg, "Unhandled Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.ThreadException += (s, e) =>
            {
                string msg = e.Exception?.ToString() ?? "Unknown error";
                File.WriteAllText(LogFile, msg);
                MessageBox.Show(msg, "Thread Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
