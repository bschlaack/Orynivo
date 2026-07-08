using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Orynivo.Mcp;

namespace Orynivo.AI;

/// <summary>
/// Embedded AI chat panel that streams responses from an OpenAI-compatible endpoint
/// and executes tool calls against the player bridge in real time.
/// </summary>
internal partial class AiChatView : UserControl
{
    private readonly ObservableCollection<AiChatMessageVm> _messages = [];
    private readonly AiChatService _service = new();
    private AiToolExecutor? _executor;
    private CancellationTokenSource? _cts;
    private bool _stickToBottom = true;

    /// <summary>Gets or sets the bridge used to dispatch tool calls to the Avalonia UI thread.</summary>
    public McpPlayerBridge? Bridge { get; set; }

    /// <summary>Gets or sets a delegate that returns the current AI chat settings on each send.</summary>
    public Func<AiChatSettings>? GetSettings { get; set; }

    /// <summary>Initializes the view.</summary>
    public AiChatView()
    {
        InitializeComponent();
        MessagesControl.ItemsSource = _messages;
        // Robust auto-scroll. Two facts drove this design:
        //  1) The streamed Markdown message rebuilds its content on every token, so the
        //     content extent keeps growing. LayoutUpdated on the ScrollViewer does NOT fire
        //     reliably for that (the ScrollViewer's own bounds don't change), which left the
        //     view pinned to a stale, too-high offset with the last lines clipped. The
        //     ScrollViewer DOES raise ScrollChanged when its extent grows, so that is the
        //     reliable trigger to re-pin to the true bottom.
        //  2) Those same extent/offset changes must never be mistaken for a user scroll, so
        //     sticky mode is released ONLY by a real mouse-wheel gesture.
        MessagesScrollViewer.LayoutUpdated += MessagesScrollViewer_OnLayoutUpdated;
        MessagesScrollViewer.ScrollChanged += MessagesScrollViewer_OnScrollChanged;
        MessagesScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            MessagesScrollViewer_OnWheel,
            RoutingStrategies.Tunnel);
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Updates the tool executor when the bridge is set.</summary>
    /// <param name="bridge">Player bridge to use for all tool executions.</param>
    public void SetBridge(McpPlayerBridge bridge)
    {
        Bridge = bridge;
        _executor = new AiToolExecutor(new Mcp.McpTools(bridge));
    }

    // ------------------------------------------------------------------ event handlers

    private void ChatInputBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private void SendButton_OnClick(object? sender, RoutedEventArgs e) => _ = SendAsync();

