using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CatchLessons;

public sealed record AudioDevice(string Id, string Name) {public override string ToString()=>Name;}
public sealed record AudioFrame(byte[] Pcm, double Start, double End, float Peak);

public sealed class AudioCapture : IDisposable, IAudioCapture
{
    public static readonly WaveFormat Format = new(16000, 16, 1);
    private readonly MMDeviceEnumerator enumerator = new();
    private MMDevice? device;
    private WasapiLoopbackCapture? capture;
    private BufferedWaveProvider? buffer;
    private ISampleProvider? source;
    private Exception? failure;

    public static List<AudioDevice> Devices()
    {
        using var e = new MMDeviceEnumerator();
        var devices = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var result = new List<AudioDevice>();
        foreach (var d in devices)
        {
            using (d) result.Add(new AudioDevice(d.ID, d.FriendlyName));
        }
        return result;
    }
    public void Start(string? id)
    {
        device = string.IsNullOrEmpty(id) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id);
        capture = new WasapiLoopbackCapture(device);
        buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromSeconds(5), ReadFully = true, DiscardOnBufferOverflow = false };
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 2)
            samples = new StereoToMonoSampleProvider(samples) { LeftVolume = .5f, RightVolume = .5f };
        else if (samples.WaveFormat.Channels != 1)
            samples = new Downmix(samples);
        source = new WdlResamplingSampleProvider(samples, 16000);
        capture.DataAvailable += (_, e) =>
        {
            try { buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); }
            catch (Exception ex) { failure = ex; }
        };
        capture.RecordingStopped += (_, e) => { if (e.Exception != null) failure = e.Exception; };
        capture.StartRecording();
    }
    public Task StartAsync(string? id,CancellationToken token=default) {token.ThrowIfCancellationRequested();Start(id);return Task.CompletedTask;}
    public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
    public async Task PumpAsync(Func<AudioFrame, Task> consume, CancellationToken token)
    {
        var samples = new float[800];
        long total = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync(token))
        {
            if (failure != null) throw new IOException("오디오 장치 연결이 끊겼거나 캡처 버퍼가 넘쳤습니다.", failure);
            int count = source!.Read(samples, 0, samples.Length);
            var pcm = new byte[count * 2];
            float peak = 0;
            for (int i = 0; i < count; i++)
            {
                peak = Math.Max(peak, Math.Abs(samples[i]));
                var value = (short)(Math.Clamp(samples[i], -1, 1) * 32767);
                pcm[i * 2] = (byte)value;
                pcm[i * 2 + 1] = (byte)(value >> 8);
            }
            var start = total / 16000d;
            total += count;
            await consume(new(pcm, start, total / 16000d, peak));
        }
    }
    public void Dispose()
    {
        capture?.StopRecording();
        capture?.Dispose();
        device?.Dispose();
        enumerator.Dispose();
    }
    private sealed class Downmix(ISampleProvider input) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(input.WaveFormat.SampleRate, 1);
        public int Read(float[] output, int offset, int count)
        {
            int channels = input.WaveFormat.Channels;
            var data = new float[count * channels];
            int frames = input.Read(data, 0, data.Length) / channels;
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int j = 0; j < channels; j++) sum += data[i * channels + j];
                output[offset + i] = sum / channels;
            }
            return frames;
        }
    }
}
