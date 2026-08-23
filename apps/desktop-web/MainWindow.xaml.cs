using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekHarness.Web;

public partial class MainWindow : Window
{
    private const string ServerAnnouncementPrefix = "dsh web: ";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private Process? _node;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RuntimeOutputTail _runtimeOutput = new(20);
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        using (var iconStream = File.OpenRead(iconPath))
        {
            Icon = BitmapFrame.Create(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var runtime = StartNode();
            _node = runtime.Process;
            var serverUri = await WaitForServerAsync(runtime, _lifetime.Token);
            var webViewData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarness",
                "WebView2");
            Directory.CreateDirectory(webViewData);
            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewData);
            await Browser.EnsureCoreWebView2Async(webViewEnvironment);
            if (_lifetime.IsCancellationRequested) return;
            Browser.CoreWebView2.Navigate(serverUri.AbsoluteUri);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Window shutdown owns this cancellation and does not surface a startup error.
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"启动失败：{error.Message}", "DeepSeek Harness", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private RuntimeStart StartNode()
    {
        var runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
        var node = Path.Combine(runtime, "node.exe");
        var bin = Path.Combine(runtime, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        foreach (var required in new[] { node, bin })
        {
            if (!File.Exists(required)) throw new FileNotFoundException($"内置 Web 运行时缺失：{required}", required);
        }

        var home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeepSeekHarness");
        Directory.CreateDirectory(home);

        var start = new ProcessStartInfo
        {
            FileName = node,
            WorkingDirectory = runtime,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(bin);
        start.ArgumentList.Add("web");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add("0");
        // The WebView2 window is this app's only surface; a system browser tab would duplicate it.
        start.ArgumentList.Add("--no-open");
        start.Environment["DSH_HOME"] = home;
        start.Environment["DSH_TELEMETRY_DISABLED"] = "1";
        // The web Models page owns a writable credential; an inherited value would make it read-only.
        start.Environment.Remove("DEEPSEEK_API_KEY");

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var announcedUri = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            _runtimeOutput.Add("stderr", args.Data);
            Debug.WriteLine($"[runtime] {args.Data}");
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            _runtimeOutput.Add("stdout", args.Data);
            Debug.WriteLine($"[runtime] {args.Data}");
            if (TryParseAnnouncedUri(args.Data, out var uri)) announcedUri.TrySetResult(uri);
        };
        if (!process.Start()) throw new InvalidOperationException("无法启动内置 Web 运行时。");
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return new RuntimeStart(process, announcedUri.Task);
    }

    internal static bool TryParseAnnouncedUri(string line, out Uri uri)
    {
        uri = null!;
        if (!line.StartsWith(ServerAnnouncementPrefix, StringComparison.Ordinal)) return false;
        var remainder = line[ServerAnnouncementPrefix.Length..].Trim();
        var separator = remainder.IndexOf(' ');
        var candidateText = separator < 0 ? remainder : remainder[..separator];
        const string loopbackPrefix = "http://127.0.0.1:";
        if (!candidateText.StartsWith(loopbackPrefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(
                candidateText.AsSpan(loopbackPrefix.Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var port) || port is < 1 or > 65_535) return false;
        if (!Uri.TryCreate(candidateText, UriKind.Absolute, out var candidate)) return false;
        if (candidate.Port != port) return false;
        uri = candidate;
        return true;
    }

    private async Task<Uri> WaitForServerAsync(RuntimeStart runtime, CancellationToken token)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(StartupTimeout);
        try
        {
            var exitTask = runtime.Process.WaitForExitAsync(startup.Token);
            var announcementTask = runtime.AnnouncedUri.WaitAsync(startup.Token);
            var first = await Task.WhenAny(announcementTask, exitTask);
            if (first == exitTask)
            {
                await exitTask;
                runtime.Process.WaitForExit();
                throw RuntimeExited(runtime.Process);
            }

            var uri = await announcementTask;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            while (true)
            {
                startup.Token.ThrowIfCancellationRequested();
                if (runtime.Process.HasExited)
                {
                    runtime.Process.WaitForExit();
                    throw RuntimeExited(runtime.Process);
                }
                try
                {
                    using var response = await client.GetAsync(uri, startup.Token);
                    if (response.IsSuccessStatusCode) return uri;
                }
                catch (HttpRequestException)
                {
                    // The listener can announce its port before the frontend fallback is ready.
                }
                catch (TaskCanceledException) when (!startup.IsCancellationRequested)
                {
                    // One HTTP probe timed out while the overall startup deadline remains active.
                }
                await Task.Delay(ProbeInterval, startup.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(RuntimeFailure("内置 Web 服务在 90 秒内未能启动。"));
        }
    }

    private InvalidOperationException RuntimeExited(Process process) =>
        new(RuntimeFailure($"内置 Web 运行时提前退出（退出码 {process.ExitCode}）。"));

    private string RuntimeFailure(string message)
    {
        var output = _runtimeOutput.Snapshot();
        return output.Length == 0 ? message : $"{message}\n\n最近的运行时日志：\n{output}";
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        _lifetime.Cancel();
        IsEnabled = false;
        await KillNodeAsync();
        Close();
    }

    /// <summary>Stops the bundled Node process tree and waits for its root process to exit.</summary>
    private async Task KillNodeAsync()
    {
        var node = Interlocked.Exchange(ref _node, null);
        if (node is null) return;
        try
        {
            if (!node.HasExited) node.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
        catch (Win32Exception error)
        {
            Debug.WriteLine($"[runtime] Process.Kill failed; falling back to taskkill: {error.Message}");
            var start = new ProcessStartInfo("taskkill")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("/PID");
            start.ArgumentList.Add(node.Id.ToString());
            start.ArgumentList.Add("/T");
            start.ArgumentList.Add("/F");
            using var taskkill = Process.Start(start);
            if (taskkill is not null) await taskkill.WaitForExitAsync();
        }
        try
        {
            await node.WaitForExitAsync().WaitAsync(ShutdownTimeout);
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("[runtime] Node process did not exit within the shutdown deadline.");
        }
        finally
        {
            node.Dispose();
        }
    }

    private readonly record struct RuntimeStart(Process Process, Task<Uri> AnnouncedUri);

    private sealed class RuntimeOutputTail
    {
        private const int MaximumLineLength = 1_000;
        private readonly int _capacity;
        private readonly Queue<string> _lines = new();
        private readonly object _lock = new();

        public RuntimeOutputTail(int capacity)
        {
            _capacity = capacity;
        }

        public void Add(string stream, string line)
        {
            var text = line.Length <= MaximumLineLength ? line : $"{line[..MaximumLineLength]}…";
            lock (_lock)
            {
                _lines.Enqueue($"[{stream}] {text}");
                while (_lines.Count > _capacity) _lines.Dequeue();
            }
        }

        public string Snapshot()
        {
            lock (_lock)
            {
                return string.Join(Environment.NewLine, _lines);
            }
        }
    }
}
