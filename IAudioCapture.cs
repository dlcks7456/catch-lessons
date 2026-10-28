namespace CatchLessons;

public interface IAudioCapture : IAsyncDisposable
{
    Task StartAsync(string? deviceId,CancellationToken token=default);
    Task PumpAsync(Func<AudioFrame,Task> consume,CancellationToken token);
}

public static class AudioPlatform
{
    public static IAudioCapture Create() => OperatingSystem.IsMacOS() ? new MacAudioCapture() : new AudioCapture();
    public static List<AudioDevice> Devices() => OperatingSystem.IsMacOS() ? [new("", "Mac 시스템 오디오")] : AudioCapture.Devices();
}
