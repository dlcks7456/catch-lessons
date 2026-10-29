using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;

namespace CatchLessons;

internal static class LocalTests
{
    public static async Task<string> PauseSmokeAsync()
    {
        await using var transcriber=new LocalTranscriber();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var captions=new List<Caption>();var first=new TaskCompletionSource<Caption>(TaskCreationOptions.RunContinuationsAsynchronously);
        transcriber.Completed+=c=>{captions.Add(c);first.TrySetResult(c);};await transcriber.InitializeAsync(new Settings{StreamingPreview=false},timeout.Token);
        using var reader=new WaveFileReader(Path.Combine(Environment.CurrentDirectory,"artifacts","jfk.wav"));long total=0;var buffer=new byte[1600];int count;
        while(total<4*32000&&(count=reader.Read(buffer,0,buffer.Length))>0){var pcm=buffer[..count];var peak=LocalTranscriber.Samples(pcm).Select(Math.Abs).DefaultIfEmpty().Max();await transcriber.PushAsync(new(pcm,total/32000d,(total+count)/32000d,peak),timeout.Token);total+=count;}
        await transcriber.FlushPendingAsync(timeout.Token);var completed=await first.Task.WaitAsync(TimeSpan.FromSeconds(25),timeout.Token);
        if(string.IsNullOrWhiteSpace(completed.Original))throw new Exception("Paused audio did not finish transcription");
        while((count=reader.Read(buffer,0,buffer.Length))>0){var pcm=buffer[..count];var peak=LocalTranscriber.Samples(pcm).Select(Math.Abs).DefaultIfEmpty().Max();await transcriber.PushAsync(new(pcm,total/32000d,(total+count)/32000d,peak),timeout.Token);total+=count;}
        await transcriber.DrainAsync(timeout.Token);if(captions.Count<2||captions[1].Start<captions[0].End)throw new Exception("Resume did not preserve continuous caption order");
        return JsonSerializer.Serialize(new{passed=true,firstCompletedWhilePaused=true,finalSegments=captions.Count,liveAudio=false});
    }
    public static async Task<string> NoiseSmokeAsync()
    {
        await using var transcriber=new LocalTranscriber();int preview=0,final=0;transcriber.Partial+=_=>Interlocked.Increment(ref preview);transcriber.Completed+=_=>Interlocked.Increment(ref final);
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));await transcriber.InitializeAsync(new Settings(),timeout.Token);
        var random=new Random(37);
        for(int frame=0;frame<240;frame++)
        {
            var pcm=new byte[1600];for(int i=0;i<pcm.Length;i+=2)System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i,2),(short)random.Next(-50,51));
            await transcriber.PushAsync(new(pcm,frame*.05,(frame+1)*.05,.0016f),timeout.Token);
        }
        await transcriber.DrainAsync(timeout.Token);if(preview!=0||final!=0)throw new Exception($"Noise generated captions: {preview} previews, {final} final");
        return JsonSerializer.Serialize(new{passed=true,syntheticNoiseSeconds=12,preview,final,cloudRequests=0});
    }
    public static async Task<string> RunAsync()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        Check(AudioCapture.Format.SampleRate == 16000 && AudioCapture.Format.Channels == 1, "Whisper audio format");
        var samples = LocalTranscriber.Samples([0, 128, 0, 0, 255, 127]);
        Check(samples.Length == 3 && samples[0] == -1 && samples[1] == 0 && samples[2] > .99, "PCM conversion");
        float[] quiet=[.002f,-.004f,0];LocalTranscriber.NormalizeQuiet(quiet);Check(quiet[0]>.07f&&quiet[1]<-.14f,"Quiet speech gets bounded amplification");
        float[] silence=[.0001f,0];LocalTranscriber.NormalizeQuiet(silence);Check(silence[0]==.0001f,"Silence is not amplified");
        float[] normal=[.2f,-.2f];LocalTranscriber.NormalizeQuiet(normal);Check(normal[0]==.2f,"Normal-level audio is unchanged");
        Check(LocalTranscriber.NativeLanguage("jv") == "jw", "Legacy language code");
        await using (var transcriber = new LocalTranscriber())
        {
            for (int i = 0; i < 120; i++) await transcriber.PushAsync(new(new byte[1600], i * .05, (i + 1) * .05, .1f), CancellationToken.None);
            Check(transcriber.PendingClips == 0, "Do not split speech at five seconds");
            Check(transcriber.PendingPreviews == 1, "Bound speculative snapshots to one");
            for (int i = 120; i < 260; i++) await transcriber.PushAsync(new(new byte[1600], i * .05, (i + 1) * .05, .1f), CancellationToken.None);
            Check(transcriber.PendingClips == 1, "Bound maximum clip duration");
            await transcriber.DrainAsync(CancellationToken.None);
            Check(transcriber.PendingClips == 2, "Flush final audio on stop");
        }
        var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "local-tests"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "integrity-test.bin"); byte[] bytes = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(path, bytes);
        var model = new LocalModel("test", "test", "test", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        Check(await LocalModels.ValidAsync(path, model, CancellationToken.None), "Model hash validation");
        await File.WriteAllBytesAsync(path, [4, 3, 2, 1]);
        Check(!await LocalModels.ValidAsync(path, model, CancellationToken.None), "Reject corrupt model");
        return JsonSerializer.Serialize(new { passed = checks, cloudRequests = 0, liveUiActions = 0 });
    }
    public static async Task<string> SmokeAsync(bool streaming = false,float volume=1)
    {
        using var logger = Whisper.net.Logger.LogProvider.AddLogger((level, message) =>
            File.AppendAllText("artifacts/local-native.log", $"{level}: {message}\n"));
        var fixture = Path.Combine(Environment.CurrentDirectory, "artifacts", "jfk.wav");
        if (!File.Exists(fixture)) throw new IOException("Download the public whisper.cpp samples/jfk.wav fixture first.");
        var captions = new List<Caption>(); var completionSeconds = new List<double>();
        var clock = new Stopwatch(); string[] detected = []; Exception? failure = null;
        var firstPreview = new TaskCompletionSource<TranscriptPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        int previewEvents = 0; bool previewBeforeFinal = false;
        await using var transcriber = new LocalTranscriber();
        transcriber.Status += text => File.AppendAllText("artifacts/local-smoke-progress.log", text + Environment.NewLine);
        transcriber.Completed += caption => { captions.Add(caption); completionSeconds.Add(clock.Elapsed.TotalSeconds); };
        transcriber.Partial += preview => { Interlocked.Increment(ref previewEvents); firstPreview.TrySetResult(preview); };
        transcriber.LanguagesDetected += codes => detected = codes;
        transcriber.Failed += ex => failure = ex;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var preparing = Stopwatch.StartNew();
        await transcriber.InitializeAsync(new Settings(), timeout.Token);
        preparing.Stop();
        using var reader = new WaveFileReader(fixture);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
            throw new IOException("Fixture must be 16 kHz mono PCM16.");
        clock.Start(); long bytes = 0; var buffer = new byte[1600]; int count;
        for (int round = 0; round < 2; round++)
        {
            reader.Position = 0;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var pcm = buffer[..count];
                if(volume!=1)for(int i=0;i<pcm.Length;i+=2){var sample=System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i,2));System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i,2),(short)(sample*volume));}
                float peak = LocalTranscriber.Samples(pcm).Select(Math.Abs).DefaultIfEmpty().Max();
                await transcriber.PushAsync(new(pcm, bytes / 32000d, (bytes + count) / 32000d, peak), timeout.Token); bytes += count;
                if (streaming && round == 0 && !previewBeforeFinal && bytes >= 3 * 32000)
                {
                    var draft = await firstPreview.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    if (captions.Count != 0 || transcriber.PendingClips != 0 || draft.End >= 3.1 || string.IsNullOrWhiteSpace(draft.Text))
                        throw new Exception("Expected an early preview before committing any final clip");
                    previewBeforeFinal = true;
                }
            }
            for (int i = 0; i < 30; i++)
            {
                await transcriber.PushAsync(new(new byte[1600], bytes / 32000d, (bytes + 1600) / 32000d, 0), timeout.Token);
                bytes += 1600;
            }
        }
        await transcriber.DrainAsync(timeout.Token); clock.Stop();
        if (failure != null) throw failure;
        if (streaming && (captions.Count != 2 || !previewBeforeFinal)) throw new Exception("Previews must not create duplicate final captions");
        var transcript = string.Join(" ", captions.Select(c => c.Original));
        if (!transcript.Contains("country", StringComparison.OrdinalIgnoreCase) || !detected.Contains("en")) throw new Exception("Local recognition/language detection did not match the English fixture: " + transcript);
        return JsonSerializer.Serialize(new { passed = true, transcript, detected, audioSeconds = reader.TotalTime.TotalSeconds * 2,
            preparingSeconds = preparing.Elapsed.TotalSeconds, processingSeconds = clock.Elapsed.TotalSeconds, completionSeconds,
            streamingTest = streaming, previewBeforeFinal, previewEvents, finalCaptions = captions.Count,
            transcriber.RuntimeInfo, openAiRequests = 0, codexRequests = 0 });
    }
}
