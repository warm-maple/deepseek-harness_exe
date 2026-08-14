using System.Windows;
using System.Windows.Controls;

namespace DeepSeekHarness.Desktop;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ApiKeyBox.Password = settings.ApiKey;
        SelectByContent(ModelBox, settings.Model);
        SelectByTag(PresetBox, settings.Preset);
        SelectByTag(PermissionBox, settings.PermissionMode);
    }

    public AppSettings Result => _settings;

    private void Save_Click(object sender, RoutedEventArgs args)
    {
        _settings.ApiKey = ApiKeyBox.Password.Trim();
        _settings.Model = (ModelBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "deepseek-v4-pro";
        _settings.Preset = (PresetBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard";
        _settings.PermissionMode = (PermissionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "workspace-write";
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
