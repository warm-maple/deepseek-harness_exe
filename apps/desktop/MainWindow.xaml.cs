using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace DeepSeekHarness.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _closing;
    private INotifyCollectionChanged? _observedMessages;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(Dispatcher);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.ScrollRequested += () => Dispatcher.BeginInvoke(ScrollToEndSafe);
        ObserveMessages();
        Closing += MainWindow_Closing;
        Loaded += (_, _) => FocusComposer();
    }

    private bool HasMessages => _viewModel.HasMessages;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.SelectedConversation))
        {
            ObserveMessages();
            Dispatcher.BeginInvoke(ScrollToEndSafe);
            FocusComposer();
        }
        else if (args.PropertyName == nameof(MainViewModel.HasMessages))
        {
            FocusComposer();
        }
    }

    private void ObserveMessages()
    {
        if (_observedMessages is not null) _observedMessages.CollectionChanged -= Messages_CollectionChanged;
        _observedMessages = _viewModel.SelectedConversation?.Messages;
        if (_observedMessages is not null) _observedMessages.CollectionChanged += Messages_CollectionChanged;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        Dispatcher.BeginInvoke(ScrollToEndSafe);

    /// <summary>聚焦当前输入框（hero 或底部），等待布局完成后执行以确保元素可见。</summary>
    private void FocusComposer()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var target = HasMessages ? PromptBox : HeroBox;
            if (target is null) return;
            target.Focus();
            Keyboard.Focus(target);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollToEndSafe()
    {
        if (ConversationScroller is not null
            && ConversationScroller.VerticalOffset + ConversationScroller.ViewportHeight >= ConversationScroller.ExtentHeight - 60)
        {
            ConversationScroller.ScrollToEnd();
        }
    }

    private void NewConversation_Click(object sender, RoutedEventArgs args) => _viewModel.NewConversation();

    private async void ChooseWorkspace_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Agent 工作区",
            InitialDirectory = _viewModel.Settings.Workspace,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunUiActionAsync(() => _viewModel.ChangeWorkspaceAsync(dialog.FolderName));
    }

    private async void Settings_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new SettingsWindow(_viewModel.Settings.Copy()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RunUiActionAsync(() => _viewModel.ApplySettingsAsync(dialog.Result));
    }

    private async void Send_Click(object sender, RoutedEventArgs args) => await RunUiActionAsync(_viewModel.SendAsync);
    private async void Stop_Click(object sender, RoutedEventArgs args) => await RunUiActionAsync(_viewModel.StopAsync);

    private async void PromptBox_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        args.Handled = true;
        await RunUiActionAsync(_viewModel.SendAsync);
    }

    private async void HeroBox_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        await RunUiActionAsync(_viewModel.SendAsync);
    }

    private void PermissionMode_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button button && button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private async void PermissionMenuItem_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem item || item.Tag is not string mode) return;
        if (mode == _viewModel.Settings.PermissionMode) return;
        // 照抄 web 的 Full access 风险确认
        if (mode == "danger-full-access")
        {
            var confirm = MessageBox.Show(this,
                "启用 Full access 后，Agent 将减少确认步骤，并且可以直接执行更多操作，包括敏感操作、文件修改或外部命令。仅建议在你信任后续任务时使用。\n\n是否继续？",
                "确认启用 Full access？",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }
        var settings = _viewModel.Settings.Copy();
        settings.PermissionMode = mode;
        await RunUiActionAsync(() => _viewModel.ApplySettingsAsync(settings));
    }

    private void AllowPermission_Click(object sender, RoutedEventArgs args)
    {
        var option = _viewModel.PendingPermission?.Options.FirstOrDefault(item => item.Kind == "allow_once")
            ?? _viewModel.PendingPermission?.Options.FirstOrDefault();
        _viewModel.ResolvePermission(option?.Id);
    }

    private void RejectPermission_Click(object sender, RoutedEventArgs args)
    {
        var option = _viewModel.PendingPermission?.Options.FirstOrDefault(item => item.Kind == "reject_once");
        _viewModel.ResolvePermission(option?.Id);
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "DeepSeek Harness", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs args)
    {
        if (_closing) return;
        args.Cancel = true;
        _closing = true;
        IsEnabled = false;
        await _viewModel.DisposeAsync();
        Close();
    }
}
