using Newtonsoft.Json;
using System.IO;
using TelegramClient;

namespace TelegramAutoDownload.Models
{
    public class ConfigFile
    {
        private readonly string _fileName;

        /// <param name="filePath">
        /// Custom path for the config file. Defaults to <see cref="AppPaths.ConfigFile"/>.
        /// Pass a temp path in tests to avoid touching the real config on disk.
        /// </param>
        public ConfigFile(string? filePath = null)
        {
            _fileName = filePath ?? AppPaths.ConfigFile;
            if (!File.Exists(_fileName))
            {
                var defaults = new ConfigParams();
                defaults.ApplyDownloadLimits();
                ConfigJsonFileHelper.WriteAtomic(_fileName,
                    JsonConvert.SerializeObject(defaults, Formatting.Indented));
            }
        }

        public void Save(ConfigParams config)
        {
            ConfigJsonFileHelper.WriteAtomic(_fileName,
                JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        public ConfigParams Read()
        {
            var data = ConfigJsonFileHelper.ReadJsonOrBackupCorrupt(_fileName, out _);
            if (data == null)
            {
                var defaults = new ConfigParams();
                defaults.NormalizeYtDlpQualityForAllChats();
                defaults.ApplyDownloadLimits();
                return defaults;
            }

            var result = JsonConvert.DeserializeObject<ConfigParams>(data) ?? new ConfigParams();
            result.NormalizeYtDlpQualityForAllChats();
            result.ApplyDownloadLimits();
            return result;
        }
    }
}
