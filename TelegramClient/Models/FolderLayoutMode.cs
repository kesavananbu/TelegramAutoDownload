namespace TelegramClient.Models
{
    /// <summary>
    /// Default on-disk layout when a chat has no custom <see cref="ChatDto.FolderTemplate"/>.
    /// </summary>
    public enum FolderLayoutMode
    {
        /// <summary>{Type}/{ChatName}/ — e.g. Videos/My Channel/</summary>
        TypeFirst = 0,

        /// <summary>{ChatName}/{Type}/ — e.g. My Channel/Videos/</summary>
        ChatFirst = 1,

        /// <summary>{ChatName}/ — all media types in one folder per chat.</summary>
        ChatCombined = 2,
    }
}
