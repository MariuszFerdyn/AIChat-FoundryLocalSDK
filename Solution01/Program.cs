using System;
using System.Windows.Forms;

namespace FoundryQA;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();

        using var picker = new ModelSelectForm();
        if (picker.ShowDialog() != DialogResult.OK || picker.SelectedAlias is null)
            return;

        Application.Run(new MainForm(picker.SelectedAlias));
    }
}

