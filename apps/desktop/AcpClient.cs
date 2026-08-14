using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DeepSeekHarness.Desktop;

public sealed class AcpClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private StreamWriter? _input;
    private Task? _readTask;
    private long _nextRequestId;

    public event Action<string, JsonElement>? NotificationReceived;
    public event Action<string>? DiagnosticReceived;
    public Func<PendingPermission, Task<string?>>? PermissionRequested { get; set; }

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (_process is not null) return;
        var runtimeRoot = ResolveRuntimeRoot();
        var nodePath = Path.Combine(runtimeRoot, "node.exe");
        var entryPath = Path.Combine(runtimeRoot, "node_modules", "@deepseek-ai", "dsh-acp-demo", "lib", "bin.js");
        var configPath = Path.Combine(runtimeRoot, "cordis.yml");
        foreach (var required in new[] { nodePath, entryPath, configPath })
        {
            if (!File.Exists(required)) throw new FileNotFoundException("桌面 Agent 运行时不完整，请重新运行 Windows 桌面打包脚本。", required);
        }

        Directory.CreateDirectory(AppSettingsStore.HarnessHome);
        Directory.CreateDirectory(AppSettingsStore.SessionsRoot);
        // 乱码修复：Node 运行时与桌面端通过 UTF-8 交换 JSON-RPC。
        // 不显式指定编码时，.NET 会按系统控制台代码页（中文 Windows 为 GBK）
        // 解码 UTF-8 字节流，导致中文与 emoji 变成乱码。
        var start = new ProcessStartInfo
        {
            FileName = nodePath,
            WorkingDirectory = settings.Workspace,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(entryPath);
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(configPath);
        start.Environment["DEEPSEEK_API_KEY"] = settings.ApiKey;
        start.Environment["DSH_HOME"] = AppSettingsStore.HarnessHome;
        start.Environment["DSH_SESSION_ROOT"] = AppSettingsStore.SessionsRoot;
        start.Environment["DSH_PERMISSION_MODE"] = settings.PermissionMode;
        start.Environment["DSH_DESKTOP_MODEL"] = settings.Model;
        start.Environment["DSH_DESKTOP_PRESET"] = settings.Preset;
        start.Environment["DSH_RUNTIME_ROOT"] = runtimeRoot;
        start.Environment["DSH_TELEMETRY_DISABLED"] = "1";

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("无法启动 DeepSeek Harness Agent 运行时。");
        _process = process;
        _input = process.StandardInput;
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) DiagnosticReceived?.Invoke(args.Data);
        };
        process.BeginErrorReadLine();
        _readTask = ReadLoopAsync(process.StandardOutput, _lifetime.Token);

        try
        {
            await RequestAsync("initialize", new { protocolVersion = 1, clientCapabilities = new { } }, cancellationToken);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task<string> CreateSessionAsync(string workspace, CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync("session/new", new { cwd = workspace, mcpServers = Array.Empty<object>() }, cancellationToken);
        return result.GetProperty("sessionId").GetString()
            ?? throw new InvalidDataException("ACP session/new 返回了空 sessionId。");
    }

    public Task<JsonElement> PromptAsync(string sessionId, string prompt, CancellationToken cancellationToken = default) =>
        RequestAsync("session/prompt", new
        {
            sessionId,
            prompt = new[] { new { type = "text", text = prompt } },
        }, cancellationToken);

    /// <summary>列出持久化的历史会话（需运行时启用 JSONL 持久化）。</summary>
    public Task<JsonElement> ListSessionsAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("session/list", new { }, cancellationToken);

    /// <summary>加载一个历史会话：返回续接后的新 sessionId，并回放历史消息通知。</summary>
    public Task<JsonElement> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        RequestAsync("session/load", new { sessionId, cwd = "", mcpServers = Array.Empty<object>() }, cancellationToken);

    public Task CancelAsync(string sessionId, CancellationToken cancellationToken = default) =>
        NotifyAsync("session/cancel", new { sessionId }, cancellationToken);

    private async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("ACP request id collision.");
        try
        {
            await WriteFrameAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken);
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteFrameAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);

    private async Task WriteFrameAsync(object frame, CancellationToken cancellationToken)
    {
        var input = _input ?? throw new InvalidOperationException("ACP runtime is not running.");
        var line = JsonSerializer.Serialize(frame);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await input.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader output, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await output.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    DiagnosticReceived?.Invoke("Agent 运行时输出了无法解析的协议消息。");
                    continue;
                }
                using (document)
                {
                    await DispatchFrameAsync(document.RootElement.Clone(), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            DiagnosticReceived?.Invoke($"Agent 运行时连接中断：{error.Message}");
        }
        finally
        {
            var exit = _process is { HasExited: true } process ? $"，退出码 {process.ExitCode}" : string.Empty;
            FailPending(new IOException($"Agent 运行时连接已关闭{exit}。"));
        }
    }

    private async Task DispatchFrameAsync(JsonElement frame, CancellationToken cancellationToken)
    {
        if (frame.TryGetProperty("method", out var methodElement))
        {
            var method = methodElement.GetString() ?? string.Empty;
            if (frame.TryGetProperty("id", out var requestId))
            {
                await HandleInboundRequestAsync(requestId.Clone(), method, frame.GetProperty("params"), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                NotificationReceived?.Invoke(method, frame.TryGetProperty("params", out var parameters) ? parameters.Clone() : default);
            }
            return;
        }

        if (!frame.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id)) return;
        if (!_pending.TryGetValue(id, out var completion)) return;
        if (frame.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? error.GetRawText()
                : error.GetRawText();
            completion.TrySetException(new InvalidOperationException(message));
        }
        else
        {
            completion.TrySetResult(frame.TryGetProperty("result", out var result) ? result.Clone() : default);
        }
    }

    private async Task HandleInboundRequestAsync(JsonElement id, string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (method != "session/request_permission")
        {
            await WriteFrameAsync(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32601, message = $"Unsupported ACP client method: {method}" },
            }, cancellationToken);
            return;
        }

        var toolCallId = parameters.GetProperty("toolCall").GetProperty("toolCallId").GetString() ?? "unknown";
        var options = parameters.GetProperty("options").EnumerateArray()
            .Select(option => new PermissionOption(
                option.GetProperty("optionId").GetString() ?? string.Empty,
                option.GetProperty("name").GetString() ?? string.Empty,
                option.GetProperty("kind").GetString() ?? string.Empty))
            .ToArray();
        var selected = PermissionRequested is null
            ? null
            : await PermissionRequested(new PendingPermission(toolCallId, options)).ConfigureAwait(false);
        var outcome = selected is null
            ? (object)new { outcome = "cancelled" }
            : new { outcome = "selected", optionId = selected };
        await WriteFrameAsync(new { jsonrpc = "2.0", id, result = new { outcome } }, cancellationToken);
    }

    private void FailPending(Exception error)
    {
        foreach (var completion in _pending.Values) completion.TrySetException(error);
    }

    private static string ResolveRuntimeRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DSH_DESKTOP_RUNTIME_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "runtime")
            : Path.GetFullPath(configured);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        var process = _process;
        _process = null;
        try
        {
            _input?.Close();
            if (process is not null && !process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            if (_readTask is not null) await _readTask.ConfigureAwait(false);
        }
        finally
        {
            FailPending(new ObjectDisposedException(nameof(AcpClient)));
            _input?.Dispose();
            process?.Dispose();
            _writeLock.Dispose();
            _lifetime.Dispose();
        }
    }
}
