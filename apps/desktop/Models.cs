using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeepSeekHarness.Desktop;

/// <summary>单条对话消息（一条用户消息 + 对应的一条助手回复）。</summary>
public sealed class ChatMessage : NotifyObject
{
    private string _text = string.Empty;
    private string _thinking = string.Empty;
    private bool _isStreaming;

    public ChatMessage(string role, string text)
    {
        Role = role;
        _text = text;
        CreatedAt = DateTimeOffset.Now;
    }

    public string Role { get; }
    public DateTimeOffset CreatedAt { get; }

    /// <summary>助手回复的 Markdown 原文；用户消息为纯文本。</summary>
    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    /// <summary>模型的思考 / 推理内容（可折叠展示）。</summary>
    public string Thinking
    {
        get => _thinking;
        set => SetField(ref _thinking, value);
    }

    /// <summary>是否正在流式接收内容（用于状态指示）。</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetField(ref _isStreaming, value);
    }

    /// <summary>本条回复过程中的工具调用卡片。</summary>
    public ObservableCollection<ToolCallItem> ToolCalls { get; } = [];

    /// <summary>本条回复携带的待办清单快照。</summary>
    public ObservableCollection<TodoItem> Todos { get; } = [];
}

/// <summary>一次工具调用的展示卡片。</summary>
public sealed class ToolCallItem : NotifyObject
{
    private string _status = "in_progress";
    private string _rawInput = string.Empty;
    private string _rawOutput = string.Empty;
    private bool _isExpanded = true;

    public ToolCallItem(string toolCallId, string title, string kind)
    {
        ToolCallId = toolCallId;
        Title = title;
        Kind = kind;
    }

    public string ToolCallId { get; }
    public string Title { get; }
    public string Kind { get; }

    public string Status
    {
        get => _status;
        set
        {
            if (!SetField(ref _status, value)) return;
            Notify(nameof(StatusLabel));
            Notify(nameof(IsRunning));
            Notify(nameof(IsCompleted));
        }
    }

    public string RawInput
    {
        get => _rawInput;
        set => SetField(ref _rawInput, value);
    }

    public string RawOutput
    {
        get => _rawOutput;
        set => SetField(ref _rawOutput, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsRunning => Status == "in_progress";
    public bool IsCompleted => Status is "completed" or "failed";

    public string StatusLabel => Status switch
    {
        "completed" => "已完成",
        "failed" => "失败",
        _ => "进行中",
    };

    /// <summary>美观化的工具参数 JSON。</summary>
    public string PrettyInput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RawInput)) return string.Empty;
            try
            {
                var element = System.Text.Json.JsonDocument.Parse(RawInput).RootElement;
                return System.Text.Json.JsonSerializer.Serialize(element, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return RawInput;
            }
        }
    }
}

/// <summary>待办事项展示项。</summary>
public sealed class TodoItem : NotifyObject
{
    private string _status = "pending";

    public TodoItem(string content, string status)
    {
        Content = content;
        Status = status;
    }

    public string Content { get; }

    public string Status
    {
        get => _status;
        set
        {
            if (!SetField(ref _status, value)) return;
            Notify(nameof(IsChecked));
            Notify(nameof(IsInProgress));
        }
    }

    public bool IsChecked => Status == "completed";
    public bool IsInProgress => Status == "in_progress";

    public string Glyph => Status switch
    {
        "completed" => "✓",
        "in_progress" => "◐",
        _ => "○",
    };
}

public sealed class Conversation : NotifyObject
{
    private string _title = "新对话";
    private string? _sessionId;
    private string? _persistedId;
    private bool _isRunning;

    public Conversation(string workspace)
    {
        Workspace = workspace;
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public string Workspace { get; }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string? SessionId
    {
        get => _sessionId;
        set => SetField(ref _sessionId, value);
    }

    /// <summary>持久化存储中的会话 id（用于重启后恢复历史）。</summary>
    public string? PersistedId
    {
        get => _persistedId;
        set => SetField(ref _persistedId, value);
    }

    /// <summary>是否为待恢复的历史会话（有持久化 id 但尚未加载）。</summary>
    public bool NeedsRestore => _persistedId is not null && _sessionId is null;

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (!SetField(ref _isRunning, value)) return;
            Notify(nameof(CanSend));
        }
    }

    public bool CanSend => !IsRunning;

    /// <summary>最后一条助手消息（流式内容接收目标）。</summary>
    public ChatMessage? LastAssistantMessage => Messages.LastOrDefault(item => item.Role == "assistant");
}

public sealed class PendingPermission : NotifyObject
{
    public PendingPermission(string toolCallId, IReadOnlyList<PermissionOption> options)
    {
        ToolCallId = toolCallId;
        Options = options;
    }

    public string ToolCallId { get; }
    public IReadOnlyList<PermissionOption> Options { get; }

    public string ToolLabel
    {
        get
        {
            var part = ToolCallId.Split(':');
            return part.Length > 0 ? part[^1] : ToolCallId;
        }
    }
}

public sealed record PermissionOption(string Id, string Name, string Kind);

public abstract class NotifyObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
