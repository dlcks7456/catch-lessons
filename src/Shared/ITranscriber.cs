namespace CatchLessons;

public interface ITranscriber : IAsyncDisposable
{
    event Action<Caption>? Completed;
    event Action<TranscriptPreview>? Partial;
    event Action<string[]>? LanguagesDetected;
    event Action<string>? Status;
    event Action<Exception>? Failed;
    string RuntimeInfo { get; }
    Task InitializeAsync(Settings settings, CancellationToken token);
    Task PushAsync(AudioFrame frame, CancellationToken token);
    Task FlushPendingAsync(CancellationToken token);
    Task DrainAsync(CancellationToken token);
}
