using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekHarness.Desktop;

public sealed class AppSettings
{
    public string Workspace { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Model { get; set; } = "deepseek-v4-pro";
    public string PermissionMode { get; set; } = "workspace-write";
    public string Preset { get; set; } = "standard";

    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;

    public AppSettings Copy() => new()
    {
        Workspace = Workspace,
        Model = Model,
        PermissionMode = PermissionMode,
        Preset = Preset,
        ApiKey = ApiKey,
    };
}

public static class AppSettingsStore
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepSeekHarness");
    private static readonly string SettingsPath = Path.Combine(Root, "settings.json");

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            settings = new AppSettings();
        }
        settings.ApiKey = CredentialStore.ReadApiKey();
        if (!Directory.Exists(settings.Workspace)) settings.Workspace = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Root);
        CredentialStore.WriteApiKey(settings.ApiKey);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json + Environment.NewLine);
    }

    public static string HarnessHome => Path.Combine(Root, "harness-home");
    public static string SessionsRoot => Path.Combine(Root, "sessions");
}
