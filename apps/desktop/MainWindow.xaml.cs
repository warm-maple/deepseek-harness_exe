using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
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
        Loaded += (_, _) => (HasMessages ? PromptBox : HeroBox)?.Focus();
    }

    private bool HasMessages => _viewModel.HasMessages;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.SelectedConversation))
        {
            ObserveMessages();
            Dispatcher.BeginInvoke(ScrollToEndSafe);
            (HasMessages ? PromptBox : HeroBox)?.Focus();
        }
        else if (args.PropertyName == nameof(MainViewModel.HasMessages))
        {
            (HasMessages ? PromptBox : HeroBox)?.Focus();
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
