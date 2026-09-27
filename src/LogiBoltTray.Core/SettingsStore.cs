using System.Text.Json;

namespace LogiBoltTray.Core;

public sealed class SettingsStore
{
    private readonly string _filePath;

    public SettingsStore(string baseDirectory)
    {
        _filePath = Path.Combine(baseDirectory, "config.json");
    }

    public SettingsModel Load()
    {
        if (!File.Exists(_filePath))
        {
            return SettingsModel.Default();
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SettingsModel>(json) ?? SettingsModel.Default();
        }
        catch (JsonException)
        {
            return SettingsModel.Default();
        }
    }

    public void Save(SettingsModel settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
