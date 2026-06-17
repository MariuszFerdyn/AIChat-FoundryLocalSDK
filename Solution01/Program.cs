using System;
using System.IO;
using System.Windows.Forms;

namespace FoundryQA;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();

        if (!CheckPrerequisites())
            return;

        using var picker = new ModelSelectForm();
        if (picker.ShowDialog() != DialogResult.OK || picker.SelectedAlias is null)
            return;

        Application.Run(new MainForm(picker.SelectedAlias));
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
                "Redistributable (x64) to run the AI inference engine (ONNX Runtime).\n\n" +
                "It was not detected on your system.\n\n" +
                "📥  Download link:\n" +
                "https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
                "Please install it and restart the application.\n\n" +
                "Click Yes to continue anyway (may crash), or No to exit.",
                "Missing Prerequisite — Visual C++ Redistributable 2022 x64",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            return result == DialogResult.Yes;
        }

        return true;
    }
}

