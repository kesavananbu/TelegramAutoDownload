using BasePlugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Service
{
    public class MessageTextFactoryService : BaseMessage
    {
        private readonly List<Type> _pluginTypes = [];
        public override MessageTypes TypeMessage => MessageTypes.Message;

        public MessageTextFactoryService(Client client, string pathFolderToSaveFiles)
            : base(client, pathFolderToSaveFiles)
        {
            // Use the absolute path next to the executable so the app works both
            // as a portable install (writable folder) and as a Program Files install
            // (read-only folder — Plugins were placed there by the installer).
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
            {
                // Only try to create the folder; silently skip if the install dir is read-only
                // (Program Files) — the installer is responsible for creating it there.
                try { Directory.CreateDirectory(pluginsDir); } catch { /* read-only install, skip */ }
            }

            var folders = Directory.Exists(pluginsDir)
                ? Directory.GetDirectories(pluginsDir)
                : Array.Empty<string>();

            // Same plugin DLL may exist under multiple subfolders (e.g. duplicate installs).
            // LoadFrom the same assembly simple name twice throws "Assembly with same name is already loaded".
            var loadedAssemblySimpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var pluginFiles = Directory.GetFiles(folder, "*.dll");

                foreach (var pluginFile in pluginFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string simpleName;
                    try
                    {
                        simpleName = AssemblyName.GetAssemblyName(pluginFile).Name ?? string.Empty;
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(simpleName) || !loadedAssemblySimpleNames.Add(simpleName))
                        continue;

                    try
                    {
                        Assembly pluginAssembly = Assembly.LoadFrom(pluginFile);
                        try
                        {
                            _pluginTypes.AddRange(pluginAssembly.GetTypes()
                                .Where(t => !t.IsAbstract && t.IsClass &&
                                            t.BaseType != null && t.BaseType.IsGenericType &&
                                            t.BaseType.GetGenericTypeDefinition() == typeof(BasePlugin<>)));
                        }
                        catch (ReflectionTypeLoadException)
                        {
                            // Assembly is loaded; do not remove simpleName — a second LoadFrom would fail.
                        }
                    }
                    catch (FileLoadException)
                    {
                        loadedAssemblySimpleNames.Remove(simpleName);
                    }
                    catch (BadImageFormatException)
                    {
                        loadedAssemblySimpleNames.Remove(simpleName);
                    }
                }
            }
        }

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            ResultExecute resultExecute = new(chatDto.Name);
            if (_pluginTypes.Count == 0)
            {
                resultExecute.ErrorMessage =
                    "No URL plugin assemblies are loaded (Plugins folder missing, empty, or DLLs failed to load).";
                return resultExecute;
            }

            var pluginRan = false;
            var split = (message.message ?? string.Empty).Split('\n');
            foreach (var rawLine in split)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var magnet = MagnetLinkHelper.TryExtract(line);
                if (magnet != null)
                    line = magnet;

                // Sort by priority on each line to ensure deterministic ordering
                var orderedPlugins = _pluginTypes
                    .Select(t => t.MakeGenericType(typeof(Message)))
                    .Select(t => Activator.CreateInstance(t) as BasePlugin<Message>)
                    .Where(p => p != null)
                    .OrderBy(p => p!.Priority)
                    .ToList();

                foreach (var pluginInstance in orderedPlugins)
                {
                    var config = new BasePlugins.Config
                    {
                        Text = line,
                        PathSaveFile = PathFolderToSaveFiles,
                        ChatName = chatDto.Name,
                        EnabledPlugins = chatDto.EnabledPlugins,
                        YtdlpQuality = chatDto.YtdlpQuality,
                        SocialDownloadFolderTemplate = string.IsNullOrWhiteSpace(chatDto.SocialDownloadFolderTemplate)
                            ? null
                            : chatDto.SocialDownloadFolderTemplate,
                        YoutubeDownloadFolderTemplate = string.IsNullOrWhiteSpace(chatDto.YoutubeDownloadFolderTemplate)
                            ? null
                            : chatDto.YoutubeDownloadFolderTemplate,
                        OtherDownloadFolderTemplate = string.IsNullOrWhiteSpace(chatDto.OtherDownloadFolderTemplate)
                            ? null
                            : chatDto.OtherDownloadFolderTemplate,
                        TorrentDownloadFolderTemplate = string.IsNullOrWhiteSpace(chatDto.TorrentDownloadFolderTemplate)
                            ? null
                            : chatDto.TorrentDownloadFolderTemplate,
                    };

                    if (!pluginInstance!.CanHandle(config)) continue;

                    // Missing key = disabled. User must explicitly enable each plugin per chat.
                    if (!config.EnabledPlugins.TryGetValue(pluginInstance.PluginName, out var enabled) || !enabled)
                        continue;

                    // Wire progress callbacks so the plugin can report to the UI
                    pluginInstance.OnProgress = OnProgress;
                    pluginInstance.OnComplete = OnComplete;

                    pluginRan = true;
                    resultExecute = await pluginInstance.ExecuteAsync(config);
                    if (resultExecute.IsSuccess) break;
                }

                if (resultExecute.IsSuccess) break;
            }

            if (!resultExecute.IsSuccess && string.IsNullOrWhiteSpace(resultExecute.ErrorMessage))
                resultExecute.ErrorMessage = ExplainPluginOutcome(message, pluginRan);

            return resultExecute;
        }

        private static string ExplainPluginOutcome(Message message, bool pluginRan)
        {
            var blob = message.message ?? string.Empty;
            if (!pluginRan)
            {
                if (!blob.Contains("http", StringComparison.OrdinalIgnoreCase))
                    return "No http/https URL in this message for URL plugins to download.";
                return "URL(s) found but no enabled plugin handled them (enable YouTube / Social media / Other / Torrent per chat, or URL pattern not supported).";
            }

            return "A plugin ran but returned failure with no error message.";
        }
    }
}
