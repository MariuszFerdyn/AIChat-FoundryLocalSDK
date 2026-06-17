namespace FoundryQA;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    // ── Top bar ──────────────────────────────────────────
    private System.Windows.Forms.Panel pnlTop;
    private System.Windows.Forms.Label lblTitle;
    private System.Windows.Forms.Label lblModel;
    private System.Windows.Forms.Button btnClear;

    // ── Chat display ─────────────────────────────────────
    private System.Windows.Forms.RichTextBox rtbChat;

    // ── Status strip ─────────────────────────────────────
    private System.Windows.Forms.Panel pnlStatus;
    private System.Windows.Forms.Label lblStatus;
    private System.Windows.Forms.ProgressBar progressBar;

    // ── Input bar ────────────────────────────────────────
    private System.Windows.Forms.Panel pnlInput;
    private System.Windows.Forms.RichTextBox rtbInput;
    private System.Windows.Forms.Button btnSend;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        this.Text = "Foundry Local Q&A — Phi-3 Mini 128k Instruct";
        this.Size = new System.Drawing.Size(900, 700);
        this.MinimumSize = new System.Drawing.Size(640, 480);
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.BackColor = System.Drawing.Color.FromArgb(18, 18, 24);
        this.ForeColor = System.Drawing.Color.WhiteSmoke;
        this.Font = new System.Drawing.Font("Segoe UI", 10f);

        // ── Top bar ──────────────────────────────────────
        pnlTop = new System.Windows.Forms.Panel
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            Height = 58,
            BackColor = System.Drawing.Color.FromArgb(26, 26, 36),
            Padding = new System.Windows.Forms.Padding(12, 0, 12, 0),
        };

        lblTitle = new System.Windows.Forms.Label
        {
            Text = "⚡ Foundry Local Q&A",
            Font = new System.Drawing.Font("Segoe UI Semibold", 14f, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(150, 200, 255),
            AutoSize = true,
            Location = new System.Drawing.Point(14, 14),
        };

        lblModel = new System.Windows.Forms.Label
        {
            Text = "Model: phi-3-mini-128k-instruct  •  Foundry Local (Orion)",
            Font = new System.Drawing.Font("Segoe UI", 8.5f),
            ForeColor = System.Drawing.Color.FromArgb(130, 130, 160),
            AutoSize = true,
            Location = new System.Drawing.Point(16, 36),
        };

        btnClear = new System.Windows.Forms.Button
        {
            Text = "Clear",
            Width = 72,
            Height = 30,
            Anchor = System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Top,
            Location = new System.Drawing.Point(this.Width - 100, 14),
            BackColor = System.Drawing.Color.FromArgb(45, 45, 60),
            ForeColor = System.Drawing.Color.Silver,
            FlatStyle = System.Windows.Forms.FlatStyle.Flat,
            Cursor = System.Windows.Forms.Cursors.Hand,
        };
        btnClear.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(70, 70, 100);

        pnlTop.Controls.Add(lblTitle);
        pnlTop.Controls.Add(lblModel);
        pnlTop.Controls.Add(btnClear);

        // ── Status strip ─────────────────────────────────
        pnlStatus = new System.Windows.Forms.Panel
        {
            Dock = System.Windows.Forms.DockStyle.Bottom,
            Height = 30,
            BackColor = System.Drawing.Color.FromArgb(22, 22, 32),
            Padding = new System.Windows.Forms.Padding(10, 0, 10, 0),
        };

        lblStatus = new System.Windows.Forms.Label
        {
            Text = "Initializing…",
            ForeColor = System.Drawing.Color.FromArgb(100, 180, 255),
            Font = new System.Drawing.Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new System.Drawing.Point(10, 7),
        };

        progressBar = new System.Windows.Forms.ProgressBar
        {
            Width = 160,
            Height = 14,
            Anchor = System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Top,
            Location = new System.Drawing.Point(this.Width - 180, 8),
            Style = System.Windows.Forms.ProgressBarStyle.Continuous,
            Visible = false,
        };

        pnlStatus.Controls.Add(lblStatus);
        pnlStatus.Controls.Add(progressBar);

        // ── Input bar ────────────────────────────────────
        pnlInput = new System.Windows.Forms.Panel
        {
            Dock = System.Windows.Forms.DockStyle.Bottom,
            Height = 90,
            BackColor = System.Drawing.Color.FromArgb(22, 22, 32),
            Padding = new System.Windows.Forms.Padding(10, 8, 10, 8),
        };

        rtbInput = new System.Windows.Forms.RichTextBox
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(30, 30, 44),
            ForeColor = System.Drawing.Color.WhiteSmoke,
            BorderStyle = System.Windows.Forms.BorderStyle.None,
            Font = new System.Drawing.Font("Segoe UI", 10f),
            ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical,
            Multiline = true,
        };

        btnSend = new System.Windows.Forms.Button
        {
            Text = "Send  ➤",
            Width = 90,
            Dock = System.Windows.Forms.DockStyle.Right,
            BackColor = System.Drawing.Color.FromArgb(50, 100, 220),
            ForeColor = System.Drawing.Color.White,
            FlatStyle = System.Windows.Forms.FlatStyle.Flat,
            Font = new System.Drawing.Font("Segoe UI Semibold", 9.5f, System.Drawing.FontStyle.Bold),
            Cursor = System.Windows.Forms.Cursors.Hand,
            Enabled = false,
        };
        btnSend.FlatAppearance.BorderSize = 0;

        pnlInput.Controls.Add(rtbInput);
        pnlInput.Controls.Add(btnSend);

        // ── Chat display ─────────────────────────────────
        rtbChat = new System.Windows.Forms.RichTextBox
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(18, 18, 26),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 230),
            BorderStyle = System.Windows.Forms.BorderStyle.None,
            Font = new System.Drawing.Font("Cascadia Code", 10f),
            ReadOnly = true,
            ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical,
            Padding = new System.Windows.Forms.Padding(8),
            WordWrap = true,
        };

        // ── Wire controls to form ─────────────────────────
        this.Controls.Add(rtbChat);       // Fill (added before panels)
        this.Controls.Add(pnlInput);
        this.Controls.Add(pnlStatus);
        this.Controls.Add(pnlTop);

        // ── Events ───────────────────────────────────────
        btnSend.Click += async (s, e) => await OnSendAsync();
        btnClear.Click += (s, e) => ClearChat();
        rtbInput.KeyDown += RtbInput_KeyDown;

        this.Resize += (s, e) =>
        {
            btnClear.Location = new System.Drawing.Point(pnlTop.Width - 100, 14);
            progressBar.Location = new System.Drawing.Point(pnlStatus.Width - 180, 8);
        };
    }
}
