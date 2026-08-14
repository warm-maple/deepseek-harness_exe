using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DeepSeekHarness.Desktop;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ApiKeyBox.Password = settings.ApiKey;
        WorkspaceBox.Text = settings.Workspace;
        SelectByContent(ModelBox, settings.Model);
        SelectByTag(PresetBox, settings.Preset);
        SelectByTag(PermissionBox, settings.PermissionMode);
        UpdateNav();
    }

    public AppSettings Result => _settings;

    private void NavGeneral_Click(object sender, RoutedEventArgs args)
    {
        GeneralPanel.Visibility = Visibility.Visible;
        ModelsPanel.Visibility = Visibility.Collapsed;
        UpdateNav();
    }

    private void NavModels_Click(object sender, RoutedEventArgs args)
    {
        GeneralPanel.Visibility = Visibility.Collapsed;
        ModelsPanel.Visibility = Visibility.Visible;
        UpdateNav();
    }

    private void UpdateNav()
    {
        var generalActive = GeneralPanel.Visibility == Visibility.Visible;
        NavGeneralBtn.Background = generalActive
            ? (System.Windows.Media.Brush)FindResource("SelectedBrush")
            : System.Windows.Media.Brushes.Transparent;
        NavModelsBtn.Background = !generalActive
            ? (System.Windows.Media.Brush)FindResource("SelectedBrush")
            : System.Windows.Media.Brushes.Transparent;
    }

    private void BrowseWorkspace_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Agent 工作区",
            InitialDirectory = _settings.Workspace,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _settings.Workspace = dialog.FolderName;
            WorkspaceBox.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs args)
    {
        _settings.ApiKey = ApiKeyBox.Password.Trim();
        _settings.Workspace = WorkspaceBox.Text.Trim();
        _settings.Model = (ModelBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "deepseek-v4-pro";
        _settings.Preset = (PresetBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard";
        var mode = (PermissionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "workspace-write";
        // 照抄 web：选择 Full access 需显式风险确认
        if (mode == "danger-full-access" && mode != _settings.PermissionMode)
        {
            var confirm = MessageBox.Show(this,
                "启用 Full access 后，Agent 将减少确认步骤，并且可以直接执行更多操作，包括敏感操作、文件修改或外部命令。仅建议在你信任后续任务时使用。\n\n是否继续？",
                "确认启用 Full access？",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }
        _settings.PermissionMode = mode;
        DialogResult = true;
    }

    private static void SelectByContent(ComboBox box, string value)
    {
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content?.ToString() == value) ?? box.Items[0];
    }

    private static void SelectByTag(ComboBox box, string value)
    {
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == value) ?? box.Items[0];
    }
}
