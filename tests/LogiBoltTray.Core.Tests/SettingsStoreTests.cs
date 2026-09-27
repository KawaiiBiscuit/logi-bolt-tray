using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LogiBoltTrayTests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_NoFileExists_ReturnsDefaults()
    {
        var store = new SettingsStore(_tempDir);

        SettingsModel settings = store.Load();

        Assert.Equal(60, settings.PollIntervalSeconds);
        Assert.Equal(IconStyle.A_Number, settings.IconStyle);
        Assert.Equal(20, settings.LowBatteryThresholdPercent);
        Assert.True(settings.LowBatteryNotificationsEnabled);
        Assert.False(settings.AutostartEnabled);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new SettingsStore(_tempDir);
        var original = new SettingsModel
        {
            PollIntervalSeconds = 120,
            IconStyle = IconStyle.C_BatteryBar,
            LowBatteryThresholdPercent = 15,
            LowBatteryNotificationsEnabled = false,
            AutostartEnabled = true,
        };
        original.DeviceVisibility["MX Master"] = false;

        store.Save(original);
        SettingsModel loaded = store.Load();

        Assert.Equal(120, loaded.PollIntervalSeconds);
        Assert.Equal(IconStyle.C_BatteryBar, loaded.IconStyle);
        Assert.Equal(15, loaded.LowBatteryThresholdPercent);
        Assert.False(loaded.LowBatteryNotificationsEnabled);
        Assert.True(loaded.AutostartEnabled);
        Assert.False(loaded.DeviceVisibility["MX Master"]);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{ not valid json");
        var store = new SettingsStore(_tempDir);

        SettingsModel settings = store.Load();

        Assert.Equal(60, settings.PollIntervalSeconds);
    }
}
