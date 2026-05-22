using BasePlugins;
using System.IO;
using System.Threading.Tasks;

namespace TorrentPlugin
{
    public class TorrentPlugin<TMessage> : BasePlugin<TMessage>
    {
        public override string PluginName => "Torrent";
        public override int Priority => 3;

        public override bool CanHandle(Config config)
        {
            if (MagnetLinkHelper.ContainsMagnetLink(config.Text))
                return true;

            return !string.IsNullOrWhiteSpace(config.LocalTorrentPath) &&
                   File.Exists(config.LocalTorrentPath);
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            var outputFolder = PluginFolderPathHelper.CombineUnderDownloadRoot(
                config.PathSaveFile,
                config.TorrentDownloadFolderTemplate,
                PluginName,
                config.ChatName,
                "{Platform}/{ChatName}");
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            var magnetUri = MagnetLinkHelper.TryExtract(config.Text);

            return await TorrentDownloadService.DownloadAsync(
                config.ChatName,
                outputFolder,
                magnetUri,
                config.LocalTorrentPath,
                PluginName,
                OnProgress,
                OnComplete,
                config.CancellationToken).ConfigureAwait(false);
        }
    }
}
