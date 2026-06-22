using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryChatApp
{
    public sealed class MainForm : Form
    {
        // ── UI controls ───────────────────────────────────────────────────────────
        private readonly RichTextBox    _rtbHardware;
        private readonly ListBox        _lstModels;
        private readonly Label          _lblModelInfo;
        private readonly Button         _btnDownload;
        private readonly Button         _btnCancel;
        private readonly ProgressBar    _progressBar;
        private readonly Label          _lblProgress;
        private readonly RichTextBox    _rtbChat;
        private readonly TextBox        _txtInput;
        private readonly Button         _btnSend;
        private readonly Label          _lblStatus;
        private Button                  _btnClearCache = null!;
        private readonly ToolTip        _toolTip;

        // ── State ─────────────────────────────────────────────────────────────────
        private List<IModel>            _models      = new List<IModel>();
        private IModel?                 _activeModel;
        private OpenAIChatClient?       _chatClient;
        private readonly List<ChatMessage> _history  = new List<ChatMessage>();
        private CancellationTokenSource? _cts;
        private bool                    _busy;
        private bool                    _generating;

        // ── Chat sessions ─────────────────────────────────────────────────────────
        private sealed class ChatSession
        {
            public string Name { get; set; } = "New Chat";
            public string ModelAlias { get; set; } = "Assistant";
            public List<ChatMessage> History { get; set; } = new List<ChatMessage>();
        }
        private readonly List<ChatSession> _sessions = new List<ChatSession>();
        private int                        _sessionIdx = -1;

        // ── Chat history panel controls ───────────────────────────────────────────
        private ListBox  _lstChats   = null!;
        private Button   _btnNewChat = null!;

        // ── Palette ───────────────────────────────────────────────────────────────
        private static readonly Color BgDark       = Rgb(32,  33,  35);
        private static readonly Color BgPanel      = Rgb(52,  53,  65);
        private static readonly Color BgInput      = Rgb(64,  65,  79);
        private static readonly Color BgList       = Rgb(40,  41,  52);
        private static readonly Color Teal         = Rgb(16, 163, 127);
        private static readonly Color TextDim      = Rgb(160, 160, 180);
        private static readonly Color TextBright   = Rgb(220, 220, 230);
        private static readonly Color Separator    = Rgb(65,  66,  80);
        private static readonly Color ColYou       = Rgb(86,  180, 120);
        private static readonly Color ColAi        = Rgb(86,  156, 214);
        private static readonly Color ColSystem    = Rgb(142, 142, 160);
        private static readonly Color ColError     = Rgb(220, 80,  80);
        private static readonly Color ColGold      = Rgb(255, 198,  80);
        private static readonly Color ColGreen     = Rgb(86,  180, 120);

        // ─────────────────────────────────────────────────────────────────────────

        public MainForm()
        {
            Text            = "Foundry Local Chat";
            Size            = new Size(1100, 760);
            MinimumSize     = new Size(860, 560);
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = BgDark;
            ForeColor       = Color.White;
            Font            = new Font("Segoe UI", 10F);
            _toolTip        = new ToolTip();

            // ── LEFT PANEL ─────────────────────────────────────────────────────────
            var left = MakePanel(DockStyle.Left, BgPanel, width: 340);
            left.Padding = new Padding(8, 8, 8, 0);

            // ── Hardware info section ──────────────────────────────────────────────
            var hwTitle = MakeLabel("🖥  Hardware", DockStyle.Top, 28, bold: true);
            hwTitle.Padding = new Padding(0, 4, 0, 0);

            _rtbHardware = new RichTextBox
            {
                Dock        = DockStyle.Top,
                Height      = 148,
                BackColor   = BgList,
                ForeColor   = TextBright,
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Font        = new Font("Consolas", 9F),
                ScrollBars  = RichTextBoxScrollBars.None,
                Text        = "  Detecting hardware…"
            };

            _btnClearCache = MakeButton("🗑  Clear Model Cache", Rgb(70, 50, 50), DockStyle.Top, height: 26);
            _btnClearCache.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            _btnClearCache.Click += OnClearCacheClick;

            var hwSep = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = BgPanel };

            // ── Model list section ─────────────────────────────────────────────────
            var titleLabel = MakeLabel("📦  Available Models", DockStyle.Top, 28, bold: true);
            titleLabel.Padding = new Padding(0, 4, 0, 0);

            _lstModels = new ListBox
            {
                Dock          = DockStyle.Fill,
                BackColor     = BgList,
                ForeColor     = TextBright,
                BorderStyle   = BorderStyle.None,
                Font          = new Font("Consolas", 9F),
                SelectionMode = SelectionMode.One,
                ItemHeight    = 22
            };
            _lstModels.SelectedIndexChanged += OnModelSelected;

            // Selected model detail strip
            _lblModelInfo = MakeLabel("", DockStyle.Bottom, 40);
            _lblModelInfo.Font      = new Font("Segoe UI", 9F, FontStyle.Italic);
            _lblModelInfo.ForeColor = TextDim;
            _lblModelInfo.Padding   = new Padding(4, 2, 0, 0);

            // Bottom controls
            var foot = MakePanel(DockStyle.Bottom, BgPanel, height: 108);
            foot.Padding = new Padding(0, 6, 0, 8);

            _btnDownload = MakeButton("⬇  Download & Load", Teal, DockStyle.Top, height: 38);
            _btnDownload.Enabled = false;
            _btnDownload.Click  += OnDownloadClick;

            _progressBar = new ProgressBar
            {
                Dock    = DockStyle.Top,
                Height  = 8,
                Style   = ProgressBarStyle.Continuous,
                Visible = false,
                Margin  = new Padding(0, 4, 0, 0)
            };

            _lblProgress = MakeLabel("", DockStyle.Top, 24);
            _lblProgress.Font      = new Font("Segoe UI", 9F);
            _lblProgress.ForeColor = TextDim;

            _btnCancel = MakeButton("✕  Cancel", Rgb(140, 60, 60), DockStyle.Top, height: 30);
            _btnCancel.Visible = false;
            _btnCancel.Click  += (_, __) => _cts?.Cancel();
            _toolTip.SetToolTip(_btnCancel, "Cancel current download or generation");

            foot.Controls.Add(_lblProgress);
            foot.Controls.Add(_progressBar);
            foot.Controls.Add(_btnCancel);
            foot.Controls.Add(_btnDownload);

            // Add to left panel (Top → Fill → Bottom order matters for dock)
            left.Controls.Add(_lstModels);
            left.Controls.Add(foot);
            left.Controls.Add(_lblModelInfo);
            left.Controls.Add(titleLabel);
            left.Controls.Add(hwSep);
            left.Controls.Add(_btnClearCache);
            left.Controls.Add(_rtbHardware);
            left.Controls.Add(hwTitle);

            // ── RIGHT / CHAT PANEL ─────────────────────────────────────────────────
            var right = MakePanel(DockStyle.Fill, BgPanel);

            _lblStatus = MakeLabel("⏳  Initializing Foundry Local…", DockStyle.Top, 28);
            _lblStatus.Font      = new Font("Segoe UI", 9F);
            _lblStatus.ForeColor = TextDim;
            _lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            _lblStatus.BackColor = BgDark;

            _rtbChat = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = BgPanel,
                ForeColor   = Color.White,
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Font        = new Font("Segoe UI", 11F),
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                Padding     = new Padding(12)
            };

            var inputRow = MakePanel(DockStyle.Bottom, BgInput, height: 80);
            inputRow.Padding = new Padding(10, 8, 8, 8);

            var inputHint = new Label
            {
                Text      = "  Enter = send  •  Shift+Enter = new line",
                Dock      = DockStyle.Bottom,
                Height    = 18,
                BackColor = BgDark,
                ForeColor = TextDim,
                Font      = new Font("Segoe UI", 8F)
            };

            _btnSend = MakeButton("➤", Teal, DockStyle.Right, width: 56);
            _btnSend.Font    = new Font("Segoe UI", 13F);
            _btnSend.Enabled = false;
            _btnSend.Click  += async (_, __) => await SendAsync();
            _toolTip.SetToolTip(_btnSend, "Send message");

            _txtInput = new TextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = BgInput,
                ForeColor   = Color.White,
                BorderStyle = BorderStyle.None,
                Font        = new Font("Segoe UI", 11F),
                Enabled     = false,
                Multiline   = true,
                AcceptsReturn = true,
                ScrollBars  = ScrollBars.Vertical
            };
            _txtInput.KeyDown += async (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    await SendAsync();
                }
                // Shift+Enter inserts a newline (handled by default for multiline TextBox)
            };

            inputRow.Controls.Add(_txtInput);
            inputRow.Controls.Add(_btnSend);

            right.Controls.Add(_rtbChat);
            right.Controls.Add(inputRow);
            right.Controls.Add(inputHint);
            right.Controls.Add(_lblStatus);

            // ── SPLITTER ────────────────────────────────────────────────────────────
            var splitter = new Splitter
            {
                Dock      = DockStyle.Left,
                Width     = 3,
                BackColor = Rgb(80, 81, 95)
            };

            // ── HISTORY PANEL (right side) ─────────────────────────────────────────────
            var histPanel = MakePanel(DockStyle.Right, BgPanel, width: 180);
            histPanel.Padding = new Padding(4, 6, 4, 4);

            var histTitle = MakeLabel("💬  Chats", DockStyle.Top, 26, bold: true);

            _btnNewChat = MakeButton("＋  New Chat", Teal, DockStyle.Top, height: 32);
            _btnNewChat.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _btnNewChat.Click += OnNewChat;

            _lstChats = new ListBox
            {
                Dock          = DockStyle.Fill,
                BackColor     = BgList,
                ForeColor     = TextBright,
                BorderStyle   = BorderStyle.None,
                Font          = new Font("Segoe UI", 9F),
                SelectionMode = SelectionMode.One,
                ItemHeight    = 24
            };
            _lstChats.SelectedIndexChanged += OnChatSelected;

            histPanel.Controls.Add(_lstChats);
            histPanel.Controls.Add(_btnNewChat);
            histPanel.Controls.Add(histTitle);

            var histSplitter = new Splitter
            {
                Dock      = DockStyle.Right,
                Width     = 3,
                BackColor = Rgb(80, 81, 95)
            };

            Controls.Add(right);
            Controls.Add(histSplitter);
            Controls.Add(histPanel);
            Controls.Add(splitter);
            Controls.Add(left);

            Load += async (_, __) => await InitAsync();
        }

        // ── Hardware detection ────────────────────────────────────────────────────

        private void LoadHardwareInfo()
        {
            try
            {
                // CPU
                string cpuName   = "Unknown CPU";
                int    cpuCores  = 0;
                using (var q = new ManagementObjectSearcher("SELECT Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor"))
                    foreach (ManagementObject o in q.Get())
                    {
                        cpuName  = o["Name"]?.ToString()?.Trim() ?? cpuName;
                        cpuCores = Convert.ToInt32(o["NumberOfCores"]);
                        break;
                    }

                // RAM total + available
                long ramTotal = 0, ramFree = 0;
                using (var q = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in q.Get())
                    { ramTotal = Convert.ToInt64(o["TotalPhysicalMemory"]); break; }
                using (var q = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem"))
                    foreach (ManagementObject o in q.Get())
                    { ramFree = Convert.ToInt64(o["FreePhysicalMemory"]) * 1024L; break; }

                // GPU(s)
                var gpus = new List<string>();
                using (var q = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                    foreach (ManagementObject o in q.Get())
                    {
                        string name  = o["Name"]?.ToString() ?? "Unknown GPU";
                        long   vram  = Convert.ToInt64(o["AdapterRAM"] ?? 0L);
                        string vramStr = vram > 0 ? $" ({vram / (1024 * 1024 * 1024.0):F0} GB)" : "";
                        gpus.Add($"{name}{vramStr}");
                    }

                SafeUI(() => RenderHardwarePanel(cpuName, cpuCores, ramTotal, ramFree, gpus));
            }
            catch (Exception ex)
            {
                SafeUI(() =>
                {
                    _rtbHardware.Text = $"  Hardware info unavailable: {ex.Message}";
                });
            }
        }

        private void RenderHardwarePanel(string cpu, int cores, long ramTotal, long ramFree,
                                         List<string> gpus)
        {
            _rtbHardware.Clear();

            void Line(string label, string value, Color valColor)
            {
                _rtbHardware.SelectionColor = TextDim;
                _rtbHardware.SelectionFont  = new Font("Consolas", 9F);
                _rtbHardware.AppendText($"  {label,-5}");
                _rtbHardware.SelectionColor = valColor;
                _rtbHardware.AppendText($"{value}\n");
            }

            double ramTotalGb = ramTotal / (1024.0 * 1024 * 1024);
            double ramFreeGb  = ramFree  / (1024.0 * 1024 * 1024);

            // Show EPs if Foundry is ready, else just show hardware
            EpInfo[]? eps = null;
            if (FoundryLocalManager.IsInitialized)
                try { eps = FoundryLocalManager.Instance.DiscoverEps(); } catch { }

            string epLine = eps != null && eps.Length > 0
                ? string.Join("  ", eps.Select(e => e.IsRegistered ? $"✓{e.Name}" : $"○{e.Name}"))
                : "CPU (default)";

            Line("CPU:",  $"{cpu}  ({cores} cores)", ColGold);
            Line("RAM:",  $"{ramTotalGb:F1} GB total  │  {ramFreeGb:F1} GB free", ColGreen);

            foreach (var gpu in gpus.Take(2))
                Line("GPU:", gpu, Rgb(140, 180, 255));

            Line("EPs:", epLine, Teal);

            string cachePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".FoundryChatApp", "cache", "models");
            long cacheBytes = 0;
            if (System.IO.Directory.Exists(cachePath))
            {
                try
                {
                    cacheBytes = new System.IO.DirectoryInfo(cachePath)
                        .GetFiles("*", System.IO.SearchOption.AllDirectories)
                        .Sum(f => f.Length);
                }
                catch { }
            }
            string cacheInfo = System.IO.Directory.Exists(cachePath)
                ? (cacheBytes > 1024L * 1024 * 1024
                    ? $"{cacheBytes / (1024.0 * 1024 * 1024):F1} GB used"
                    : $"{cacheBytes / (1024.0 * 1024):F0} MB used")
                : "(empty)";
            Line("📦:", $"{cacheInfo}", TextDim);
        }

        // ── Initialisation ────────────────────────────────────────────────────────

        private async Task InitAsync()
        {
            // Show hardware info immediately (WMI is synchronous)
            await Task.Run(() => LoadHardwareInfo());

            try
            {
                Status("⏳  Starting Foundry Local engine…");

                await FoundryLocalManager.CreateAsync(
                    new Configuration { AppName = "FoundryChatApp" },
                    NullLogger.Instance);

                // Register all available EPs (GPU/NPU/DirectML) so the catalog
                // includes hardware-accelerated model variants.
                // Without this, only CPU models appear.
                Status("🔌  Registering execution providers (GPU/NPU) — CUDA registration may take several minutes, please wait…");
                try
                {
                    string curEp = "";
                    await FoundryLocalManager.Instance.DownloadAndRegisterEpsAsync((epName, pct) =>
                    {
                        if (epName != curEp)
                        {
                            curEp = epName;
                            Status($"🔌  Registering {epName}…  {pct:F0}%");
                        }
                    });
                }
                catch { /* non-critical — catalog still works with CPU-only EPs */ }

                // Re-render hardware panel now that EPs are registered
                await Task.Run(() => LoadHardwareInfo());

                Status("📋  Loading model catalog…");

                var catalog = await FoundryLocalManager.Instance.GetCatalogAsync();
                var models  = (await catalog.ListModelsAsync()).ToList();
                var cached  = new HashSet<string>((await catalog.GetCachedModelsAsync()).Select(m => m.Id));

                // Expand each model into its individual variants (GPU first, then NPU, then CPU).
                // This lets the user explicitly pick the device they want.
                var allVariants = models
                    .SelectMany(m => m.Variants.Count > 0
                        ? (IEnumerable<IModel>)m.Variants.OrderBy(v => v.Info?.Runtime?.DeviceType switch
                            { DeviceType.GPU => 0, DeviceType.NPU => 1, _ => 2 })
                        : new List<IModel> { m })
                    .ToList();

                _models = allVariants;

                SafeUI(() =>
                {
                    _lstModels.BeginUpdate();
                    _lstModels.Items.Clear();
                    foreach (var m in allVariants)
                    {
                        string cached_mark = cached.Contains(m.Id) ? "✓" : " ";
                        string sizePart    = m.Info?.FileSizeMb.HasValue == true
                            ? $"{m.Info.FileSizeMb.Value / 1024.0:F1} GB"
                            : "  ?  ";
                        string alias       = (m.Alias ?? "").PadRight(24);
                        string devTag      = DeviceTag(m);
                        _lstModels.Items.Add($"{cached_mark} {alias} {sizePart,7}  {devTag}");
                    }
                    _lstModels.EndUpdate();

                    if (_lstModels.Items.Count > 0)
                    {
                        _lstModels.SelectedIndex = 0;
                        _btnDownload.Enabled = true;
                    }
                });

                Status($"✅  {allVariants.Count} model variants available — select one and click Download & Load.");
            }
            catch (Exception ex)
            {
                string detail = ex.ToString();
                System.IO.File.WriteAllText(Program.LogFile, detail);
                Status($"❌  Init failed — see error.log");
                MessageBox.Show(
                    $"Could not start Foundry Local.\n\n{ex.Message}\n\nFull details in:\n{Program.LogFile}",
                    "Initialization Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Model selection ───────────────────────────────────────────────────────

        private void OnModelSelected(object? sender, EventArgs e)
        {
            int idx = _lstModels.SelectedIndex;
            if (idx < 0 || idx >= _models.Count) return;

            var m    = _models[idx];
            var info = m.Info;

            var parts = new List<string>();

            if (info?.FileSizeMb.HasValue == true)
            {
                double gb = info.FileSizeMb.Value / 1024.0;
                parts.Add($"💾 {gb:F1} GB RAM needed");
            }

            if (info?.ContextLength.HasValue == true)
                parts.Add($"📝 {info.ContextLength.Value / 1000}K ctx");

            if (info?.MaxOutputTokens.HasValue == true)
                parts.Add($"⬆ {info.MaxOutputTokens.Value} max tokens");

            if (!string.IsNullOrEmpty(info?.Publisher))
                parts.Add($"by {info.Publisher}");

            parts.Add(DeviceTag(m));

            _lblModelInfo.Text   = parts.Count > 0 ? "  " + string.Join("  │  ", parts) : $"  {info?.DisplayName ?? m.Alias}";
            _btnDownload.Enabled = !_busy;
        }

        // ── Download & Load ───────────────────────────────────────────────────────

        private async void OnDownloadClick(object? sender, EventArgs e)
        {
            if (_generating)
            {
                Status("⚠  Cancel the current response before loading a different model");
                return;
            }

            int idx = _lstModels.SelectedIndex;
            if (idx < 0 || idx >= _models.Count) return;

            var model = _models[idx];
            SetBusy(true);

            _cts = new CancellationTokenSource();

            try
            {
                // Unload any previously loaded model
                if (_activeModel != null)
                {
                    Status($"⏏  Unloading previous model…");
                    try { await _activeModel.UnloadAsync(); } catch { /* ignore */ }
                    _activeModel = null;
                    _chatClient  = null;
                    SafeUI(() => { _txtInput.Enabled = false; _btnSend.Enabled = false; });
                }

                Status($"⬇  Downloading {model.Alias}…");
                SafeUI(() => { _progressBar.Visible = true; _progressBar.Value = 0; _btnCancel.Visible = true; });

                await model.DownloadAsync(
                    pct =>
                    {
                        int v = Math.Min(100, (int)pct);
                        SafeUI(() =>
                        {
                            _progressBar.Value = v;
                            _lblProgress.Text  = $"Downloading…  {v}%";
                        });
                    },
                    _cts.Token);

                SafeUI(() => { _progressBar.Visible = false; _btnCancel.Visible = false; _lblProgress.Text = ""; });

                // The user selected the specific variant; no need to SelectVariant.
                string deviceLabel = model.Info?.Runtime?.DeviceType switch
                {
                    DeviceType.GPU => "GPU",
                    DeviceType.NPU => "NPU",
                    _              => "CPU"
                };
                Status($"⏳  Loading {model.Alias} ({deviceLabel}) into memory… (may take a minute for large models)");

                await model.LoadAsync();

                _chatClient               = await model.GetChatClientAsync();
                _chatClient.Settings.Temperature = 0.7f;
                _chatClient.Settings.MaxTokens   = 512;   // conservative; avoids timeout on slow hardware
                _chatClient.Settings.TopP         = 0.95f;

                _activeModel = model;

                SaveCurrentSession();

                // Reset conversation
                _history.Clear();
                _history.Add(new ChatMessage
                {
                    Role    = "system",
                    Content = "You are a helpful, friendly, and knowledgeable AI assistant."
                });

                // Initialize first chat session if none exist
                if (_sessions.Count == 0)
                {
                    var firstSession = new ChatSession
                    {
                        Name = "Chat 1",
                        ModelAlias = model.Alias ?? "Assistant"
                    };
                    firstSession.History.AddRange(_history);
                    _sessions.Add(firstSession);
                    _sessionIdx = 0;
                    SafeUI(() =>
                    {
                        _lstChats.Items.Clear();
                        _lstChats.Items.Add("Chat 1");
                        _lstChats.SelectedIndex = 0;
                    });
                }
                else
                {
                    // Start fresh session
                    var newSession = new ChatSession
                    {
                        Name = "New Chat",
                        ModelAlias = model.Alias ?? "Assistant"
                    };
                    newSession.History.AddRange(_history);
                    _sessions.Add(newSession);
                    _sessionIdx = _sessions.Count - 1;
                    SafeUI(() =>
                    {
                        _lstChats.Items.Add("New Chat");
                        _lstChats.SelectedIndex = _lstChats.Items.Count - 1;
                    });
                }

                SafeUI(() =>
                {
                    _txtInput.Enabled = true;
                    _btnSend.Enabled  = true;
                    _txtInput.Focus();
                    // Refresh list to update cached indicator (✓ prefix)
                    string item = _lstModels.Items[idx]?.ToString() ?? "";
                    if (!item.StartsWith("✓"))
                        _lstModels.Items[idx] = "✓" + item.Substring(1);
                });

                AppendChat("System", $"✅  Model '{model.Alias}' loaded and ready! ({deviceLabel})\nYou can now type a message below.", ColSystem, italic: true);
                Status($"✅  {model.Alias} ({deviceLabel})  —  Ready");
            }
            catch (OperationCanceledException)
            {
                Status("⚠  Download cancelled.");
                AppendChat("System", "Download was cancelled.", ColSystem, italic: true);
            }
            catch (Exception ex)
            {
                Status($"❌  {ex.Message}");
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                SafeUI(() =>
                {
                    _progressBar.Visible = false;
                    _btnCancel.Visible   = false;
                    _lblProgress.Text    = "";
                });
            }
        }

        // ── Chat ──────────────────────────────────────────────────────────────────

        private void OnNewChat(object? sender, EventArgs e)
        {
            if (_generating)
            {
                Status("⚠  Cancel the current response before starting a new chat");
                return;
            }

            // Save current chat to session (if it has messages beyond system)
            SaveCurrentSession();

            // Create new session
            var session = new ChatSession
            {
                Name = "New Chat",
                ModelAlias = _activeModel?.Alias ?? "Assistant"
            };
            session.History.Add(new ChatMessage
            {
                Role    = "system",
                Content = "You are a helpful, friendly, and knowledgeable AI assistant."
            });
            _sessions.Add(session);
            _sessionIdx = _sessions.Count - 1;

            // Update list
            SafeUI(() =>
            {
                _lstChats.Items.Add("New Chat");
                _lstChats.SelectedIndex = _lstChats.Items.Count - 1;
            });

            // Clear current chat display and reset history
            _history.Clear();
            _history.Add(new ChatMessage
            {
                Role    = "system",
                Content = "You are a helpful, friendly, and knowledgeable AI assistant."
            });
            SafeUI(() => _rtbChat.Clear());
            Status($"✅  {_activeModel?.Alias ?? "No model loaded"}  —  New chat started");
        }

        private void OnChatSelected(object? sender, EventArgs e)
        {
            if (_generating)
            {
                Status("⚠  Cancel the current response before switching chats");
                if (_sessionIdx >= 0 && _sessionIdx < _lstChats.Items.Count)
                    SafeUI(() => _lstChats.SelectedIndex = _sessionIdx);
                return;
            }

            int idx = _lstChats.SelectedIndex;
            if (idx < 0 || idx >= _sessions.Count || idx == _sessionIdx) return;

            // Save current session
            SaveCurrentSession();

            // Load selected session
            _sessionIdx = idx;
            var session = _sessions[idx];
            const string assistantName = "Assistant";

            _history.Clear();
            _history.AddRange(session.History);

            // Rebuild chat display from history
            SafeUI(() =>
            {
                _rtbChat.Clear();
                foreach (var msg in _history)
                {
                    if (msg.Role == "user")
                        AppendChat("You", msg.Content ?? "", ColYou);
                    else if (msg.Role == "assistant")
                        AppendChat(assistantName, msg.Content ?? "", ColAi);
                }
            });
            Status($"✅  {_activeModel?.Alias ?? assistantName}  —  Chat loaded");
        }

        private void SaveCurrentSession()
        {
            if (_sessionIdx >= 0 && _sessionIdx < _sessions.Count)
            {
                _sessions[_sessionIdx].History = new List<ChatMessage>(_history);
                // Auto-name from first user message
                var firstUser = _history.FirstOrDefault(m => m.Role == "user");
                if (firstUser != null)
                {
                    string name = firstUser.Content ?? "Chat";
                    if (name.Length > 25) name = name.Substring(0, 22) + "…";
                    _sessions[_sessionIdx].Name = name;
                    SafeUI(() =>
                    {
                        if (_sessionIdx < _lstChats.Items.Count)
                            _lstChats.Items[_sessionIdx] = name;
                    });
                }
            }
        }

        private async Task SendAsync()
        {
            string text = _txtInput.Text.Trim();
            if (string.IsNullOrEmpty(text) || _chatClient is null || _busy) return;

            SafeUI(() =>
            {
                _txtInput.Clear();
                _txtInput.Enabled = false;
                _btnSend.Enabled  = false;
                _btnNewChat.Enabled = false;
                _lstChats.Enabled = false;
                _btnDownload.Enabled = false;
                _lstModels.Enabled = false;
                _btnCancel.Visible = true;
                _lblProgress.Text = "Generating…";
            });
            _toolTip.SetToolTip(_btnSend, "Generating… use Cancel to stop.");

            Status("⏳  Thinking…");
            AppendChat("You", text, ColYou);
            _history.Add(new ChatMessage { Role = "user", Content = text });
            TrimHistory(20);
            SaveCurrentSession();

            _cts = new CancellationTokenSource();
            _generating = true;

            try
            {
                // Streaming: display tokens as they arrive — avoids timeout on long prompts
                var sb = new System.Text.StringBuilder();
                bool headerWritten = false;

                await foreach (var chunk in _chatClient.CompleteChatStreamingAsync(_history.ToArray(), _cts.Token))
                {
                    string content = chunk.Choices?.Count > 0
                        ? (chunk.Choices[0].Message?.Content ?? chunk.Choices[0].Delta?.Content ?? string.Empty)
                        : string.Empty;
                    if (string.IsNullOrEmpty(content)) continue;

                    if (!headerWritten)
                    {
                        // Write the sender header once
                        SafeUI(() =>
                        {
                            _rtbChat.SelectionStart = _rtbChat.TextLength;
                            _rtbChat.SelectionColor = ColAi;
                            _rtbChat.SelectionFont  = new Font("Segoe UI", 10F, FontStyle.Bold);
                            _rtbChat.AppendText($"\n{_activeModel?.Alias ?? "Assistant"}\n");
                        });
                        headerWritten = true;
                    }

                    sb.Append(content);
                    SafeUI(() =>
                    {
                        _rtbChat.SelectionStart = _rtbChat.TextLength;
                        _rtbChat.SelectionColor = TextBright;
                        _rtbChat.SelectionFont  = new Font("Segoe UI", 11F, FontStyle.Regular);
                        _rtbChat.AppendText(content);
                        _rtbChat.ScrollToCaret();
                    });
                }

                string fullReply = sb.Length > 0 ? sb.ToString() : "(no response)";

                if (!headerWritten)
                {
                    SafeUI(() =>
                    {
                        _rtbChat.SelectionStart = _rtbChat.TextLength;
                        _rtbChat.SelectionColor = ColAi;
                        _rtbChat.SelectionFont  = new Font("Segoe UI", 10F, FontStyle.Bold);
                        _rtbChat.AppendText($"\n{_activeModel?.Alias ?? "Assistant"}\n");
                        _rtbChat.SelectionColor = TextBright;
                        _rtbChat.SelectionFont  = new Font("Segoe UI", 11F, FontStyle.Regular);
                        _rtbChat.AppendText(fullReply);
                    });
                }

                // Write separator after complete response
                SafeUI(() =>
                {
                    _rtbChat.SelectionStart = _rtbChat.TextLength;
                    _rtbChat.SelectionColor = Separator;
                    _rtbChat.SelectionFont  = new Font("Segoe UI", 8F);
                    _rtbChat.AppendText("\n─────────────────────────────────────────────────────\n");
                    _rtbChat.ScrollToCaret();
                });

                _history.Add(new ChatMessage { Role = "assistant", Content = fullReply });
                SaveCurrentSession();
                Status($"✅  {_activeModel?.Alias}  —  Ready");
            }
            catch (OperationCanceledException)
            {
                if (_history.Count > 0
                    && string.Equals(_history[_history.Count - 1].Role, "user", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(_history[_history.Count - 1].Content, text, StringComparison.Ordinal))
                {
                    _history.RemoveAt(_history.Count - 1);
                    SaveCurrentSession();
                }

                SafeUI(() =>
                {
                    _rtbChat.SelectionStart = _rtbChat.TextLength;
                    _rtbChat.SelectionColor = ColSystem;
                    _rtbChat.SelectionFont  = new Font("Segoe UI", 10F, FontStyle.Italic);
                    _rtbChat.AppendText("\n(cancelled)\n─────────────────────────────────────────────────────\n");
                });
                Status("⚠  Cancelled");
            }
            catch (Exception ex)
            {
                if (_history.Count > 0
                    && string.Equals(_history[_history.Count - 1].Role, "user", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(_history[_history.Count - 1].Content, text, StringComparison.Ordinal))
                {
                    _history.RemoveAt(_history.Count - 1);
                    SaveCurrentSession();
                }

                string detail = $"[{DateTime.Now:HH:mm:ss}] SendAsync error:\n{ex}\n\n";
                System.IO.File.AppendAllText(Program.LogFile, detail);

                bool isOom = ex.Message.IndexOf("memory", StringComparison.OrdinalIgnoreCase) >= 0
                          || ex.Message.IndexOf("OOM", StringComparison.OrdinalIgnoreCase) >= 0
                          || ex.Message.IndexOf("allocat", StringComparison.OrdinalIgnoreCase) >= 0;

                SafeUI(() =>
                {
                    _rtbChat.SelectionStart = _rtbChat.TextLength;
                    _rtbChat.SelectionColor = ColError;
                    _rtbChat.SelectionFont  = new Font("Segoe UI", 11F, isOom ? FontStyle.Bold : FontStyle.Regular);
                    string label = isOom ? "\n⚠  OUT OF MEMORY — GPU/RAM exhausted\n" : $"\nError\n{ex.Message}\n";
                    _rtbChat.AppendText(label);
                    if (isOom)
                    {
                        _rtbChat.SelectionColor = TextDim;
                        _rtbChat.SelectionFont  = new Font("Segoe UI", 9F, FontStyle.Italic);
                        _rtbChat.AppendText("Tip: Use shorter prompts, start a New Chat, or load a smaller/CPU model.\n");
                    }
                    _rtbChat.SelectionColor = Separator;
                    _rtbChat.SelectionFont  = new Font("Segoe UI", 8F);
                    _rtbChat.AppendText("─────────────────────────────────────────────────────\n");
                    _rtbChat.ScrollToCaret();
                });
                Status("❌  Error — see error.log for details");
            }
            finally
            {
                _generating = false;
                _cts = null;
                _toolTip.SetToolTip(_btnSend, "Send message");
                SafeUI(() =>
                {
                    _txtInput.Enabled = true;
                    _btnSend.Enabled  = true;
                    _btnNewChat.Enabled = true;
                    _lstChats.Enabled = true;
                    _btnDownload.Enabled = !_busy && _lstModels.SelectedIndex >= 0;
                    _lstModels.Enabled = !_busy;
                    _btnCancel.Visible = false;
                    _lblProgress.Text = "";
                    _txtInput.Focus();
                });
            }
        }

        private void OnClearCacheClick(object? sender, EventArgs e)
        {
            string cachePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".FoundryChatApp", "cache", "models");
            if (!System.IO.Directory.Exists(cachePath))
            {
                MessageBox.Show("Model cache directory does not exist:\n" + cachePath,
                    "Clear Cache", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(
                $"Delete all cached models from:\n{cachePath}\n\nYou will need to re-download models.",
                "Clear Model Cache", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                foreach (var d in new System.IO.DirectoryInfo(cachePath).GetDirectories())
                    d.Delete(true);
                foreach (var f in new System.IO.DirectoryInfo(cachePath).GetFiles())
                    f.Delete();
                MessageBox.Show("Model cache cleared.", "Clear Cache",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Task.Run(() => LoadHardwareInfo());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing cache: {ex.Message}", "Clear Cache",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TrimHistory(int maxTurns)
        {
            // Always keep the system message (index 0)
            // Each turn = 2 messages (user + assistant); trim oldest pairs
            int maxMessages = 1 + maxTurns * 2;
            while (_history.Count > maxMessages)
            {
                _history.RemoveAt(1); // remove oldest user after system msg
                if (_history.Count > 1 && string.Equals(_history[1].Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    _history.RemoveAt(1);
            }
        }

        // ── UI helpers ────────────────────────────────────────────────────────────

        private void AppendChat(string sender, string text, Color nameColor, bool italic = false)
        {
            SafeUI(() =>
            {
                _rtbChat.SuspendLayout();

                _rtbChat.SelectionStart  = _rtbChat.TextLength;
                _rtbChat.SelectionLength = 0;

                // Sender name
                _rtbChat.SelectionColor = nameColor;
                _rtbChat.SelectionFont  = new Font("Segoe UI", 10F, FontStyle.Bold);
                _rtbChat.AppendText($"\n{sender}\n");

                // Message body
                _rtbChat.SelectionColor = TextBright;
                _rtbChat.SelectionFont  = new Font("Segoe UI", 11F, italic ? FontStyle.Italic : FontStyle.Regular);
                _rtbChat.AppendText($"{text}\n");

                // Separator
                _rtbChat.SelectionColor = Separator;
                _rtbChat.SelectionFont  = new Font("Segoe UI", 8F);
                _rtbChat.AppendText("─────────────────────────────────────────────────────\n");

                _rtbChat.ResumeLayout();
                _rtbChat.ScrollToCaret();
            });
        }

        private void Status(string msg) => SafeUI(() => _lblStatus.Text = msg);

        private void SetBusy(bool busy)
        {
            _busy = busy;
            SafeUI(() =>
            {
                _btnDownload.Enabled = !busy && _lstModels.SelectedIndex >= 0;
                _lstModels.Enabled   = !busy;
            });
        }

        private void SafeUI(Action action)
        {
            if (IsDisposed) return;
            if (IsHandleCreated && InvokeRequired) Invoke(action);
            else action();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            if (FoundryLocalManager.IsInitialized)
                FoundryLocalManager.Instance.Dispose();
            base.OnFormClosing(e);
        }

        // ── UI factory helpers ─────────────────────────────────────────────────────

        private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

        // Returns a [GPU], [CPU], [GPU+CPU], etc. tag based on available variants.
        private static string DeviceTag(IModel m)
        {
            var types = m.Variants
                .Select(v => v.Info?.Runtime?.DeviceType ?? DeviceType.Invalid)
                .Where(d => d != DeviceType.Invalid)
                .Distinct()
                .ToList();
            if (!types.Any())
            {
                var own = m.Info?.Runtime?.DeviceType ?? DeviceType.Invalid;
                if (own != DeviceType.Invalid) types.Add(own);
            }
            bool gpu = types.Contains(DeviceType.GPU);
            bool npu = types.Contains(DeviceType.NPU);
            bool cpu = types.Contains(DeviceType.CPU) || !types.Any();
            var parts = new List<string>();
            if (gpu) parts.Add("GPU");
            if (npu) parts.Add("NPU");
            if (cpu) parts.Add("CPU");
            return $"[{string.Join("+", parts.DefaultIfEmpty("CPU"))}]";
        }

        private static Panel MakePanel(DockStyle dock, Color back, int width = 0, int height = 0)
        {
            var p = new Panel { Dock = dock, BackColor = back };
            if (width  > 0) p.Width  = width;
            if (height > 0) p.Height = height;
            return p;
        }

        private static Label MakeLabel(string text, DockStyle dock, int height, bool bold = false)
            => new Label
            {
                Text      = text,
                Dock      = dock,
                Height    = height,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 10F, bold ? FontStyle.Bold : FontStyle.Regular)
            };

        private static Button MakeButton(
            string text, Color back, DockStyle dock, int height = 0, int width = 0)
        {
            var b = new Button
            {
                Text      = text,
                Dock      = dock,
                BackColor = back,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor    = Cursors.Hand
            };
            b.FlatAppearance.BorderSize         = 0;
            b.FlatAppearance.MouseOverBackColor  = ControlPaint.Light(back, 0.2f);
            b.FlatAppearance.MouseDownBackColor  = ControlPaint.Dark(back, 0.1f);
            if (height > 0) b.Height = height;
            if (width  > 0) b.Width  = width;
            return b;
        }
    }
}
