using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace DeepSeekHarness.Desktop;

public sealed class MainViewModel : NotifyObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private AppSettings _settings;
    private Conversation? _selectedConversation;
    private AcpClient? _client;
    private string? _runtimeWorkspace;
    private string _inputText = string.Empty;
    private string _status = "就绪";
    private PendingPermission? _pendingPermission;
    private TaskCompletionSource<string?>? _permissionCompletion;
    private readonly DispatcherTimer _scrollTimer;

    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _settings = AppSettingsStore.Load();
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _scrollTimer.Tick += (_, _) => ScrollRequested?.Invoke();
        NewConversation();
        // 启动后在后台把持久化历史会话恢复到侧栏（失败时静默降级）。
        _ = RestoreHistoryAsync();
    }

    public ObservableCollection<Conversation> Conversations { get; } = [];
    public AppSettings Settings => _settings;

    public event Action? ScrollRequested;

    public Conversation? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (!SetField(ref _selectedConversation, value)) return;
            Notify(nameof(CanSend));
            Notify(nameof(CanStop));
            Notify(nameof(HasMessages));
            if (value is { NeedsRestore: true })
            {
                _ = LoadConversationHistoryAsync(value);
            }
        }
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (!SetField(ref _inputText, value)) return;
            Notify(nameof(CanSend));
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public PendingPermission? PendingPermission
    {
        get => _pendingPermission;
        private set
        {
            if (!SetField(ref _pendingPermission, value)) return;
            Notify(nameof(HasPendingPermission));
        }
    }

    public bool HasPendingPermission => PendingPermission is not null;
    public bool CanSend => SelectedConversation?.IsRunning == false && !string.IsNullOrWhiteSpace(InputText);
    public bool CanStop => SelectedConversation?.IsRunning == true;
    public bool HasMessages => SelectedConversation?.Messages.Count > 0;

    public string PermissionLabel => _settings.PermissionMode switch
    {
        "read-only" => "只读",
        "danger-full-access" => "完全访问",
        _ => "工作区写入",
    };

    /// <summary>工作区目录的展示名称（目录名；根目录时显示完整路径）。</summary>
    public string WorkspaceLabel
    {
        get
        {
            var path = _settings.Workspace;
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? path : name;
        }
    }

    public void NewConversation()
    {
        var conversation = new Conversation(_settings.Workspace);
        Conversations.Insert(0, conversation);
        SelectedConversation = conversation;
    }

    public async Task SendAsync()
    {
        var conversation = SelectedConversation;
        var prompt = InputText.Trim();
        if (conversation is null || conversation.IsRunning || prompt.Length == 0) return;
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("请先在设置中保存 DeepSeek API Key。");
        }
        if (!Directory.Exists(conversation.Workspace))
        {
            throw new DirectoryNotFoundException($"工作区不存在：{conversation.Workspace}");
        }

        // 历史会话：发送前先完成恢复（选择时会触发，此处兜底）。
        if (conversation.NeedsRestore)
        {
            await LoadConversationHistoryAsync(conversation);
        }

        InputText = string.Empty;
        conversation.Messages.Add(new ChatMessage("user", prompt));
        Notify(nameof(HasMessages));
        if (conversation.Messages.Count == 1)
        {
            conversation.Title = TitleFor(prompt);
        }
        conversation.IsRunning = true;
        Notify(nameof(CanSend));
        Notify(nameof(CanStop));
        Status = "Agent 正在工作";
        _scrollTimer.Start();

        try
        {
            var client = await EnsureClientAsync(conversation.Workspace);
            conversation.SessionId ??= await client.CreateSessionAsync(conversation.Workspace);
            await client.PromptAsync(conversation.SessionId, prompt);
            var assistant = conversation.LastAssistantMessage;
            if (assistant is null || (assistant.Text.Length == 0 && assistant.Thinking.Length == 0 && assistant.ToolCalls.Count == 0))
            {
                assistant = new ChatMessage("assistant", "任务已结束，没有返回文本。");
                conversation.Messages.Add(assistant);
                Status = "已完成（无文本返回）";
            }
            else
            {
                assistant.IsStreaming = false;
                Status = "已完成";
            }
        }
        catch (Exception error)
        {
            var assistant = conversation.LastAssistantMessage;
            if (assistant is null)
            {
                assistant = new ChatMessage("assistant", string.Empty);
                conversation.Messages.Add(assistant);
            }
            assistant.IsStreaming = false;
            assistant.Text = $"**运行失败：** {error.Message}";
            Status = "运行失败";
        }
        finally
        {
            conversation.IsRunning = false;
            _scrollTimer.Stop();
            Notify(nameof(CanSend));
            Notify(nameof(CanStop));
        }
    }

    public async Task StopAsync()
    {
        var conversation = SelectedConversation;
        if (_client is null || conversation?.SessionId is null || !conversation.IsRunning) return;
        Status = "正在停止";
        await _client.CancelAsync(conversation.SessionId);
        var assistant = conversation.LastAssistantMessage;
        if (assistant is not null) assistant.IsStreaming = false;
    }

    public async Task ChangeWorkspaceAsync(string workspace)
    {
        var fullPath = Path.GetFullPath(workspace);
        if (string.Equals(_settings.Workspace, fullPath, StringComparison.OrdinalIgnoreCase)) return;
        if (Conversations.Any(conversation => conversation.IsRunning))
        {
            throw new InvalidOperationException("请先停止正在运行的任务，再切换工作区。");
        }
        await StopRuntimeAsync();
        _settings.Workspace = fullPath;
        AppSettingsStore.Save(_settings);
        NewConversation();
        Notify(nameof(Settings));
        Notify(nameof(WorkspaceLabel));
        Status = "工作区已切换";
        _ = RestoreHistoryAsync();
    }

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        var restart = !string.Equals(_settings.Model, settings.Model, StringComparison.Ordinal)
            || !string.Equals(_settings.PermissionMode, settings.PermissionMode, StringComparison.Ordinal)
            || !string.Equals(_settings.Preset, settings.Preset, StringComparison.Ordinal)
            || !string.Equals(_settings.ApiKey, settings.ApiKey, StringComparison.Ordinal);
        _settings = settings.Copy();
        AppSettingsStore.Save(_settings);
        Notify(nameof(Settings));
        Notify(nameof(PermissionLabel));
        if (restart && !Conversations.Any(conversation => conversation.IsRunning)) await StopRuntimeAsync();
        Status = "设置已保存";
    }

    public void ResolvePermission(string? optionId)
    {
        var completion = _permissionCompletion;
        _permissionCompletion = null;
        PendingPermission = null;
        completion?.TrySetResult(optionId);
    }

    /// <summary>启动/切换工作区后，把该工作区的持久化历史会话补进侧栏。</summary>
    private async Task RestoreHistoryAsync()
    {
        try
        {
            var workspace = _settings.Workspace;
            var client = await EnsureClientAsync(workspace);
            var result = await client.ListSessionsAsync();
            if (!result.TryGetProperty("sessions", out var sessionsElement)) return;

            var seen = new HashSet<string>(Conversations
                .Select(conversation => conversation.PersistedId ?? conversation.SessionId ?? string.Empty)
                .Where(id => id.Length > 0));
            var restored = new List<(string Id, string Cwd, DateTimeOffset? Updated)>();
            foreach (var session in sessionsElement.EnumerateArray())
            {
                var id = session.TryGetProperty("sessionId", out var idElement) ? idElement.GetString() : null;
                if (id is null || id.Length == 0 || seen.Contains(id)) continue;
                var cwd = session.TryGetProperty("cwd", out var cwdElement) ? cwdElement.GetString() : null;
                if (!string.Equals(cwd, workspace, StringComparison.OrdinalIgnoreCase)) continue;
                DateTimeOffset? updated = null;
                if (session.TryGetProperty("updatedAt", out var updatedElement)
                    && DateTimeOffset.TryParse(updatedElement.GetString(), out var parsed))
                {
                    updated = parsed;
                }
                restored.Add((id, cwd ?? workspace, updated));
            }
            if (restored.Count == 0) return;

            _ = _dispatcher.BeginInvoke(() =>
            {
                foreach (var (id, cwd, updated) in restored.OrderByDescending(item => item.Updated ?? DateTimeOffset.MinValue))
                {
                    if (Conversations.Any(conversation => conversation.PersistedId == id || conversation.SessionId == id)) continue;
                    Conversations.Add(new Conversation(cwd)
                    {
                        PersistedId = id,
                        Title = "会话 · " + (updated?.ToLocalTime().ToString("MM-dd HH:mm") ?? "历史"),
                    });
                }
            });
        }
        catch (Exception)
        {
            // 运行时未启用持久化或历史不可用时静默跳过。
        }
    }

    /// <summary>把历史会话的持久化日志加载进当前会话（回放消息通知由通知处理器落地）。</summary>
    private async Task LoadConversationHistoryAsync(Conversation conversation)
    {
        if (!conversation.NeedsRestore) return;
        var persistedId = conversation.PersistedId!;
        try
        {
            var client = await EnsureClientAsync(conversation.Workspace);
            // 回放通知携带持久化 id，先路由到本会话，响应返回后再切换为续接的新 id。
            conversation.SessionId = persistedId;
            var result = await client.LoadSessionAsync(persistedId);
            var newId = result.TryGetProperty("sessionId", out var idElement) ? idElement.GetString() : null;
            if (!string.IsNullOrEmpty(newId)) conversation.SessionId = newId;
            Status = "已恢复历史会话";
        }
        catch (Exception)
        {
            conversation.SessionId = null;
            Status = "历史会话恢复失败";
        }
    }

    private async Task<AcpClient> EnsureClientAsync(string workspace)
    {
        if (_client is not null && string.Equals(_runtimeWorkspace, workspace, StringComparison.OrdinalIgnoreCase)) return _client;
        await StopRuntimeAsync();
        var client = new AcpClient();
        client.NotificationReceived += HandleNotification;
        client.DiagnosticReceived += diagnostic => _dispatcher.BeginInvoke(() => Status = diagnostic);
        client.PermissionRequested = HandlePermissionAsync;
        Status = "正在启动 Agent";
        await client.StartAsync(_settings);
        _client = client;
        _runtimeWorkspace = workspace;
        return client;
    }

    private void HandleNotification(string method, JsonElement parameters)
    {
        if (method != "session/update") return;
        if (!parameters.TryGetProperty("sessionId", out var sessionElement)) return;
        var sessionId = sessionElement.GetString();
        if (sessionId is null) return;
        var update = parameters.GetProperty("update");
        var type = update.TryGetProperty("sessionUpdate", out var typeElement)
            ? typeElement.GetString()
            : null;
        switch (type)
        {
            case "user_message_chunk":
                HandleUserChunk(sessionId, update);
                break;
            case "agent_message_chunk":
                HandleTextChunk(sessionId, update);
                break;
            case "agent_thought_chunk":
                HandleThoughtChunk(sessionId, update);
                break;
            case "tool_call":
                HandleToolCall(sessionId, update);
                break;
            case "tool_call_update":
                HandleToolCallUpdate(sessionId, update);
                break;
        }
    }

    private void HandleUserChunk(string sessionId, JsonElement update)
    {
        if (!update.TryGetProperty("content", out var content) || content.GetProperty("type").GetString() != "text") return;
        var text = content.GetProperty("text").GetString();
        if (string.IsNullOrEmpty(text)) return;
        _dispatcher.BeginInvoke(() =>
        {
            var conversation = FindConversation(sessionId);
            if (conversation is null) return;
            // 历史回放时追加用户消息；去重避免与本地添加的相同消息重复。
            if (conversation.Messages.LastOrDefault(item => item.Role == "user")?.Text == text) return;
            conversation.Messages.Add(new ChatMessage("user", text));
            if (conversation.Messages.Count == 1)
            {
                conversation.Title = TitleFor(text);
            }
            Notify(nameof(HasMessages));
        });
    }

    private void HandleTextChunk(string sessionId, JsonElement update)
    {
        if (!update.TryGetProperty("content", out var content) || content.GetProperty("type").GetString() != "text") return;
        var text = content.GetProperty("text").GetString();
        if (string.IsNullOrEmpty(text)) return;
        _dispatcher.BeginInvoke(() =>
        {
            var message = EnsureCurrentAssistant(sessionId);
            if (message is null) return;
            message.Text += text;
            message.IsStreaming = true;
            Status = "正在生成回复";
        });
    }

    private void HandleThoughtChunk(string sessionId, JsonElement update)
    {
        // DSH 扩展：待办快照搭载在 _meta["dsh:todos"] 上
        if (update.TryGetProperty("_meta", out var meta)
            && meta.TryGetProperty("dsh:todos", out var todosElement)
            && todosElement.ValueKind == JsonValueKind.Array)
        {
            _dispatcher.BeginInvoke(() =>
            {
                var message = EnsureCurrentAssistant(sessionId);
                if (message is null) return;
                message.Todos.Clear();
                foreach (var item in todosElement.EnumerateArray())
                {
                    var content = item.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                    var status = item.TryGetProperty("status", out var s) ? s.GetString() ?? "pending" : "pending";
                    message.Todos.Add(new TodoItem(content, status));
                }
            });
            return;
        }
        if (!update.TryGetProperty("content", out var content) || content.GetProperty("type").GetString() != "text") return;
        var text = content.GetProperty("text").GetString();
        if (string.IsNullOrEmpty(text)) return;
        _dispatcher.BeginInvoke(() =>
        {
            var message = EnsureCurrentAssistant(sessionId);
            if (message is null) return;
            message.Thinking += text;
            Status = "正在思考";
        });
    }

    private void HandleToolCall(string sessionId, JsonElement update)
    {
        var id = update.TryGetProperty("toolCallId", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
        var title = update.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "工具" : "工具";
        var kind = update.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() ?? "other" : "other";
        var rawInput = update.TryGetProperty("rawInput", out var inputElement) ? inputElement.GetRawText() : string.Empty;
        var status = update.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "in_progress" : "in_progress";
        _dispatcher.BeginInvoke(() =>
        {
            var message = EnsureCurrentAssistant(sessionId);
            if (message is null) return;
            var card = message.ToolCalls.FirstOrDefault(item => item.ToolCallId == id);
            if (card is null)
            {
                card = new ToolCallItem(id, title, kind) { RawInput = rawInput, Status = status };
                message.ToolCalls.Add(card);
            }
            else
            {
                card.RawInput = string.IsNullOrEmpty(card.RawInput) ? rawInput : card.RawInput;
                card.Status = status;
            }
            Status = "正在执行工具";
        });
    }

    private void HandleToolCallUpdate(string sessionId, JsonElement update)
    {
        var id = update.TryGetProperty("toolCallId", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
        var status = update.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var rawOutput = update.TryGetProperty("rawOutput", out var outputElement) ? outputElement.GetString() : null;
        _dispatcher.BeginInvoke(() =>
        {
            var message = EnsureCurrentAssistant(sessionId);
            if (message is null) return;
            var card = message.ToolCalls.FirstOrDefault(item => item.ToolCallId == id);
            if (card is null)
            {
                card = new ToolCallItem(id, "工具", "other");
                message.ToolCalls.Add(card);
            }
            if (status is not null) card.Status = status;
            if (!string.IsNullOrEmpty(rawOutput)) card.RawOutput = rawOutput;
        });
    }

    /// <summary>按会话 id 查找会话。</summary>
    private Conversation? FindConversation(string sessionId) =>
        Conversations.FirstOrDefault(item => item.SessionId == sessionId);

    /// <summary>取当前回复的助手消息；不存在（流式首包/历史回放）时创建并追加。</summary>
    private ChatMessage? EnsureCurrentAssistant(string sessionId)
    {
        var conversation = FindConversation(sessionId);
        if (conversation is null) return null;
        var last = conversation.Messages.LastOrDefault();
        if (last is { Role: "assistant" })
        {
            last.IsStreaming = true;
            return last;
        }
        var message = new ChatMessage("assistant", string.Empty) { IsStreaming = true };
        conversation.Messages.Add(message);
        Notify(nameof(HasMessages));
        return message;
    }

    private static string TitleFor(string text) =>
        text.Length <= 24 ? text : text[..24] + "…";

    private Task<string?> HandlePermissionAsync(PendingPermission permission)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.BeginInvoke(() =>
        {
            _permissionCompletion?.TrySetResult(null);
            _permissionCompletion = completion;
            PendingPermission = permission;
            Status = "等待工具授权";
        });
        return completion.Task;
    }

    private async Task StopRuntimeAsync()
    {
        ResolvePermission(null);
        var client = _client;
        _client = null;
        _runtimeWorkspace = null;
        if (client is not null) await client.DisposeAsync();
        foreach (var conversation in Conversations)
        {
            conversation.SessionId = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _scrollTimer.Stop();
        await StopRuntimeAsync();
    }
}
