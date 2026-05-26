namespace TelegramAutoDownload.Headless.Scanning;

public sealed record TestDownloadStepLog(string Phase, bool Ok, string Detail);

public sealed record TestDownloadItemResult(
    int MessageId,
    string Kind,
    string? FileName,
    long SizeBytes,
    string Outcome,
    IReadOnlyList<TestDownloadStepLog> Steps,
    string? Error);

public sealed record TestDownloadReport(
    long ChatId,
    string ChatName,
    int RequestedSamples,
    int SamplesFound,
    int Succeeded,
    int Skipped,
    int Failed,
    bool ReadyForBootstrap,
    string Summary,
    IReadOnlyList<TestDownloadStepLog> SetupLogs,
    IReadOnlyList<TestDownloadItemResult> Items);
