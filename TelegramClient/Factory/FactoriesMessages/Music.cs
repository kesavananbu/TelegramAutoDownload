using BasePlugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.FactoriesMessages
{
    internal class Music : BaseMessage
    {
        public Music(Client client, string pathFolderToSaveFiles, FolderLayoutMode folderLayout = FolderLayoutMode.TypeFirst)
            : base(client, pathFolderToSaveFiles, folderLayout)
        {
        }

        public override MessageTypes TypeMessage => MessageTypes.Music;

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Music) return new ResultExecute(chatDto.Name);
            if (message.media is MessageMediaDocument mediaDocument)
            {
                var document = (Document)mediaDocument.document;

                // Voice messages are transient chat artifacts — skip them regardless of the Music toggle
                if (document.attributes?.Any(a => a is DocumentAttributeAudio audio &&
                        audio.flags.HasFlag(DocumentAttributeAudio.Flags.voice)) == true)
                    return new ResultExecute(chatDto.Name);

                var fileName = !string.IsNullOrEmpty(document.Filename) ? document.Filename : document.ID.ToString();
                // Primary dedup: Telegram document ID (unique content fingerprint)
                if (FileDownloadIndex.IsAlreadyDownloaded(document.ID))
                {
                    // Verify the file still exists on disk — guards against stale index after reinstall / moved files
                    var existingFile = GetPathOfDuplicateFile(chatDto, fileName, document.size);
                    if (existingFile != null)
                        return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} already downloaded (id match)" };
                    // Stale index entry — file gone from disk, remove and re-download
                    FileDownloadIndex.Remove(document.ID);
                }

                // Secondary dedup: filename + file size match on disk
                var fileExist = GetPathOfDuplicateFile(chatDto, fileName, document.size);
                if (fileExist != null)
                {
                    FileDownloadIndex.MarkDownloaded(document.ID);
                    return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} is exist on {fileExist}" };
                }
                return await DownloadDocumentAsync(document, chatDto, fileName, TypeMessage.ToString());
            }
            return new ResultExecute(chatDto.Name);
        }
    }
}
