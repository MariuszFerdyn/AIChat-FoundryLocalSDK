using System;
using System.IO;
using System.Windows.Forms;

namespace FoundrySTT
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

            if (!CheckPrerequisites()) return;

            Application.Run(new MainForm());
        }

        private static bool CheckPrerequisites()
        {
            string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            bool hasVcRuntime = File.Exists(Path.Combine(system32, "vcruntime140.dll"))
                             && File.Exists(Path.Combine(system32, "msvcp140.dll"));

            if (!hasVcRuntime)
            {
                var result = MessageBox.Show(
                    "⚠  Missing Dependency Detected\n\n" +
                    "This application requires the Microsoft Visual C++ 2019/2022 " +
                    "Redistributable (x64) to run.\n\n" +
                    "📥  Download link:\n" +
                    "https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
                    "Please install it and restart the application.\n\n" +
                    "Click Yes to continue anyway (may crash), or No to exit.",
                    "Missing Prerequisite — Visual C++ Redistributable 2022 x64",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (result == DialogResult.No) return false;
            }

            return true;
        }
    }
}
