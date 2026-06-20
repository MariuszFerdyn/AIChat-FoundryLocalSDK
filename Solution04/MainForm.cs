using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryDocChat
{
    public sealed class MainForm : Form
    {
        // ── UI Controls ──────────────────────────────────────────────────────────
        private readonly ListBox     _lstModels;
        private readonly Label       _lblModelInfo;
        private readonly Button      _btnLoad;
        private readonly Button      _btnCancelLoad;
        private readonly ProgressBar _progress;
        private readonly Label       _lblProgress;

        private readonly Label       _lblDocName;
        private readonly RichTextBox _rtbDoc;
        private readonly Button      _btnOpenFile;
        private readonly Button      _btnClearDoc;
        private readonly Button      _btnSummarise;

        private readonly RichTextBox _rtbChat;
        private readonly TextBox     _txtInput;
        private readonly Button      _btnSend;
        private readonly Button      _btnClearChat;
        private readonly Label       _lblStatus;
        private readonly Label       _lblDocChars;

        // ── State ─────────────────────────────────────────────────────────────────
        private List<IModel>              _models      = new List<IModel>();
        private IModel?                   _activeModel;
        private OpenAIChatClient?         _chatClient;
        private readonly List<ChatMessage> _history    = new List<ChatMessage>();
        private CancellationTokenSource?  _cts;
        private bool                      _busy;
        private string                    _docContent  = string.Empty;
        private string                    _docFileName = string.Empty;

        // ── Palette ───────────────────────────────────────────────────────────────
        private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);
        private static readonly Color BgDark    = Rgb(28,  29,  34);
        private static readonly Color BgPanel   = Rgb(38,  39,  50);
        private static readonly Color BgList    = Rgb(34,  35,  46);
        private static readonly Color BgInput   = Rgb(50,  51,  64);
        private static readonly Color Teal      = Rgb(16, 163, 127);
        private static readonly Color TextDim   = Rgb(150, 150, 170);
        private static readonly Color TextBright= Rgb(220, 220, 230);
        private static readonly Color ColYou    = Rgb(86,  180, 120);
        private static readonly Color ColAi     = Rgb(86,  156, 214);
        private static readonly Color ColSys    = Rgb(150, 130, 200);
        private static readonly Color ColErr    = Rgb(220,  80,  80);
        private static readonly Color ColDoc    = Rgb(255, 198,  80);
        private static readonly Color Accent    = Rgb( 64, 140, 240);

        // ─────────────────────────────────────────────────────────────────────────

        public MainForm()
        {
            Text          = "Foundry Doc Q&A  —  Chat with your documents";
            Size          = new Size(1200, 800);
            MinimumSize   = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BgDark;
            ForeColor     = Color.White;
            Font          = new Font("Segoe UI", 10F);
            AllowDrop     = true;

            // ── STATUS BAR ────────────────────────────────────────────────────────
            _lblStatus = MakeLabel("Initialising Foundry Local…", DockStyle.Bottom, 24);
            _lblStatus.BackColor = BgPanel;
            _lblStatus.Padding   = new Padding(8, 0, 0, 0);

            // ── LEFT PANEL — model picker ──────────────────────────────────────────
            var left = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = 300,
                BackColor = BgPanel,
                Padding   = new Padding(8),
            };

            var lblModels = MakeLabel("📦  Models", DockStyle.Top, 28, bold: true);
            lblModels.Padding = new Padding(0, 4, 0, 2);

            _lstModels = new ListBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = BgList,
                ForeColor   = TextBright,
                BorderStyle = BorderStyle.None,
                Font        = new Font("Consolas", 9F),
            };
            _lstModels.SelectedIndexChanged += (s, e) => UpdateModelInfo();

            _lblModelInfo = MakeLabel("Select a model above", DockStyle.Bottom, 52);
            _lblModelInfo.BackColor = BgList;
            _lblModelInfo.Padding   = new Padding(4);
            _lblModelInfo.Font      = new Font("Consolas", 8F);
            _lblModelInfo.ForeColor = TextDim;

            var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 36 };
            btnRow.BackColor = BgPanel;

            _btnLoad = MakeButton("⬇  Download & Load", Teal);
            _btnLoad.Dock    = DockStyle.Fill;
            _btnLoad.Enabled = false;
            _btnLoad.Click  += async (s, e) => await LoadSelectedModelAsync();

            _btnCancelLoad = MakeButton("✕", ColErr);
            _btnCancelLoad.Dock    = DockStyle.Right;
            _btnCancelLoad.Width   = 36;
            _btnCancelLoad.Enabled = false;
            _btnCancelLoad.Click  += (s, e) => _cts?.Cancel();

            btnRow.Controls.Add(_btnLoad);
            btnRow.Controls.Add(_btnCancelLoad);

            _progress = new ProgressBar
            {
                Dock    = DockStyle.Bottom,
                Height  = 6,
                Minimum = 0,
                Maximum = 100,
                Visible = false,
                Style   = ProgressBarStyle.Continuous,
            };

            _lblProgress = MakeLabel("", DockStyle.Bottom, 18);
            _lblProgress.BackColor = BgPanel;
            _lblProgress.ForeColor = TextDim;
            _lblProgress.Font      = new Font("Consolas", 8F);
            _lblProgress.Padding   = new Padding(4, 0, 0, 0);

            left.Controls.Add(_lstModels);
            left.Controls.Add(lblModels);
            left.Controls.Add(_lblModelInfo);
            left.Controls.Add(btnRow);
            left.Controls.Add(_progress);
            left.Controls.Add(_lblProgress);

            // ── MIDDLE PANEL — document viewer ────────────────────────────────────
            var mid = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = 340,
                BackColor = BgDark,
                Padding   = new Padding(0, 0, 4, 0),
            };

            var docHeader = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgPanel };
            var lblDocTitle = MakeLabel("📄  Document", DockStyle.Left, 32, bold: true);
            lblDocTitle.Width   = 130;
            lblDocTitle.Padding = new Padding(8, 6, 0, 0);

            _lblDocChars = MakeLabel("", DockStyle.Left, 32);
            _lblDocChars.Width     = 90;
            _lblDocChars.ForeColor = TextDim;
            _lblDocChars.Font      = new Font("Consolas", 8F);
            _lblDocChars.Padding   = new Padding(4, 8, 0, 0);

            _btnClearDoc = MakeButton("✕  Clear", ColErr);
            _btnClearDoc.Dock    = DockStyle.Right;
            _btnClearDoc.Width   = 70;
            _btnClearDoc.Height  = 28;
            _btnClearDoc.Enabled = false;
            _btnClearDoc.Click  += (s, e) => ClearDocument();

            _btnSummarise = MakeButton("✦  Summarise", Accent);
            _btnSummarise.Dock    = DockStyle.Right;
            _btnSummarise.Width   = 100;
            _btnSummarise.Height  = 28;
            _btnSummarise.Enabled = false;
            _btnSummarise.Click  += (s, e) => AskQuestion("Please give a concise summary of this document. Highlight the key points.");

            docHeader.Controls.Add(lblDocTitle);
            docHeader.Controls.Add(_lblDocChars);
            docHeader.Controls.Add(_btnSummarise);
            docHeader.Controls.Add(_btnClearDoc);

            _lblDocName = MakeLabel("Drop a file here, or click Open", DockStyle.Top, 22);
            _lblDocName.BackColor = BgList;
            _lblDocName.ForeColor = ColDoc;
            _lblDocName.Font      = new Font("Consolas", 8F);
            _lblDocName.Padding   = new Padding(6, 4, 0, 4);

            _rtbDoc = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = BgList,
                ForeColor   = Rgb(200, 200, 215),
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Font        = new Font("Consolas", 9F),
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                Text        = "",
                AllowDrop   = true,
            };
            _rtbDoc.DragEnter += OnDragEnter;
            _rtbDoc.DragDrop  += OnDragDrop;

            var docFooter = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = BgPanel };
            _btnOpenFile = MakeButton("📂  Open File…", Teal);
            _btnOpenFile.Dock   = DockStyle.Fill;
            _btnOpenFile.Click += (s, e) => OpenFileDialog();

            docFooter.Controls.Add(_btnOpenFile);

            mid.Controls.Add(_rtbDoc);
            mid.Controls.Add(docHeader);
            mid.Controls.Add(_lblDocName);
            mid.Controls.Add(docFooter);

            // ── RIGHT PANEL — chat ────────────────────────────────────────────────
            var right = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = BgDark,
                Padding   = new Padding(4, 0, 0, 0),
            };

            var chatHeader = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgPanel };
            var lblChat = MakeLabel("💬  Chat", DockStyle.Left, 32, bold: true);
            lblChat.Width   = 100;
            lblChat.Padding = new Padding(8, 6, 0, 0);

            _btnClearChat = MakeButton("✕  Clear chat", ColErr);
            _btnClearChat.Dock   = DockStyle.Right;
            _btnClearChat.Width  = 110;
            _btnClearChat.Height = 28;
            _btnClearChat.Click += (s, e) => ClearChat();

            chatHeader.Controls.Add(lblChat);
            chatHeader.Controls.Add(_btnClearChat);

            _rtbChat = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = BgDark,
                ForeColor   = TextBright,
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Font        = new Font("Cascadia Code", 10F),
                ScrollBars  = RichTextBoxScrollBars.Vertical,
            };

            var inputPanel = new Panel { Dock = DockStyle.Bottom, Height = 80, BackColor = BgPanel };

            _txtInput = new TextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = BgInput,
                ForeColor   = TextBright,
                BorderStyle = BorderStyle.None,
                Font        = new Font("Segoe UI", 11F),
                Multiline   = true,
                ScrollBars  = ScrollBars.Vertical,
                Enabled     = false,
            };
            _txtInput.KeyDown += TxtInput_KeyDown;

            _btnSend = MakeButton("Send  ↵", Teal);
            _btnSend.Dock    = DockStyle.Right;
            _btnSend.Width   = 100;
            _btnSend.Enabled = false;
            _btnSend.Click  += async (s, e) => await SendAsync();

            inputPanel.Controls.Add(_txtInput);
            inputPanel.Controls.Add(_btnSend);

            right.Controls.Add(_rtbChat);
            right.Controls.Add(chatHeader);
            right.Controls.Add(inputPanel);

            // ── Assemble form ──────────────────────────────────────────────────────
            Controls.Add(right);
            Controls.Add(mid);
            Controls.Add(left);
            Controls.Add(_lblStatus);

            // ── Drag-and-drop on main form ─────────────────────────────────────────
            DragEnter += OnDragEnter;
            DragDrop  += OnDragDrop;

            // ── Start ─────────────────────────────────────────────────────────────
            _ = InitFoundryAsync();
        }

        // ── Foundry initialisation ────────────────────────────────────────────────

        private async Task InitFoundryAsync()
        {
            SetStatus("Starting Foundry Local…");
            try
            {
                var manager = FoundryLocalManager.Instance;
                SetStatus("Fetching model catalog…");
                var catalog = await manager.GetCatalogAsync();
                var models  = await catalog.GetModelsAsync();

                _models = models
                    .Where(m => m.Task == "chat" || m.Task == "text-generation")
                    .OrderBy(m => m.ModelId)
                    .ToList();

                InvokeIfNeeded(() =>
                {
                    _lstModels.Items.Clear();
                    foreach (var m in _models)
                        _lstModels.Items.Add(FormatModelEntry(m));

                    if (_lstModels.Items.Count > 0)
                        _lstModels.SelectedIndex = 0;

                    _btnLoad.Enabled = _lstModels.Items.Count > 0;
                    SetStatus($"Catalog loaded — {_models.Count} chat models available. Load a model to begin.");
                });

                AppendSys("Welcome to Foundry Doc Q&A!\n\n" +
                          "1. Load a model from the left panel.\n" +
                          "2. Open or drop a document (any text / code / markdown file).\n" +
                          "3. Ask questions about it — or click ✦ Summarise.\n\n" +
                          "You can also chat without a document for general Q&A.\n");
            }
            catch (Exception ex)
            {
                AppendSys($"⚠  Failed to connect to Foundry Local: {ex.Message}\n" +
                          "Make sure Foundry Local is installed and running.");
                SetStatus("Error — see chat for details.");
            }
        }

        private async Task LoadSelectedModelAsync()
        {
            if (_busy || _lstModels.SelectedIndex < 0) return;

            var model = _models[_lstModels.SelectedIndex];

            _busy = true;
            _btnLoad.Enabled       = false;
            _btnCancelLoad.Enabled = true;
            _cts = new CancellationTokenSource();

            try
            {
                SetStatus($"Downloading {model.ModelId}…");
                _progress.Visible = true;
                _lblProgress.Text = "  Downloading…";

                await model.DownloadAsync(pct =>
                {
                    InvokeIfNeeded(() =>
                    {
                        _progress.Value   = (int)Math.Max(0, Math.Min(100, pct));
                        _lblProgress.Text = $"  {pct:F0}%";
                    });
                });

                _progress.Visible = false;
                SetStatus($"Loading {model.ModelId} into memory…");
                _lblProgress.Text = "  Loading…";

                await model.LoadAsync();
                _chatClient   = await model.GetChatClientAsync();
                _activeModel  = model;

                _history.Clear();
                _txtInput.Enabled  = true;
                _btnSend.Enabled   = true;
                _btnSummarise.Enabled = !string.IsNullOrEmpty(_docContent);

                InvokeIfNeeded(() => _lblProgress.Text = "");
                SetStatus($"Ready  •  {model.ModelId}");
                AppendSys($"✅  {model.ModelId} loaded. Ask me anything!");
                if (!string.IsNullOrEmpty(_docContent))
                    AppendSys($"📄  Document "{_docFileName}" is active ({_docContent.Length:N0} chars) — questions will be answered in its context.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Load cancelled.");
                _progress.Visible = false;
                _lblProgress.Text = "";
            }
            catch (Exception ex)
            {
                AppendSys($"❌  Failed to load model: {ex.Message}");
                SetStatus("Error loading model.");
                _progress.Visible = false;
                _lblProgress.Text = "";
            }
            finally
            {
                _busy = false;
                _btnLoad.Enabled       = true;
                _btnCancelLoad.Enabled = false;
            }
        }

        // ── Document handling ─────────────────────────────────────────────────────

        private void OnDragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data?.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
                LoadDocument(files[0]);
        }

        private void OpenFileDialog()
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Open document",
                Filter = "Text & code files|*.txt;*.md;*.cs;*.py;*.js;*.ts;*.java;*.go;*.rs;*.json;*.yaml;*.yml;*.xml;*.html;*.css;*.sql;*.sh;*.ps1;*.bat;*.cpp;*.h;*.c|All files|*.*",
            };

            if (dlg.ShowDialog() == DialogResult.OK)
                LoadDocument(dlg.FileName);
        }

        private void LoadDocument(string path)
        {
            try
            {
                long size = new FileInfo(path).Length;
                if (size > 1_048_576) // 1 MB guard
                {
                    if (MessageBox.Show(
                        $"This file is {size / 1024:N0} KB. Large files may exceed the model context window and cause errors.\n\nLoad anyway?",
                        "Large file",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                }

                _docContent  = File.ReadAllText(path, Encoding.UTF8);
                _docFileName = Path.GetFileName(path);

                _rtbDoc.Text        = _docContent;
                _lblDocName.Text    = $"  📄  {_docFileName}";
                _lblDocChars.Text   = $"  {_docContent.Length:N0} chars";
                _btnClearDoc.Enabled = true;
                _btnSummarise.Enabled = _chatClient != null;

                // Clear chat and start fresh with new document context
                _history.Clear();
                _rtbChat.Clear();
                AppendSys($"📄  Loaded "{_docFileName}" ({_docContent.Length:N0} chars).\n" +
                          (_chatClient != null
                              ? "Ask questions about it, or click ✦ Summarise."
                              : "Load a model to start chatting about this document."));

                SetStatus($"Document loaded: {_docFileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load file:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearDocument()
        {
            _docContent  = string.Empty;
            _docFileName = string.Empty;
            _rtbDoc.Clear();
            _lblDocName.Text     = "Drop a file here, or click Open";
            _lblDocChars.Text    = "";
            _btnClearDoc.Enabled = false;
            _btnSummarise.Enabled = false;
            _history.Clear();
            _rtbChat.Clear();
            AppendSys("Document cleared. You can now load a new file or chat without a document.");
        }

        // ── Chat ──────────────────────────────────────────────────────────────────

        private void AskQuestion(string question)
        {
            if (_chatClient == null)
            {
                MessageBox.Show("Please load a model first.", "No model loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _txtInput.Text = question;
            _ = SendAsync();
        }

        private async Task SendAsync()
        {
            if (_chatClient == null || _busy) return;

            string question = _txtInput.Text.Trim();
            if (string.IsNullOrEmpty(question)) return;

            _txtInput.Clear();
            _busy = true;
            _txtInput.Enabled = false;
            _btnSend.Enabled  = false;
            _cts = new CancellationTokenSource();

            AppendUser(question);

            // Build message list: system prompt (with doc), then history, then user
            var messages = BuildMessages(question);

            AppendAiLabel();

            try
            {
                SetStatus("Generating…");

                var stream   = _chatClient.CompleteChatStreamingAsync(messages, _cts.Token);
                var fullResp = new StringBuilder();

                await foreach (var chunk in stream)
                {
                    string delta = chunk.Choices?.Count > 0
                        ? (chunk.Choices[0].Message?.Content ?? chunk.Choices[0].Delta?.Content ?? "")
                        : "";

                    if (!string.IsNullOrEmpty(delta))
                    {
                        fullResp.Append(delta);
                        AppendDelta(delta);
                    }
                }

                // Keep (question, answer) in history for multi-turn
                _history.Add(new ChatMessage { Role = "user",      Content = question });
                _history.Add(new ChatMessage { Role = "assistant", Content = fullResp.ToString() });

                AppendNewline();
                SetStatus($"Ready  •  {_activeModel?.ModelId}");
            }
            catch (OperationCanceledException)
            {
                AppendSys("[Cancelled]");
            }
            catch (Exception ex) when (
                ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
                ex.InnerException is OperationCanceledException)
            {
                AppendNewline(); // normal end-of-stream from SDK
                SetStatus($"Ready  •  {_activeModel?.ModelId}");
            }
            catch (Exception ex)
            {
                AppendSys($"[Error: {ex.Message}]");
                SetStatus("Error — see chat.");
            }
            finally
            {
                _busy = false;
                _txtInput.Enabled = true;
                _btnSend.Enabled  = true;
                _txtInput.Focus();
            }
        }

        private List<ChatMessage> BuildMessages(string newQuestion)
        {
            var msgs = new List<ChatMessage>();

            // System prompt — include document if loaded
            string systemPrompt;
            if (!string.IsNullOrEmpty(_docContent))
            {
                // Truncate to ~30 000 chars to stay within typical context windows
                string docSnippet = _docContent.Length > 30_000
                    ? _docContent.Substring(0, 30_000) + "\n\n[…document truncated at 30 000 chars…]"
                    : _docContent;

                systemPrompt =
                    "You are a helpful AI assistant. The user has loaded the following document.\n" +
                    "Answer questions accurately using the document as your primary source.\n" +
                    "If the answer is not in the document, say so clearly.\n\n" +
                    $"=== DOCUMENT: {_docFileName} ===\n{docSnippet}\n=== END OF DOCUMENT ===";
            }
            else
            {
                systemPrompt = "You are a helpful AI assistant.";
            }

            msgs.Add(new ChatMessage { Role = "system", Content = systemPrompt });

            // Prior turns (excluding system)
            foreach (var h in _history)
                msgs.Add(h);

            // New question
            msgs.Add(new ChatMessage { Role = "user", Content = newQuestion });

            return msgs;
        }

        private void ClearChat()
        {
            if (_busy) _cts?.Cancel();
            _history.Clear();
            _rtbChat.Clear();
            if (!string.IsNullOrEmpty(_docContent))
                AppendSys($"Chat cleared. Document "{_docFileName}" is still loaded.");
            else
                AppendSys("Chat cleared.");
        }

        // ── Input ─────────────────────────────────────────────────────────────────

        private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return && (e.Control || e.Shift))
            {
                e.SuppressKeyPress = true;
                _ = SendAsync();
            }
        }

        // ── Rich-text helpers ─────────────────────────────────────────────────────

        private void AppendUser(string text)
        {
            InvokeIfNeeded(() =>
            {
                _rtbChat.SelectionStart  = _rtbChat.TextLength;
                _rtbChat.SelectionColor  = ColYou;
                _rtbChat.SelectionFont   = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
                _rtbChat.AppendText("You  ");
                _rtbChat.SelectionColor  = Rgb(60, 60, 80);
                _rtbChat.AppendText(new string('─', 55) + "\n");
                _rtbChat.SelectionColor  = Rgb(215, 215, 230);
                _rtbChat.SelectionFont   = new Font("Segoe UI", 10F);
                _rtbChat.AppendText(text + "\n\n");
                _rtbChat.ScrollToCaret();
            });
        }

        private void AppendAiLabel()
        {
            InvokeIfNeeded(() =>
            {
                _rtbChat.SelectionStart = _rtbChat.TextLength;
                _rtbChat.SelectionColor = ColAi;
                _rtbChat.SelectionFont  = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
                _rtbChat.AppendText("AI  ");
                _rtbChat.SelectionColor = Rgb(60, 60, 80);
                _rtbChat.AppendText(new string('─', 56) + "\n");
                _rtbChat.SelectionColor = Rgb(205, 225, 210);
                _rtbChat.SelectionFont  = new Font("Cascadia Code", 10F);
            });
        }

        private void AppendDelta(string delta)
        {
            InvokeIfNeeded(() =>
            {
                _rtbChat.SelectionStart = _rtbChat.TextLength;
                _rtbChat.SelectionColor = Rgb(205, 225, 210);
                _rtbChat.SelectionFont  = new Font("Cascadia Code", 10F);
                _rtbChat.AppendText(delta);
                _rtbChat.ScrollToCaret();
            });
        }

        private void AppendNewline() => InvokeIfNeeded(() => _rtbChat.AppendText("\n\n"));

        private void AppendSys(string text)
        {
            InvokeIfNeeded(() =>
            {
                _rtbChat.SelectionStart = _rtbChat.TextLength;
                _rtbChat.SelectionColor = ColSys;
                _rtbChat.SelectionFont  = new Font("Segoe UI", 9F, FontStyle.Italic);
                _rtbChat.AppendText(text + "\n");
                _rtbChat.ScrollToCaret();
            });
        }

        // ── UI helpers ────────────────────────────────────────────────────────────

        private void UpdateModelInfo()
        {
            if (_lstModels.SelectedIndex < 0 || _lstModels.SelectedIndex >= _models.Count)
            {
                _lblModelInfo.Text = "";
                return;
            }
            var m = _models[_lstModels.SelectedIndex];
            _lblModelInfo.Text =
                $" ID:   {m.ModelId}\n" +
                $" Size: {(m.ModelSize > 0 ? $"{m.ModelSize / 1_073_741_824.0:F1} GB" : "unknown")}\n" +
                $" Task: {m.Task}";
        }

        private string FormatModelEntry(IModel m)
        {
            string size = m.ModelSize > 0
                ? $" [{m.ModelSize / 1_073_741_824.0:F1}G]"
                : "";
            return $"{m.ModelId}{size}";
        }

        private void SetStatus(string text)
        {
            InvokeIfNeeded(() => _lblStatus.Text = "  " + text);
        }

        private static Panel MakePanel(DockStyle dock, Color bg, int width = 0)
        {
            var p = new Panel { Dock = dock, BackColor = bg };
            if (width > 0) p.Width = width;
            return p;
        }

        private static Label MakeLabel(string text, DockStyle dock, int height, bool bold = false)
        {
            return new Label
            {
                Text      = text,
                Dock      = dock,
                Height    = height,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = bold
                    ? new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
                    : new Font("Segoe UI", 10F),
            };
        }

        private static Button MakeButton(string text, Color back)
        {
            return new Button
            {
                Text      = text,
                BackColor = back,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Cursor    = Cursors.Hand,
            };
        }

        private void InvokeIfNeeded(Action action)
        {
            if (InvokeRequired) Invoke(action);
            else action();
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            if (_activeModel != null)
            {
                try { await _activeModel.UnloadAsync(); } catch { }
            }
            base.OnFormClosing(e);
        }
    }
}
