using BasePlugins;
using System.Linq;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Factories
{

    public class Videos : BaseMessage
    {
        public override MessageTypes TypeMessage => MessageTypes.Videos;
        private string PluginName => "Videos";

        public Videos(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Videos) return new ResultExecute(chatDto.Name);
            if (message.media is not MessageMediaDocument mediaVideo) return new ResultExecute(chatDto.Name);

            var document = (Document)mediaVideo.document;
            var mime_type = "mp4";
            string fileName;

            if (!string.IsNullOrEmpty(document.Filename))
            {
                mime_type = document.Filename.Split('.').LastOrDefault();
                fileName = document.Filename;
            }
            else
            {
                fileName = $"{document.ID}.{mime_type}";
            }

            if (FileDownloadIndex.IsAlreadyDownloaded(document.ID))
            {
                var existingFile = GetPathOfDuplicateFile(fileName, document.size);
                if (existingFile != null)
                    return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} already downloaded (id match)" };
                FileDownloadIndex.Remove(document.ID);
            }

            var fileExist = GetPathOfDuplicateFile(fileName, document.size);
            if (fileExist != null)
            {
                FileDownloadIndex.MarkDownloaded(document.ID);
                return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} is exist on {fileExist}" };
            }

            return await DownloadDocumentAsync(document, chatDto, fileName, PluginName);
        }

    }

}
