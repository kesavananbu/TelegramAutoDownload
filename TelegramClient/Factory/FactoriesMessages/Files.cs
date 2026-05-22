using BasePlugins;
using System.IO;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Factories
{

    public class Files : BaseMessage
    {
        public override MessageTypes TypeMessage => MessageTypes.Files;

        public Files(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Files) return new ResultExecute(chatDto.Name);

            if (message.media is MessageMediaDocument mediaDocument)
            {
                var document = (Document)mediaDocument.document;
                var fileName = !string.IsNullOrEmpty(document.Filename) ? document.Filename : document.ID.ToString();

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

                return await DownloadDocumentAsync(document, chatDto, fileName, TypeMessage.ToString());
            }
            return new ResultExecute(chatDto.Name);
        }
    }
}
