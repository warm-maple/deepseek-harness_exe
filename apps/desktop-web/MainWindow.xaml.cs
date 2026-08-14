using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Net.Sockets;
using System.Text;

namespace DeepSeekHarness.Web;

public partial class MainWindow : Window
{
    private Process? _node;
    private int _port;
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow()
    {
        InitializeComponent();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
        }
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _port = FindFreePort();
            _node = StartNode(_port);
            var ready = await WaitForServerAsync(_port, _lifetime.Token);
            if (!ready) throw new InvalidOperationException("内置 Web 服务未能启动。");
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Navigate($"http://127.0.0.1:{_port}");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"启动失败：{error.Message}", "DeepSeek Harness", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private Process StartNode(int port)
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
        start.ArgumentList.Add(port.ToString());
        start.Environment["DSH_HOME"] = home;
        start.Environment["DSH_TELEMETRY_DISABLED"] = "1";
        // 不继承系统的 DEEPSEEK_API_KEY：原版 harness 中环境变量优先且界面只读。
        // 移除后，API Key 从 Web 设置的「模型」页填写，并写入
        // %APPDATA%\DeepSeekHarness\.credentials.yaml（可写）。
        start.Environment.Remove("DEEPSEEK_API_KEY");

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("无法启动内置 Web 运行时。");
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) System.Diagnostics.Debug.WriteLine($"[runtime] {args.Data}");
        };
        process.BeginErrorReadLine();
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) System.Diagnostics.Debug.WriteLine($"[runtime] {args.Data}");
        };
        process.BeginOutputReadLine();
        return process;
    }

    private static async Task<bool> WaitForServerAsync(int port, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}", token);
                if (response.IsSuccessStatusCode) return true;
            }
            catch
            {
                // 服务尚未就绪，稍后重试。
            }
            await Task.Delay(500, token);
        }
        return false;
    }

    private bool _closing;

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        _lifetime.Cancel();
        IsEnabled = false;
        await KillNodeAsync();
        Close();
    }

    /// <summary>结束内置 node 的整棵进程树（web 服务及其子进程）。</summary>
    private async Task KillNodeAsync()
    {
        var node = _node;
        _node = null;
        if (node is null || node.HasExited) return;
        try
        {
            var psi = new ProcessStartInfo("taskkill")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("/PID");
            psi.ArgumentList.Add(node.Id.ToString());
            psi.ArgumentList.Add("/T");
            psi.ArgumentList.Add("/F");
            var task = Process.Start(psi);
            if (task is not null) await task.WaitForExitAsync();
        }
        catch
        {
            // 进程可能已经退出；尽力而为即可。
        }
    }
}
