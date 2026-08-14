using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace DeepSeekHarness.Desktop;

/// <summary>
/// 只读 Markdown 视图：把绑定的 Markdown 文本渲染为 FlowDocument。
/// 流式接收时按 70ms 节流重渲染，避免每个 token 都重建布局。
/// </summary>
public sealed class MarkdownView : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownView),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    private DispatcherTimer? _timer;
    private bool _dirty;
    private string _rendered = string.Empty;

    public MarkdownView()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IsToolBarVisible = false;
        Document = new FlowDocument { PagePadding = new Thickness(0) };
        AddHandler(Hyperlink.RequestNavigateEvent, new RoutedEventHandler(OnRequestNavigate));
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MarkdownView)d).ScheduleRender();

    private void ScheduleRender()
    {
        _dirty = true;
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(70),
            };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                if (_dirty)
                {
                    _dirty = false;
                    RenderNow();
                }
            };
        }
        _timer.Start();
    }

    /// <summary>立即渲染（消息完成时调用）。</summary>
    public void RenderNow()
    {
        _dirty = false;
        var text = Markdown ?? string.Empty;
        if (text == _rendered) return;
        _rendered = text;
        Document = MarkdownRenderer.Render(text);
    }

    private void OnRequestNavigate(object sender, RoutedEventArgs args)
    {
        if (args is not System.Windows.Navigation.RequestNavigateEventArgs nav || nav.Uri is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(nav.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打不开链接时静默忽略。
        }
        args.Handled = true;
    }
}
