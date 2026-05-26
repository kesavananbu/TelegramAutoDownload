namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>Thrown when a new bootstrap would exceed the configured parallel-scan guard.</summary>
public sealed class BootstrapConflictException : InvalidOperationException
{
    public string BlockingChatName { get; }
    public long   BlockingChatId { get; }

    public BootstrapConflictException(string blockingChatName, long blockingChatId, string? message = null)
        : base(message ?? $"Bootstrap already running for \"{blockingChatName}\". " +
                         "Only one history scan runs at a time to protect your account — cancel it, wait, or override.")
    {
        BlockingChatName = blockingChatName;
        BlockingChatId   = blockingChatId;
    }
}
