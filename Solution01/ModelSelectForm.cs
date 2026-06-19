using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryQA;

public partial class ModelSelectForm : Form
{
    public string? SelectedAlias { get; private set; }

    private readonly List<ModelRow> _rows = new();
    private FoundryLocalManager? _manager;
    private long _ramMb;

    public ModelSelectForm()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    private record ModelRow(
        string Alias,
        string DisplayName,
        long SizeMb,
        long ContextK,
        string Device,
        bool Cached,
        string Fit,
        Color FitColor);

    private async Task LoadAsync()
    {
        SetStatus("Detecting hardware…");
        LoadHardwareInfo();

        SetStatus("Initialising Foundry Local SDK…");
        try
        {
            var config = new Configuration { AppName = "FoundryQA" };
            await FoundryLocalManager.CreateAsync(config, NullLogger.Instance);
            _manager = FoundryLocalManager.Instance;

            SetStatus("Fetching model catalog…");
            var catalog = await _manager.GetCatalogAsync();
            var models = (await catalog.ListModelsAsync())
                .Where(m => m.Info?.Task == "chat-completion")
                .OrderBy(m => m.Info?.FileSizeMb ?? 0)
                .ToList();

            foreach (var m in models)
            {
                var sizeMb = m.Info?.FileSizeMb ?? 0;
                var ctx    = (m.Info?.ContextLength ?? 0) / 1024;
                var dev    = m.Info?.Runtime.DeviceType.ToString() ?? "CPU";
                var cached = m.Info?.Cached ?? false;
                var (fit, fitColor) = GetFit(sizeMb, _ramMb);

                _rows.Add(new ModelRow(
                    m.Alias,
                    m.Info?.DisplayName ?? m.Alias,
                    sizeMb,
                    ctx,
                    dev,
                    cached,
                    fit,
                    fitColor));
            }

            PopulateGrid();
            SetStatus($"{_rows.Count} models available  •  select one and click Load");
            btnLoad.Enabled = true;
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void LoadHardwareInfo()
    {
        try
        {
            // CPU
            using var cpuSearch = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
            var cpu = cpuSearch.Get().Cast<ManagementObject>().FirstOrDefault();
            string cpuName  = cpu?["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
            string cpuCores = cpu?["NumberOfCores"]?.ToString() ?? "?";

            // RAM
            using var ramSearch = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            var ram = ramSearch.Get().Cast<ManagementObject>().FirstOrDefault();
            long ramBytes = Convert.ToInt64(ram?["TotalPhysicalMemory"] ?? 0L);
            _ramMb = ramBytes / 1024 / 1024;
            double ramGb = Math.Round(_ramMb / 1024.0, 1);

            // GPU
            using var gpuSearch = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            var gpus = gpuSearch.Get().Cast<ManagementObject>()
                .Select(g => g["Name"]?.ToString()?.Trim() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
            string gpuText = gpus.Any() ? string.Join(" / ", gpus) : "No GPU detected";

            InvokeIfNeeded(() =>
            {
                lblCpu.Text = $"🖥  CPU:  {cpuName}  ({cpuCores} cores)";
                lblRam.Text = $"🧠  RAM:  {ramGb} GB";
                lblGpu.Text = $"⚡  GPU:  {gpuText}";

                // Recommendation hint
                string hint = _ramMb switch
                {
                    < 6144  => "Recommend: small models ≤ 1 GB  (e.g. qwen2.5-0.5b)",
                    < 10240 => "Recommend: models up to ~3 GB  (e.g. phi-3-mini-128k, phi-4-mini)",
                    < 20480 => "Recommend: models up to ~8 GB  (e.g. phi-4, qwen2.5-7b)",
                    _       => "Plenty of RAM — any model should fit comfortably",
                };
                lblHint.Text = $"💡  {hint}";
            });
        }
        catch { /* best-effort */ }
    }

    private (string label, Color color) GetFit(long sizeMb, long ramMb)
    {
        if (ramMb == 0) return ("?", Color.Gray);
        double ratio = (double)sizeMb / ramMb;
        return ratio switch
        {
            < 0.40 => ("✅ Great fit", Color.FromArgb(80, 220, 130)),
            < 0.65 => ("✔  Fits",      Color.FromArgb(180, 220, 80)),
            < 0.85 => ("⚠  Tight",     Color.FromArgb(255, 180, 50)),
            _      => ("❌ Too large",  Color.FromArgb(255, 90, 90)),
        };
    }

    private void PopulateGrid()
    {
        InvokeIfNeeded(() =>
        {
            dgv.Rows.Clear();
            foreach (var r in _rows)
            {
                double sizeGb = Math.Round(r.SizeMb / 1024.0, 2);
                string ctxLabel = r.ContextK >= 1 ? $"{r.ContextK}K" : $"{r.SizeMb} MB ctx";
                string cached = r.Cached ? "✓ cached" : "";

                int idx = dgv.Rows.Add(
                    r.Alias,
                    sizeGb > 0 ? $"{sizeGb:F1} GB" : "—",
                    r.ContextK > 0 ? ctxLabel : "—",
                    r.Device,
                    cached,
                    r.Fit);

                var row = dgv.Rows[idx];
                row.Tag = r.Alias;

                // Colour the Fit cell
                row.Cells[5].Style.ForeColor = r.FitColor;
                row.Cells[5].Style.Font = new Font(dgv.Font, FontStyle.Bold);

                // Highlight already-cached rows
                if (r.Cached)
                    row.DefaultCellStyle.BackColor = Color.FromArgb(28, 48, 30);
            }

            // Pre-select phi-3-mini-128k if present
            var defaultRow = dgv.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => r.Tag?.ToString()?.Contains("phi-3-mini-128k") == true);
            if (defaultRow != null)
            {
                defaultRow.Selected = true;
                dgv.FirstDisplayedScrollingRowIndex = defaultRow.Index;
            }
        });
    }

    private void BtnLoad_Click(object? sender, EventArgs e)
    {
        if (dgv.SelectedRows.Count == 0) return;
        SelectedAlias = dgv.SelectedRows[0].Tag?.ToString();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetStatus(string text) =>
        InvokeIfNeeded(() => lblStatus.Text = text);

    private void InvokeIfNeeded(Action a)
    {
        if (InvokeRequired) Invoke(a); else a();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            DialogResult = DialogResult.Cancel;
        base.OnFormClosing(e);
    }
}
