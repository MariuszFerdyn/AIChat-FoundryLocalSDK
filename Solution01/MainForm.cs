using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AI.Foundry.Local;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;

namespace FoundryQA;

public partial class MainForm : Form
{
    private readonly string _modelAlias;

    private FoundryLocalManager? _manager;
    private IModel? _model;
    private OpenAIChatClient? _chatClient;
    private readonly List<ChatMessage> _history = new();
    private CancellationTokenSource _cts = new();
    private bool _isGenerating;

    public MainForm(string modelAlias)
    {
        _modelAlias = modelAlias;
        InitializeComponent();
        // Update header to show chosen model
        lblModel.Text = $"Model: {modelAlias}  •  Foundry Local (Orion)";
        _ = InitModelAsync();
    }

    // ── Model initialisation ─────────────────────────────────────────────────

    private async Task InitModelAsync()
    {
        SetStatus("Connecting to Foundry Local…", busy: true);

        try
        {
            // Reuse the singleton already initialised by ModelSelectForm
            _manager = FoundryLocalManager.Instance;

            SetStatus("Fetching model catalog…", busy: true);
            var catalog = await _manager.GetCatalogAsync();

            _model = await catalog.GetModelAsync(_modelAlias)
                ?? throw new Exception($"Model '{_modelAlias}' not found in catalog.");

            SetStatus($"Downloading {_modelAlias}…", busy: true, showProgress: true);
            await _model.DownloadAsync(pct =>
            {
                UpdateProgress((int)pct);
                SetStatus($"Downloading {_modelAlias}… {pct:F0}%", busy: true, showProgress: true);
            });

            SetStatus($"Loading {_modelAlias} into memory…", busy: true);
            await _model.LoadAsync();

            _chatClient = await _model.GetChatClientAsync();

            AppendSystemLine($"✅  {_modelAlias} is ready. Ask me anything!\n");
            SetStatus($"Ready  •  {_modelAlias}", busy: false);
            EnableInput(true);
        }
        catch (Exception ex)
        {
            AppendSystemLine($"❌  Failed to load model: {ex.Message}\n");
            SetStatus("Error – see chat for details", busy: false);
        }
    }

    // ── Send / generation ────────────────────────────────────────────────────

    private async Task OnSendAsync()
    {
        if (_chatClient is null || _isGenerating) return;

        var question = rtbInput.Text.Trim();
        if (string.IsNullOrEmpty(question)) return;

        rtbInput.Clear();
        EnableInput(false);
        _isGenerating = true;
        _cts = new CancellationTokenSource();

        // Add user bubble
        AppendUserLine(question);

        // Add to history (keeps context across turns)
        _history.Add(new ChatMessage { Role = "user", Content = question });

        // Start AI response label
        AppendAssistantLabel();

        try
        {
            SetStatus("Generating…", busy: true);

            var stream = _chatClient.CompleteChatStreamingAsync(_history, _cts.Token);
            var fullReply = new System.Text.StringBuilder();

            await foreach (var chunk in stream)
            {
                // Use null-conditional — end-of-stream chunks may have empty Choices
                var delta = chunk.Choices?.Count > 0
                    ? (chunk.Choices[0].Message?.Content ?? chunk.Choices[0].Delta?.Content ?? string.Empty)
                    : string.Empty;

                if (!string.IsNullOrEmpty(delta))
                {
                    fullReply.Append(delta);
                    AppendStreamDelta(delta);
                }
            }

            // Store assistant response in history
            _history.Add(new ChatMessage { Role = "assistant", Content = fullReply.ToString() });
            AppendNewline();
        }
        catch (OperationCanceledException)
        {
            AppendSystemLine("[Generation cancelled]\n");
        }
        catch (Exception ex) when (
            ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
            ex.InnerException is OperationCanceledException)
        {
            // SDK wraps end-of-stream cancellation in a non-OCE — treat as normal completion
            AppendNewline();
        }
        catch (Exception ex)
        {
            AppendSystemLine($"[Error: {ex.Message}]\n");
        }
        finally
        {
            _isGenerating = false;
            SetStatus($"Ready  •  {_modelAlias}", busy: false);
            EnableInput(true);
            rtbInput.Focus();
        }
    }

