#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace FoundryQA;

partial class ModelSelectForm
{
    private System.ComponentModel.IContainer? components;

    // ── Hardware panel ────────────────────────────────────────────────────────
    private Panel pnlHardware;
    private Label lblHwTitle;
    private Label lblCpu;
    private Label lblRam;
    private Label lblGpu;
    private Label lblHint;

    // ── Grid ──────────────────────────────────────────────────────────────────
    private DataGridView dgv;

    // ── Status / buttons ─────────────────────────────────────────────────────
    private Panel pnlBottom;
    private Label lblStatus;
    private Button btnLoad;
    private Button btnCancel;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        var dark       = Color.FromArgb(18, 18, 26);
        var darkPanel  = Color.FromArgb(26, 26, 36);
        var darkBorder = Color.FromArgb(45, 45, 65);
        var accent     = Color.FromArgb(100, 160, 255);
        var fg         = Color.FromArgb(220, 220, 235);
        var fgDim      = Color.FromArgb(130, 130, 160);
        var baseFont   = new Font("Segoe UI", 9.5f);

        Text            = "Select a Model — Foundry Local Q&A";
        Size            = new Size(1020, 640);
        MinimumSize     = new Size(780, 500);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = dark;
        ForeColor       = fg;
        Font            = baseFont;

        // ── Hardware panel ────────────────────────────────────────────────────
        pnlHardware = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 130,
            BackColor = darkPanel,
            Padding   = new Padding(14, 10, 14, 6),
        };

        lblHwTitle = new Label
        {
            Text      = "Your Hardware",
            Font      = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            ForeColor = accent,
            AutoSize  = true,
            Location  = new Point(14, 10),
        };

        lblCpu = new Label { AutoSize = true, ForeColor = fg,    Location = new Point(16, 36), Font = baseFont };
        lblRam = new Label { AutoSize = true, ForeColor = fg,    Location = new Point(16, 58), Font = baseFont };
        lblGpu = new Label { AutoSize = true, ForeColor = fg,    Location = new Point(16, 80), Font = baseFont };
        lblHint = new Label
        {
            AutoSize  = true,
            ForeColor = Color.FromArgb(255, 210, 80),
            Location  = new Point(16, 104),
            Font      = new Font("Segoe UI", 9f, FontStyle.Italic),
            Text      = "💡  Detecting…",
        };

        pnlHardware.Controls.AddRange(new Control[] { lblHwTitle, lblCpu, lblRam, lblGpu, lblHint });

        // ── DataGridView ──────────────────────────────────────────────────────
        dgv = new DataGridView
        {
            Dock                  = DockStyle.Fill,
            BackgroundColor       = dark,
            GridColor             = darkBorder,
            BorderStyle           = BorderStyle.None,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            CellBorderStyle       = DataGridViewCellBorderStyle.SingleHorizontal,
            SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect           = false,
            ReadOnly              = true,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible     = false,
            Font                  = new Font("Cascadia Code", 9f),
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
            RowTemplate           = { Height = 28 },
        };

        // Header style
        dgv.ColumnHeadersDefaultCellStyle.BackColor  = darkPanel;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor  = accent;
        dgv.ColumnHeadersDefaultCellStyle.Font       = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = darkPanel;
        dgv.ColumnHeadersHeight = 30;
        dgv.EnableHeadersVisualStyles = false;

        // Row style
        dgv.DefaultCellStyle.BackColor          = dark;
        dgv.DefaultCellStyle.ForeColor          = fg;
        dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 80, 160);
        dgv.DefaultCellStyle.SelectionForeColor = Color.White;
        dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(22, 22, 34);

        // Columns
        dgv.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Alias",   HeaderText = "Model Alias",   FillWeight = 25 },
            new DataGridViewTextBoxColumn { Name = "Size",    HeaderText = "Download Size",  FillWeight = 12, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "Context", HeaderText = "Context",        FillWeight = 10, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "Device",  HeaderText = "Device",         FillWeight = 10 },
            new DataGridViewTextBoxColumn { Name = "Cached",  HeaderText = "Status",         FillWeight = 10, DefaultCellStyle = { ForeColor = Color.FromArgb(80, 200, 120) } },
            new DataGridViewTextBoxColumn { Name = "Fit",     HeaderText = "Fit for Your RAM", FillWeight = 18 }
        );

        dgv.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) BtnLoad_Click(null, EventArgs.Empty); };

        // ── Bottom bar ────────────────────────────────────────────────────────
        pnlBottom = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 48,
            BackColor = darkPanel,
            Padding   = new Padding(12, 8, 12, 8),
        };

        lblStatus = new Label
        {
            Dock      = DockStyle.Fill,
            ForeColor = fgDim,
            Font      = new Font("Segoe UI", 9f, FontStyle.Italic),
            TextAlign = ContentAlignment.MiddleLeft,
            Text      = "Loading catalog…",
        };

        btnLoad = new Button
        {
            Text      = "Load Model  ➤",
            Width     = 140,
            Dock      = DockStyle.Right,
            BackColor = Color.FromArgb(50, 100, 220),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Enabled   = false,
        };
        btnLoad.FlatAppearance.BorderSize = 0;
        btnLoad.Click += BtnLoad_Click;

        btnCancel = new Button
        {
            Text      = "Cancel",
            Width     = 80,
            Dock      = DockStyle.Right,
            BackColor = Color.FromArgb(45, 45, 60),
            ForeColor = Color.Silver,
            FlatStyle = FlatStyle.Flat,
        };
        btnCancel.FlatAppearance.BorderColor = darkBorder;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        pnlBottom.Controls.Add(lblStatus);
        pnlBottom.Controls.Add(btnLoad);
        pnlBottom.Controls.Add(btnCancel);

        // ── Wire to form ──────────────────────────────────────────────────────
        Controls.Add(dgv);
        Controls.Add(pnlBottom);
        Controls.Add(pnlHardware);

        AcceptButton = btnLoad;
        CancelButton = btnCancel;
    }
}
