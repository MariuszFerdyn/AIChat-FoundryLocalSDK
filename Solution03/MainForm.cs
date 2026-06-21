using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AI.Foundry.Local;
using Microsoft.AI.Foundry.Local.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace FoundrySTT
{
    public sealed class MainForm : Form
    {
        // ── UI controls ───────────────────────────────────────────────────────────
        private readonly RichTextBox  _rtbHardware;
        private readonly ComboBox     _cmbMic;
        private readonly ComboBox     _cmbEngine;
        private readonly Label        _lblEngineInfo;
        private readonly Button       _btnLoad;
        private readonly ProgressBar  _progressBar;
        private readonly Label        _lblProgress;
        private readonly RichTextBox  _rtbTranscript;
        private readonly Button       _btnRecord;
        private readonly Button       _btnClear;
        private readonly Label        _lblStatus;

        // ── State ─────────────────────────────────────────────────────────────────
        private List<IModel>          _transcriptionModels = new List<IModel>();
        private IModel?               _activeModel;
        private bool                  _isRecording;
        private bool                  _busy;

        // ── Windows STT ───────────────────────────────────────────────────────────
        private SpeechRecognitionEngine? _sre;
        private int                   _interimStart = -1;

        // ── NAudio / Foundry Whisper (SDK native, no HTTP) ────────────────────────
        private WaveInEvent?                  _waveIn;
        private OpenAIAudioClient?            _audioClient;
        private LiveAudioTranscriptionSession? _liveSession;
        private Task?                         _liveStreamTask;
        private CancellationTokenSource?      _liveCts;

        // ── Colour palette ────────────────────────────────────────────────────────
        private static readonly Color BgDark    = Rgb(32,  33,  35);
        private static readonly Color BgPanel   = Rgb(52,  53,  65);
        private static readonly Color BgInput   = Rgb(64,  65,  79);
        private static readonly Color BgList    = Rgb(40,  41,  52);
        private static readonly Color Teal      = Rgb(16, 163, 127);
        private static readonly Color TextDim   = Rgb(160, 160, 180);
        private static readonly Color TextBright= Rgb(220, 220, 230);
        private static readonly Color ColRed    = Rgb(200,  55,  55);
        private static readonly Color ColGold   = Rgb(255, 198,  80);
        private static readonly Color ColGreen  = Rgb( 86, 180, 120);
        private static readonly Color ColGray   = Rgb(140, 140, 160);

        // ─────────────────────────────────────────────────────────────────────────

        public MainForm()
        {
            Text          = "Foundry Voice to Text";
            Size          = new Size(1060, 720);
            MinimumSize   = new Size(800, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BgDark;
            ForeColor     = Color.White;
            Font          = new Font("Segoe UI", 10F);

            // ── LEFT PANEL ────────────────────────────────────────────────────────
            var left = MakePanel(DockStyle.Left, BgPanel, width: 320);
            left.Padding = new Padding(8, 8, 8, 0);

            // Hardware
            var hwTitle = MakeLabel("🖥  Hardware", DockStyle.Top, 28, bold: true);
            _rtbHardware = new RichTextBox
            {
                Dock = DockStyle.Top, Height = 82,
                BackColor = BgList, ForeColor = TextBright,
                BorderStyle = BorderStyle.None, ReadOnly = true,
                Font = new Font("Consolas", 9F), ScrollBars = RichTextBoxScrollBars.None,
                Text = "  Detecting hardware…"
            };

            // Microphone
            var sep1 = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = BgPanel };
            var micTitle = MakeLabel("🎙  Microphone", DockStyle.Top, 28, bold: true);
            _cmbMic = new ComboBox
            {
                Dock = DockStyle.Top,
                BackColor = BgInput, ForeColor = TextBright,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            var micNote = MakeLabel("  Mic selection applies to Whisper mode", DockStyle.Top, 22);
            micNote.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            micNote.ForeColor = ColGray;

            // Engine / model
            var sep2 = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = BgPanel };
            var engTitle = MakeLabel("🧠  Engine / Model", DockStyle.Top, 28, bold: true);
            _cmbEngine = new ComboBox
            {
                Dock = DockStyle.Top,
                BackColor = BgInput, ForeColor = TextBright,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            _cmbEngine.SelectedIndexChanged += OnEngineSelected;

            _lblEngineInfo = MakeLabel("", DockStyle.Top, 36);
            _lblEngineInfo.Font = new Font("Segoe UI", 8.5F, FontStyle.Italic);
            _lblEngineInfo.ForeColor = TextDim;
            _lblEngineInfo.Padding = new Padding(2, 2, 0, 0);

            // Bottom: load button + progress
            var foot = MakePanel(DockStyle.Bottom, BgPanel, height: 92);
            foot.Padding = new Padding(0, 6, 0, 8);

            _btnLoad = MakeButton("⬇  Download & Load", Teal, DockStyle.Top, height: 36);
            _btnLoad.Enabled = false;
            _btnLoad.Click += async (_, __) => await OnLoadAsync();

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top, Height = 8,
                Style = ProgressBarStyle.Continuous, Visible = false
            };
            _lblProgress = MakeLabel("", DockStyle.Top, 24);
            _lblProgress.Font = new Font("Segoe UI", 9F);
            _lblProgress.ForeColor = TextDim;

            foot.Controls.Add(_lblProgress);
            foot.Controls.Add(_progressBar);
            foot.Controls.Add(_btnLoad);

            // Assemble left panel (dock order: Top items first, then Fill spacer, then Bottom)
            left.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = BgPanel });
            left.Controls.Add(foot);
            left.Controls.Add(_lblEngineInfo);
            left.Controls.Add(_cmbEngine);
            left.Controls.Add(engTitle);
            left.Controls.Add(sep2);
            left.Controls.Add(micNote);
            left.Controls.Add(_cmbMic);
            left.Controls.Add(micTitle);
            left.Controls.Add(sep1);
            left.Controls.Add(_rtbHardware);
            left.Controls.Add(hwTitle);

            // ── RIGHT / TRANSCRIPT PANEL ──────────────────────────────────────────
            var right = MakePanel(DockStyle.Fill, BgPanel);

            _lblStatus = MakeLabel("⏳  Initializing…", DockStyle.Top, 28);
            _lblStatus.Font = new Font("Segoe UI", 9F);
            _lblStatus.ForeColor = TextDim;
            _lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            _lblStatus.BackColor = BgDark;

            _rtbTranscript = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = BgPanel, ForeColor = Color.White,
                BorderStyle = BorderStyle.None, ReadOnly = true,
                Font = new Font("Segoe UI", 13F),
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Padding = new Padding(14)
            };

            var bottomBar = MakePanel(DockStyle.Bottom, BgInput, height: 56);
            bottomBar.Padding = new Padding(10, 8, 10, 8);

            _btnClear = MakeButton("🗑  Clear", Rgb(75, 75, 95), DockStyle.Right, width: 100);
            _btnClear.Click += (_, __) => { _rtbTranscript.Clear(); _interimStart = -1; };

            _btnRecord = MakeButton("🎤  Start Recording", Teal, DockStyle.Fill);
            _btnRecord.Enabled = false;
            _btnRecord.Click += async (_, __) => await ToggleRecordingAsync();

            bottomBar.Controls.Add(_btnClear);
            bottomBar.Controls.Add(_btnRecord);

            right.Controls.Add(_rtbTranscript);
            right.Controls.Add(bottomBar);
            right.Controls.Add(_lblStatus);

            // ── SPLITTER ──────────────────────────────────────────────────────────
            var splitter = new Splitter { Dock = DockStyle.Left, Width = 3, BackColor = Rgb(80, 81, 95) };

            Controls.Add(right);
            Controls.Add(splitter);
            Controls.Add(left);

            Load        += async (_, __) => await InitAsync();
            FormClosing += OnFormClosing;
        }

        // ── Initialisation ────────────────────────────────────────────────────────

        private async Task InitAsync()
        {
            await Task.Run(() => LoadHardwareInfo());
            LoadMicrophones();

            // Windows Speech Recognition is always available
            SafeUI(() => _cmbEngine.Items.Add(new EngineItem(
                "Windows Speech Recognition  (built-in, near-realtime)", null)));

            try
            {
                Status("⏳  Starting Foundry Local…");
                await FoundryLocalManager.CreateAsync(
                    new Configuration { AppName = "FoundrySTT" },
                    NullLogger.Instance);

                Status("📋  Loading model catalog…");
                var catalog = await FoundryLocalManager.Instance.GetCatalogAsync();
                var all     = (await catalog.ListModelsAsync()).ToList();

                // Look for transcription / ASR / Whisper models
                _transcriptionModels = all.Where(m =>
                    ContainsAny(m.Info?.Task    ?? "", "transcri", "speech", "asr", "whisper") ||
                    ContainsAny(m.Alias         ?? "", "whisper") ||
                    ContainsAny(m.Info?.DisplayName ?? "", "whisper", "transcri")
                ).ToList();

                SafeUI(() =>
                {
                    foreach (var m in _transcriptionModels)
                    {
                        string size   = m.Info?.FileSizeMb.HasValue == true
                            ? $"  ({m.Info.FileSizeMb.Value / 1024.0:F1} GB)" : "";
                        bool   cached = m.Info?.Cached ?? false;
                        string mark   = cached ? "✓ " : "";
                        _cmbEngine.Items.Add(new EngineItem(
                            $"{mark}Foundry: {m.Alias}{size}", m));
                    }
                });

                string note = _transcriptionModels.Count > 0
                    ? $"✅  Ready — {_transcriptionModels.Count} Foundry transcription model(s) found"
                    : "✅  Ready — no Foundry transcription models in catalog; using Windows STT";
                Status(note);
            }
            catch (Exception ex)
            {
                File.AppendAllText(Program.LogFile, ex + Environment.NewLine);
                Status("⚠  Foundry Local unavailable — using Windows Speech Recognition");
            }
            finally
            {
                SafeUI(() =>
                {
                    if (_cmbEngine.Items.Count > 0)
                        _cmbEngine.SelectedIndex = 0;
                });
            }
        }

        private void LoadMicrophones()
        {
            SafeUI(() =>
            {
                _cmbMic.Items.Clear();
                int count = WaveInEvent.DeviceCount;
                for (int i = 0; i < count; i++)
                {
                    var caps = WaveIn.GetCapabilities(i);
                    _cmbMic.Items.Add(new MicItem(caps.ProductName, i));
                }
                if (_cmbMic.Items.Count == 0)
                    _cmbMic.Items.Add(new MicItem("Default microphone", 0));
                _cmbMic.SelectedIndex = 0;
            });
        }

        private void LoadHardwareInfo()
        {
            try
            {
                string cpu = "Unknown"; int cores = 0;
                using (var q = new ManagementObjectSearcher(
                    "SELECT Name, NumberOfCores FROM Win32_Processor"))
                    foreach (ManagementObject o in q.Get())
                    { cpu = o["Name"]?.ToString()?.Trim() ?? cpu; cores = Convert.ToInt32(o["NumberOfCores"]); break; }

                long ramTotal = 0;
                using (var q = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in q.Get())
                    { ramTotal = Convert.ToInt64(o["TotalPhysicalMemory"]); break; }

                SafeUI(() =>
                {
                    _rtbHardware.Clear();
                    void Line(string lbl, string val, Color c)
                    {
                        _rtbHardware.SelectionColor = TextDim;
                        _rtbHardware.SelectionFont  = new Font("Consolas", 9F);
                        _rtbHardware.AppendText($"  {lbl,-5}");
                        _rtbHardware.SelectionColor = c;
                        _rtbHardware.AppendText($"{val}\n");
                    }
                    Line("CPU:", $"{cpu}  ({cores} cores)", ColGold);
                    Line("RAM:", $"{ramTotal / (1024.0 * 1024 * 1024):F1} GB", ColGreen);
                    Line("MIC:", $"{WaveInEvent.DeviceCount} input device(s) detected", Teal);
                });
            }
            catch { SafeUI(() => _rtbHardware.Text = "  Hardware info unavailable"); }
        }

        // ── Engine selection ──────────────────────────────────────────────────────

        private void OnEngineSelected(object? sender, EventArgs e)
        {
            var item = _cmbEngine.SelectedItem as EngineItem;
            if (item == null) return;

            if (item.Model == null)
            {
                // Windows STT — always ready
                _lblEngineInfo.Text = "  Real-time word-by-word • uses Windows default mic";
                _btnLoad.Enabled    = false;
                _btnRecord.Enabled  = !_busy;
            }
            else
            {
                bool cached = item.Model.Info?.Cached ?? false;
                long sizeMb = item.Model.Info?.FileSizeMb ?? 0;
                string sizeStr = sizeMb > 0 ? $"{sizeMb / 1024.0:F1} GB" : "unknown size";

                _lblEngineInfo.Text = cached
                    ? $"  ✓ Downloaded • {sizeStr} • chunk-based transcription"
                    : $"  Requires download • {sizeStr} • chunk-based transcription";

                _btnLoad.Enabled   = !cached && !_busy;
                _btnLoad.Text      = cached ? "✅  Already Downloaded" : "⬇  Download & Load";
                _btnRecord.Enabled = (cached || _activeModel?.Alias == item.Model.Alias) && !_busy;
            }
        }

        // ── Download & load a Foundry model ───────────────────────────────────────

        private async Task OnLoadAsync()
        {
            var item = _cmbEngine.SelectedItem as EngineItem;
            if (item?.Model == null) return;

            SetBusy(true);
            try
            {
                Status($"⬇  Downloading {item.Model.Alias}…");
                SafeUI(() => { _progressBar.Visible = true; _progressBar.Value = 0; });

                await item.Model.DownloadAsync(pct => SafeUI(() =>
                {
                    _progressBar.Value = Math.Min(100, (int)pct);
                    _lblProgress.Text  = $"{(int)pct}%";
                }));

                SafeUI(() => { _progressBar.Visible = false; _lblProgress.Text = ""; });

                Status($"⏳  Loading {item.Model.Alias} into memory…");
                await item.Model.LoadAsync();

                // Get the SDK-native audio client — no HTTP endpoint needed
                _audioClient = await item.Model.GetAudioClientAsync();
                _audioClient.Settings.Language = "en";

                _activeModel = item.Model;

                SafeUI(() =>
                {
                    _btnLoad.Text       = "✅  Loaded";
                    _btnLoad.Enabled    = false;
                    _btnRecord.Enabled  = true;
                    _lblEngineInfo.Text = $"  ✓ Loaded  •  SDK native in-process inference";
                });

                Status($"✅  {item.Model.Alias} ready — press Record to start");
            }
            catch (Exception ex)
            {
                Status($"❌  {ex.Message}");
                MessageBox.Show(ex.Message, "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                SafeUI(() => { _progressBar.Visible = false; _lblProgress.Text = ""; });
            }
        }

        // ── Record toggle ─────────────────────────────────────────────────────────

        private async Task ToggleRecordingAsync()
        {
            if (_isRecording) { await StopRecordingAsync(); return; }

            var item     = _cmbEngine.SelectedItem as EngineItem;
            int micIndex = (_cmbMic.SelectedItem as MicItem)?.DeviceIndex ?? 0;

            _isRecording = true;
            SafeUI(() =>
            {
                _btnRecord.Text      = "⏹  Stop Recording";
                _btnRecord.BackColor = ColRed;
                _cmbEngine.Enabled   = false;
                _cmbMic.Enabled      = false;
            });

            if (item?.Model == null)
                await Task.Run(() => StartWindowsStt());          // Windows STT
            else
                await StartWhisperCaptureAsync(micIndex);         // Foundry SDK native

            Status("🔴  Recording…  speak now");
        }

        private async Task StopRecordingAsync()
        {
            _isRecording = false;

            // Stop Windows STT
            try { _sre?.RecognizeAsyncStop(); } catch { }

            // Stop microphone capture
            try { _waveIn?.StopRecording(); } catch { }

            // Signal the live session to flush + finish, then wait for the stream task
            _liveCts?.Cancel();
            if (_liveSession != null)
            {
                var s = _liveSession;
                _liveSession = null;
                try { await s.StopAsync(); } catch { }
            }
            if (_liveStreamTask != null)
            {
                try { await _liveStreamTask; } catch { }
                _liveStreamTask = null;
            }

            SafeUI(() =>
            {
                _btnRecord.Text      = "🎤  Start Recording";
                _btnRecord.BackColor = Teal;
                _cmbEngine.Enabled   = true;
                _cmbMic.Enabled      = true;
            });
            Status("⏸  Stopped");
        }

        // ── Windows Speech Recognition ────────────────────────────────────────────

        private void StartWindowsStt()
        {
            try
            {
                _sre?.Dispose();
                _sre = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("en-US"));
                _sre.LoadGrammar(new DictationGrammar());

                // Near-realtime: show hypothesis (gray) as user speaks
                _sre.SpeechHypothesized        += (s, e) => SafeUI(() => ShowInterim(e.Result.Text));
                _sre.SpeechRecognized          += (s, e) => SafeUI(() => CommitText(e.Result.Text + " "));
                _sre.SpeechRecognitionRejected += (s, e) => SafeUI(ClearInterim);

                _sre.SetInputToDefaultAudioDevice();
                _sre.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch (Exception ex)
            {
                SafeUI(() =>
                {
                    Status($"❌  Windows STT error: {ex.Message}");
                    _isRecording         = false;
                    _btnRecord.Text      = "🎤  Start Recording";
                    _btnRecord.BackColor = Teal;
                });
            }
        }

        // ── Foundry Whisper — SDK-native live transcription (no HTTP) ─────────────

        private async Task StartWhisperCaptureAsync(int micIndex)
        {
            if (_audioClient == null)
            {
                SafeUI(() => AppendSystemLine("[Audio client not ready — load a model first]"));
                await StopRecordingAsync();
                return;
            }

            try
            {
                // Create and configure the live transcription session
                _liveSession = _audioClient.CreateLiveTranscriptionSession();
                _liveSession.Settings.SampleRate = 16000;
                _liveSession.Settings.Channels   = 1;
                _liveSession.Settings.Language   = "en";
                await _liveSession.StartAsync();

                _liveCts = new CancellationTokenSource();
                var session = _liveSession; // capture for closure

                // Background task: read transcription results as they arrive
                _liveStreamTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var result in session.GetStream())
                        {
                            string text = result.Content?.Count > 0
                                ? (result.Content[0].Text ?? result.Content[0].Transcript ?? "")
                                : "";

                            if (string.IsNullOrWhiteSpace(text)) continue;

                            if (result.IsFinal)
                                SafeUI(() => CommitText(text.Trim() + " "));
                            else
                                SafeUI(() => ShowInterim(text));
                        }
                    }
                    catch (OperationCanceledException) { /* normal stop */ }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Program.LogFile,
                            $"[{DateTime.Now:HH:mm:ss}] Whisper stream error: {ex}\n");
                        SafeUI(() => AppendSystemLine($"[Transcription error: {ex.Message}]"));
                    }
                });

                // Start microphone — push raw 16-bit PCM directly to the SDK session
                var waveFormat = new WaveFormat(16000, 16, 1);
                _waveIn = new WaveInEvent
                {
                    DeviceNumber       = micIndex,
                    WaveFormat         = waveFormat,
                    BufferMilliseconds = 100
                };
                _waveIn.DataAvailable += (s, e) =>
                {
                    if (session != null && _isRecording && e.BytesRecorded > 0)
                        _ = session.AppendAsync(
                            new ReadOnlyMemory<byte>(e.Buffer, 0, e.BytesRecorded));
                };
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                File.AppendAllText(Program.LogFile, $"[{DateTime.Now:HH:mm:ss}] Start error: {ex}\n");
                SafeUI(() =>
                {
                    AppendSystemLine($"[Failed to start Whisper session: {ex.Message}]");
                    Status($"❌  {ex.Message}");
                });
                await StopRecordingAsync();
            }
        }

        // ── Transcript display ────────────────────────────────────────────────────

        // Show hypothesis as gray italic text (replaced on each update)
        private void ShowInterim(string text)
        {
            ClearInterim();
            _interimStart = _rtbTranscript.TextLength;
            _rtbTranscript.SelectionStart  = _interimStart;
            _rtbTranscript.SelectionColor  = ColGray;
            _rtbTranscript.SelectionFont   = new Font("Segoe UI", 13F, FontStyle.Italic);
            _rtbTranscript.AppendText(text);
            _rtbTranscript.ScrollToCaret();
        }

        private void ClearInterim()
        {
            if (_interimStart >= 0 && _interimStart < _rtbTranscript.TextLength)
            {
                _rtbTranscript.Select(
                    _interimStart,
                    _rtbTranscript.TextLength - _interimStart);
                _rtbTranscript.SelectedText = "";
            }
            _interimStart = -1;
        }

        // Commit a confirmed word/phrase as white text
        private void CommitText(string text)
        {
            ClearInterim();
            _rtbTranscript.SelectionStart = _rtbTranscript.TextLength;
            _rtbTranscript.SelectionColor = TextBright;
            _rtbTranscript.SelectionFont  = new Font("Segoe UI", 13F, FontStyle.Regular);
            _rtbTranscript.AppendText(text);
            _rtbTranscript.ScrollToCaret();
        }

        private void AppendSystemLine(string text)
        {
            _rtbTranscript.SelectionStart = _rtbTranscript.TextLength;
            _rtbTranscript.SelectionColor = ColGray;
            _rtbTranscript.SelectionFont  = new Font("Segoe UI", 10F, FontStyle.Italic);
            _rtbTranscript.AppendText("\n" + text + "\n");
        }

        // ── Cleanup ───────────────────────────────────────────────────────────────

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _isRecording = false;
            _liveCts?.Cancel();
            try { _sre?.RecognizeAsyncStop(); } catch { }
            try { _waveIn?.StopRecording(); } catch { }
            try { _liveSession?.StopAsync().GetAwaiter().GetResult(); } catch { }
            _sre?.Dispose();
            _waveIn?.Dispose();
            if (FoundryLocalManager.IsInitialized)
                try { FoundryLocalManager.Instance.Dispose(); } catch { }
        }

        // ── UI helpers ────────────────────────────────────────────────────────────

        private void Status(string msg) => SafeUI(() => _lblStatus.Text = msg);

        private void SetBusy(bool busy)
        {
            _busy = busy;
            SafeUI(() =>
            {
                _btnLoad.Enabled    = !busy;
                _cmbEngine.Enabled  = !busy;
                _cmbMic.Enabled     = !busy;
                _btnRecord.Enabled  = !busy;
            });
        }

        private void SafeUI(Action action)
        {
            if (IsDisposed) return;
            if (IsHandleCreated && InvokeRequired) Invoke(action);
            else action();
        }

        private static bool ContainsAny(string source, params string[] tokens)
            => tokens.Any(t => source.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

        private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

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
                Text      = text, Dock = dock, Height = height,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 10F, bold ? FontStyle.Bold : FontStyle.Regular)
            };

        private static Button MakeButton(
            string text, Color back, DockStyle dock, int height = 0, int width = 0)
        {
            var b = new Button
            {
                Text      = text, Dock = dock, BackColor = back, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor    = Cursors.Hand
            };
            b.FlatAppearance.BorderSize        = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.2f);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.1f);
            if (height > 0) b.Height = height;
            if (width  > 0) b.Width  = width;
            return b;
        }

        // ── Item types for combo boxes ────────────────────────────────────────────

        private sealed class EngineItem
        {
            public string  Label { get; }
            public IModel? Model { get; }
            public EngineItem(string label, IModel? model) { Label = label; Model = model; }
            public override string ToString() => Label;
        }

        private sealed class MicItem
        {
            public string Label       { get; }
            public int    DeviceIndex { get; }
            public MicItem(string label, int idx) { Label = label; DeviceIndex = idx; }
            public override string ToString() => Label;
        }
    }
}