    private void RtbInput_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Enter or Shift+Enter → send; plain Enter adds newline
        if (e.KeyCode == Keys.Return && (e.Control || e.Shift))
        {
            e.SuppressKeyPress = true;
            _ = OnSendAsync();
        }
    }

    private void ClearChat()
    {
        if (_isGenerating)
        {
            _cts.Cancel();
        }
        rtbChat.Clear();
        _history.Clear();
        if (_chatClient != null)
            AppendSystemLine($"✅  Context cleared. Ask me anything!\n");
    }

    // ── UI helpers ───────────────────────────────────────────────────────────

    private void AppendUserLine(string text)
    {
        InvokeIfNeeded(() =>
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;

            // Label
            rtbChat.SelectionColor = Color.FromArgb(130, 180, 255);
            rtbChat.SelectionFont = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            rtbChat.AppendText("You  ");

            // Separator line
            rtbChat.SelectionColor = Color.FromArgb(60, 60, 80);
            rtbChat.AppendText(new string('─', 60) + "\n");

            // Text
            rtbChat.SelectionColor = Color.FromArgb(220, 220, 235);
            rtbChat.SelectionFont = new Font("Cascadia Code", 10f);
            rtbChat.AppendText(text + "\n\n");

            rtbChat.ScrollToCaret();
        });
    }

    private void AppendAssistantLabel()
    {
        InvokeIfNeeded(() =>
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionColor = Color.FromArgb(100, 220, 160);
            rtbChat.SelectionFont = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            rtbChat.AppendText("Phi-3  ");
            rtbChat.SelectionColor = Color.FromArgb(60, 60, 80);
            rtbChat.AppendText(new string('─', 58) + "\n");
            rtbChat.SelectionColor = Color.FromArgb(210, 230, 210);
            rtbChat.SelectionFont = new Font("Cascadia Code", 10f);
        });
    }

    private void AppendStreamDelta(string delta)
    {
        InvokeIfNeeded(() =>
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionColor = Color.FromArgb(210, 230, 210);
            rtbChat.SelectionFont = new Font("Cascadia Code", 10f);
            rtbChat.AppendText(delta);
            rtbChat.ScrollToCaret();
        });
    }

    private void AppendNewline()
    {
        InvokeIfNeeded(() => rtbChat.AppendText("\n\n"));
    }

    private void AppendSystemLine(string text)
    {
        InvokeIfNeeded(() =>
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionColor = Color.FromArgb(170, 130, 255);
            rtbChat.SelectionFont = new Font("Segoe UI", 9f, FontStyle.Italic);
            rtbChat.AppendText(text);
            rtbChat.ScrollToCaret();
        });
    }

    private void SetStatus(string text, bool busy, bool showProgress = false)
    {
        InvokeIfNeeded(() =>
        {
            lblStatus.Text = text;
            progressBar.Visible = showProgress;
            if (!showProgress) progressBar.Value = 0;
        });
    }

    private void UpdateProgress(int pct)
    {
        InvokeIfNeeded(() =>
        {
            progressBar.Value = Math.Clamp(pct, 0, 100);
        });
    }

    private void EnableInput(bool enable)
    {
        InvokeIfNeeded(() =>
        {
            btnSend.Enabled = enable;
            rtbInput.Enabled = enable;
            if (enable) rtbInput.Focus();
        });
    }

    private void InvokeIfNeeded(Action action)
    {
        if (InvokeRequired)
            Invoke(action);
        else
            action();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        _cts.Cancel();
        if (_model != null)
        {
            try { await _model.UnloadAsync(); } catch { }
        }
        base.OnFormClosing(e);
    }
}
