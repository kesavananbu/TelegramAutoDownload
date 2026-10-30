using Newtonsoft.Json;
using TelegramAutoDownload.Models;
using TelegramClient;

namespace TelegramAutoDownload.Headless;

/// <summary>
/// Reads/writes the headless config file. Mirrors ConfigFile from the WPF project
/// but bound to <see cref="HeadlessPaths.ConfigFile"/> and survives container restarts.
/// </summary>
public sealed class ConfigStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public ConfigStore(string? path = null)
    {
        _path = path ?? HeadlessPaths.ConfigFile;
        if (!File.Exists(_path))
        {
            var seeded = new ConfigParams { PathSaveFile = HeadlessPaths.DownloadsDir };
            seeded.ApplyDownloadLimits();
            ConfigJsonFileHelper.WriteAtomic(_path, JsonConvert.SerializeObject(seeded, Formatting.Indented));
        }
    }

    public ConfigParams Read()
    {
        lock (_lock)
        {
            var json = ConfigJsonFileHelper.ReadJsonOrBackupCorrupt(_path, out _);
            ConfigParams cfg;
            if (json == null)
                cfg = new ConfigParams { PathSaveFile = HeadlessPaths.DownloadsDir };
            else
                cfg = JsonConvert.DeserializeObject<ConfigParams>(json) ?? new ConfigParams();

            cfg.NormalizeYtDlpQualityForAllChats();
            if (string.IsNullOrWhiteSpace(cfg.PathSaveFile))
                cfg.PathSaveFile = HeadlessPaths.DownloadsDir;
            cfg.ApplyDownloadLimits();
            return cfg;
        }
    }

    public void Save(ConfigParams cfg)
    {
        lock (_lock)
        {
            ConfigJsonFileHelper.WriteAtomic(_path,
                JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }
    }
}