    private void ClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _service.ClearHistory();
        _messages.Clear();
    }

    // ------------------------------------------------------------------ core send logic

    private async Task SendAsync()
    {
        var text = ChatInputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var settings = GetSettings?.Invoke();
        if (settings is null || !settings.Enabled)
        {
            AddMessage(new AiChatMessageVm { Role = "error", Content = GetDisabledMessage() });
            return;
        }

        if (_executor is null && Bridge is not null)
            _executor = new AiToolExecutor(new Mcp.McpTools(Bridge));
        if (_executor is null) return;

        // Cancel any in-flight request
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ChatInputBox.Text = string.Empty;
        SetBusy(true);

        AddMessage(new AiChatMessageVm { Role = "user", Content = text });

        AiChatMessageVm? assistantMsg = null;
        var assistantHasVisibleContent = false;
        var sawToolCall = false;
        string? lastToolResult = null;

        try
        {
            await foreach (var ev in _service.SendAsync(text, settings, _executor, ct))
            {
                if (ct.IsCancellationRequested) break;

                switch (ev)
                {
                    case AiStreamEvent.TokenEvent tok:
                        if (assistantMsg is null && string.IsNullOrWhiteSpace(tok.Text))
                            break;
                        if (assistantMsg is null)
                        {
                            assistantMsg = new AiChatMessageVm { Role = "assistant", IsStreaming = true };
                            AddMessage(assistantMsg);
                        }
                        assistantMsg.Content += tok.Text;
                        if (!string.IsNullOrWhiteSpace(tok.Text))
                            assistantHasVisibleContent = true;
                        ScrollToBottom();
                        break;

                    case AiStreamEvent.ToolCallEvent toolEv:
                        if (assistantMsg is not null && string.IsNullOrWhiteSpace(assistantMsg.Content))
                            _messages.Remove(assistantMsg);
                        var toolMsg = new AiChatMessageVm
                        {
                            Role = "tool",
                            Content = toolEv.ToolName
                        };
                        AddMessage(toolMsg);
                        sawToolCall = true;
                        assistantHasVisibleContent = false;
                        // The next token stream replaces assistantMsg
                        assistantMsg = null;
                        break;

                    case AiStreamEvent.ToolResultEvent toolResultEv:
                        lastToolResult = toolResultEv.Result;
                        break;

                    case AiStreamEvent.ErrorEvent errEv:
                        AddMessage(new AiChatMessageVm { Role = "error", Content = errEv.Message });
                        break;

                    case AiStreamEvent.DoneEvent:
                        if (assistantMsg is not null)
                            assistantMsg.IsStreaming = false;
                        if (!assistantHasVisibleContent)
                            AddEmptyResponseFallback(sawToolCall, lastToolResult);
                        ScrollToBottom();
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* user cancelled */ }
        catch (Exception ex)
        {
            AddMessage(new AiChatMessageVm { Role = "error", Content = $"Error: {ex.Message}" });
        }
        finally
        {
            if (assistantMsg is not null)
                assistantMsg.IsStreaming = false;
            SetBusy(false);
        }
    }

    // ------------------------------------------------------------------ helpers

    private void AddMessage(AiChatMessageVm msg)
    {
        // A new message (or a fresh turn) always re-enables sticky auto-scroll.
        _stickToBottom = true;
        _messages.Add(msg);
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        _stickToBottom = true;
        // The actual scroll is performed by the LayoutUpdated handler once the new
        // content has been laid out; nudging here covers the case where no further
        // layout pass is pending.
        Dispatcher.UIThread.Post(ForceScrollToEnd, DispatcherPriority.Background);
    }

    private void ForceScrollToEnd()
    {
        var sv = MessagesScrollViewer;
        var target = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        if (Math.Abs(sv.Offset.Y - target) < 0.5)
        {
            MessagesBottomAnchor.BringIntoView();
            return;
        }

        sv.Offset = new Vector(sv.Offset.X, target);
        MessagesBottomAnchor.BringIntoView();
    }

    private void MessagesScrollViewer_OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_stickToBottom)
            ForceScrollToEnd();
    }

    private void MessagesScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Fires whenever the extent, viewport, or offset changes — including while the
        // streamed reply grows — so it is the reliable place to re-pin to the bottom.
        // It only *follows* the content; detaching is handled by the wheel gesture.
        if (_stickToBottom)
            ForceScrollToEnd();
    }

    private void MessagesScrollViewer_OnWheel(object? sender, PointerWheelEventArgs e)
    {
        // Positive Y = wheel up = the user wants to read earlier messages, so stop
        // following. Wheel down re-attaches once they return to the bottom.
        if (e.Delta.Y > 0)
        {
            _stickToBottom = false;
        }
        else if (e.Delta.Y < 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var sv = MessagesScrollViewer;
                if (sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 24)
                    _stickToBottom = true;
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>Copies a chat message's raw text to the clipboard.</summary>
    /// <param name="sender">The copy button whose data context is the message.</param>
    /// <param name="e">The click event data.</param>
    private async void CopyMessage_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AiChatMessageVm vm })
            return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(vm.Content ?? string.Empty);
    }

    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = !busy;
        ChatInputBox.IsEnabled = !busy;
    }

    /// <summary>Adds a visible fallback when the model finishes without answer text.</summary>
    /// <param name="sawToolCall">Whether at least one tool was executed during the turn.</param>
    /// <param name="lastToolResult">The most recent tool result, if one was received.</param>
    private void AddEmptyResponseFallback(bool sawToolCall, string? lastToolResult)
    {
        var s = Localization.LocalizationManager.Current;
        if (sawToolCall && !string.IsNullOrWhiteSpace(lastToolResult))
        {
            var prefix = string.IsNullOrWhiteSpace(s.AiChatToolResultFallback)
                ? "The model did not return a final answer. Tool result:"
                : s.AiChatToolResultFallback;
            AddMessage(new AiChatMessageVm
            {
                Role = "assistant",
                Content = $"{prefix}\n\n{lastToolResult.Trim()}",
            });
            return;
        }

        AddMessage(new AiChatMessageVm
        {
            Role = "error",
            Content = string.IsNullOrWhiteSpace(s.AiChatEmptyResponse)
                ? "The model returned an empty answer."
                : s.AiChatEmptyResponse,
        });
    }

    private static string GetDisabledMessage()
    {
        var s = Localization.LocalizationManager.Current;
        return string.IsNullOrEmpty(s.AiChatNotEnabled)
            ? "AI Chat is not enabled. Enable it in Settings > AI Chat."
            : s.AiChatNotEnabled;
    }
}
