using BasePlugins;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TelegramClient.Factory.Factories;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Interfaces.Messages;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Base
{
    public abstract class BaseMessage : IMessageType
    {
        public Client Client { get; }
        public string PathFolderToSaveFiles { get; }
        protected FolderLayoutMode FolderLayout { get; }
        public abstract MessageTypes TypeMessage { get; }

        /// <summary>
        /// Called periodically during download: (chatName, fileName, pluginName, percent, bytesDownloaded, totalBytes)
        /// </summary>
        public Action<string, string, string, double, long, long>? OnProgress { get; set; }

        /// <summary>
        /// Called when download finishes: (chatName, fileName, success)
        /// </summary>
        public Action<string, string, bool>? OnComplete { get; set; }

        public BaseMessage(Client client, string pathFolderToSaveFiles, FolderLayoutMode folderLayout = FolderLayoutMode.TypeFirst)
        {
            Client = client;
            PathFolderToSaveFiles = pathFolderToSaveFiles;
            FolderLayout = folderLayout;
        }
        public abstract Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto);

        /// <summary>
        /// Creates a WTelegram ProgressCallback and a CancellationToken for this download.
        ///
        /// Two cancellation sources are combined into the returned token:
        ///   1. <b>User/watchdog cancel</b> — registered in CancellationRegistry so the UI
        ///      cancel button and the UI watchdog can both abort the transfer.
        ///   2. <b>Inactivity timeout</b> — a 3-minute self-resetting timer that fires when
        ///      WTelegram stops calling the progress callback (hung TCP connection, Telegram
        ///      server stall, etc.).  Every received byte resets the timer.  This is the
        ///      primary guard against stuck downloads and works even when DownloadFileAsync
        ///      never returns and never calls the callback again.
        ///
        /// The caller must still register <c>token.Register(() => stream.Dispose())</c> so
        /// that a hung DownloadFileAsync is force-interrupted when the token fires.
        ///
        /// The returned <c>userToken</c> is the raw user/UI cancellation token. Callers can
        /// use it to distinguish an explicit user cancel (delete .part file) from an inactivity
        /// timeout (keep .part file for resume on the next attempt).
        /// </summary>
        protected (Client.ProgressCallback callback, System.Threading.CancellationToken token, System.Threading.CancellationToken userToken)
            MakeProgress(string chatName, string fileName, long totalBytes)
        {
            var cancelKey = CancellationRegistry.MakeKey(chatName, fileName);
            var userToken = CancellationRegistry.Register(cancelKey);

            // Fires after 3 minutes of no progress; reset on every callback invocation
            var inactivityCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
            var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(userToken, inactivityCts.Token);
            var token = linkedCts.Token;

            // Clean up helper CTSes when the combined token fires (either source)
            token.Register(() =>
            {
                try { inactivityCts.Dispose(); } catch { }
                try { linkedCts.Dispose(); } catch { }
            });

            // Always create a callback so that:
            //   a) inactivity timer is reset on every received chunk, and
            //   b) ThrowIfCancellationRequested is polled during the transfer.
            Client.ProgressCallback callback = (transmitted, total) =>
            {
                token.ThrowIfCancellationRequested();
                // Reset the inactivity watchdog — download is still alive
                inactivityCts.CancelAfter(TimeSpan.FromMinutes(3));

                if (OnProgress == null) return;
                long effectiveTotal = total > 0 ? total : totalBytes;
                double pct = effectiveTotal > 0 ? transmitted * 100.0 / effectiveTotal : 0;
                OnProgress.Invoke(chatName, fileName, TypeMessage.ToString(), Math.Min(99, pct), transmitted, effectiveTotal);
            };

            return (callback, token, userToken);
        }

        /// <summary>Waits until the Telegram client is logged in and connected (max ~30 s).</summary>
        protected async Task WaitForConnectionAsync(CancellationToken ct = default)
        {
            for (var i = 0; i < 60; i++)
            {
                if (Client.UserId != 0 && !Client.Disconnected)
                    return;
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Downloads a document to a .part file with retry, fresh progress tokens per attempt,
        /// and protection against reconnect interrupting the transfer.
        /// </summary>
        protected async Task<ResultExecute> DownloadDocumentAsync(
            Document document, ChatDto chatDto, string fileName, string pluginName,
            string? overrideFinalPath = null,
            bool markDownloadedInIndex = true)
        {
            var finalPath = overrideFinalPath ?? PathLocationFolder(chatDto, fileName);
            var partPath = GetPartFilePath(finalPath);
            var cancelKey = CancellationRegistry.MakeKey(chatDto.Name, fileName);

            OnProgress?.Invoke(chatDto.Name, fileName, pluginName, 0, 0, document.size);
            DownloadActivity.Enter();
            var userCancelToken = CancellationToken.None;
            try
            {
                await WaitForConnectionAsync().ConfigureAwait(false);
                await WithRetryAsync(async () =>
                {
                    var (progress, downloadToken, userToken) = MakeProgress(chatDto.Name, fileName, document.size);
                    userCancelToken = userToken;
                    try
                    {
                        using var stream = OpenOrResumePartFile(partPath);
                        using var _ = downloadToken.Register(() => { try { stream.Dispose(); } catch { } });
                        await Client.DownloadFileAsync(document, stream, (TL.PhotoSizeBase?)null, progress)
                            .ConfigureAwait(false);
                        return true;
                    }
                    catch (Exception) when (downloadToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(downloadToken);
                    }
                }).ConfigureAwait(false);

                File.Move(partPath, finalPath, overwrite: true);
                if (markDownloadedInIndex)
                    FileDownloadIndex.MarkDownloaded(document.ID);
                OnComplete?.Invoke(chatDto.Name, fileName, true);
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = true,
                    FileName = fileName,
                    FilePath = finalPath,
                };
            }
            catch (OperationCanceledException)
            {
                if (userCancelToken.IsCancellationRequested)
                    DeletePartialFile(partPath);
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = false,
                    FileName = fileName,
                    ErrorMessage = "Cancelled by user",
                };
            }
            catch (Exception ex)
            {
                OnComplete?.Invoke(chatDto.Name, fileName, false);
                var message = userCancelToken.IsCancellationRequested
                    ? "Cancelled by user"
                    : $"Download interrupted (partial file kept for resume): {ex.Message}";
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = false,
                    FileName = fileName,
                    ErrorMessage = message,
                };
            }
            finally
            {
                DownloadActivity.Leave();
                CancellationRegistry.Remove(cancelKey);
            }
        }

        /// <summary>Silently deletes a partially downloaded file after a cancelled download.</summary>
        protected static void DeletePartialFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Returns the path of the in-progress partial file for a given final destination path.
        /// The .part file accumulates bytes during a download and is renamed to the final name
        /// on success. If the app is closed mid-download the .part file is kept so the next
        /// attempt can resume from where it left off.
        /// </summary>
        protected static string GetPartFilePath(string finalPath) => finalPath + ".part";

        /// <summary>
        /// Opens an existing .part file positioned at the end (for resume), or creates a new one.
        /// WTelegram's DownloadFileAsync reads stream.Position to determine the byte offset to
        /// start from, so positioning at the end instructs it to resume after existing data.
        /// </summary>
        protected static FileStream OpenOrResumePartFile(string partPath)
        {
            if (File.Exists(partPath))
            {
                var stream = new FileStream(partPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                stream.Seek(0, SeekOrigin.End);
                return stream;
            }
            return File.Create(partPath);
        }

        /// <summary>
        /// Executes a download action with automatic retry (exponential backoff).
        /// Does NOT retry on OperationCanceledException or when <paramref name="ct"/> is cancelled
        /// (covers IOException / ObjectDisposedException caused by forced stream closure).
        /// </summary>
        protected static async Task<T> WithRetryAsync<T>(
            Func<Task<T>> action,
            System.Threading.CancellationToken ct = default,
            int maxAttempts = 3)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch when (ct.IsCancellationRequested)
                {
                    // Stream was disposed by watchdog or user cancel — do not retry
                    throw;
                }
                catch when (++attempt < maxAttempts)
                {
                    int delayMs = attempt == 1 ? 2000 : 5000;
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }
        }

        public string PathLocationFolder(ChatDto chatDto, string fileName)
        {
            var folderName = chatDto.Name.TrimEnd();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, ' ');
                folderName = folderName.Replace(c, ' ');
            }

            folderName = folderName.Replace('~', ' ');
            fileName = fileName.Replace('~', ' ');

            var fullDir = FolderTemplateHelper.ResolveDownloadDirectory(
                PathFolderToSaveFiles ?? string.Empty,
                FolderLayout,
                chatDto.FolderTemplate,
                TypeMessage.ToString(),
                folderName);
            Directory.CreateDirectory(fullDir);
            return Path.Combine(fullDir, fileName);
        }

        public ResultExecute CheckDownloadPolicy(ChatDto chatDto, Message message)
        {
            if (message.media is MessageMediaDocument media && media.document is Document document)
            {
                var documentSizeInMb = document.size / 1024 / 1024;

                // Skip download if the file is smaller than the configured minimum threshold
                if (chatDto.DownloadFromSize != 0 && documentSizeInMb < chatDto.DownloadFromSize)
                {
                    return new ResultExecute(chatDto.Name)
                    {
                        FileName = document.Filename,
                        IsSuccess = false,
                        ErrorMessage = $"file limit to start download is: {chatDto.DownloadFromSize}MB, and the original file is: {documentSizeInMb}MB",
                    };
                }

                foreach (var regexPattern in chatDto.IgnoreFileByRegex)
                {
                    Regex regex = new(regexPattern);
                    if (regex.IsMatch(document.Filename))
                    {
                        return new ResultExecute(chatDto.Name)
                        {
                            FileName = document.Filename,
                            IsSuccess = false,
                            ErrorMessage = $"skip by regex pattern: '{regexPattern}' matched the document filename: {document.Filename}"
                        };
                    }
                }
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = true
                };
            }
            else
            {
                //ignore policy for plugins 
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = true
                };
            }
        }

        /// <summary>
        /// Scans the chat folder for a file with the same name and matching size.
        /// Passing <paramref name="expectedSize"/> = 0 skips the size check (photos, etc.).
        /// Returns the chat folder segment where the duplicate was found, or null.
        /// </summary>
        protected string? GetPathOfDuplicateFile(ChatDto chatDto, string fileName, long expectedSize = 0)
        {
            try
            {
                var chatDir = FolderTemplateHelper.ResolveDownloadDirectory(
                    PathFolderToSaveFiles ?? string.Empty,
                    FolderLayout,
                    chatDto.FolderTemplate,
                    TypeMessage.ToString(),
                    chatDto.Name);

                if (!Directory.Exists(chatDir))
                    return null;

                foreach (var file in Directory.EnumerateFiles(chatDir, fileName, SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                    if (expectedSize > 0)
                    {
                        var info = new FileInfo(file);
                        if (info.Length != expectedSize) continue;
                    }

                    var parent = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
                    return string.IsNullOrEmpty(parent) ? chatDto.Name : parent;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
