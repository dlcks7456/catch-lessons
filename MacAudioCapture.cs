using System.Diagnostics;
using NAudio.Wave;

namespace CatchLessons;

public sealed class MacAudioCapture : IAudioCapture
{
    private Process? helper;
    private readonly BufferedWaveProvider buffer=new(AudioCapture.Format){ReadFully=true,BufferDuration=TimeSpan.FromSeconds(5)};
    private readonly CancellationTokenSource lifetime=new();
    private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? reader,errors;
    private Exception? failure;
    public async Task StartAsync(string? deviceId,CancellationToken token=default)
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        var path=Path.Combine(AppContext.BaseDirectory,"native","caption-audio");
        if(!File.Exists(path))throw new IOException("Mac 오디오 구성 요소가 없습니다. build-macos.sh로 앱을 빌드하세요.");
        helper=Process.Start(new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true})??throw new IOException("Mac 오디오 실행 실패");
        reader=Task.Run(async()=>
        {
            var bytes=new byte[3200];
            try {int count;while((count=await helper.StandardOutput.BaseStream.ReadAsync(bytes,lifetime.Token))>0)buffer.AddSamples(bytes,0,count);if(!lifetime.IsCancellationRequested)throw new IOException("Mac 오디오 연결 종료");}
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
            catch(Exception ex){failure=ex;ready.TrySetException(ex);}
        });
        errors=Task.Run(async()=>
        {
            try {string? line;while((line=await helper.StandardError.ReadLineAsync(lifetime.Token))!=null){if(line=="CAPTION_READY")ready.TrySetResult();else if(line.StartsWith("ERROR:")){failure=new IOException(line);ready.TrySetException(failure);}}}
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(45),token);
    }
    public async Task PumpAsync(Func<AudioFrame,Task> consume,CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(50));long total=0;
        while(await timer.WaitForNextTickAsync(token))
        {
            if(failure!=null)throw failure;
            var bytes=new byte[1600];buffer.Read(bytes,0,bytes.Length);
            float peak=LocalTranscriber.Samples(bytes).Select(Math.Abs).Max();
            await consume(new(bytes,total/16000d,(total+800)/16000d,peak));total+=800;
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if(helper!=null)
        {
            try {helper.StandardInput.Close();await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch{try{if(!helper.HasExited)helper.Kill(true);}catch{}}
        }
        if(reader!=null)await reader;if(errors!=null)await errors;helper?.Dispose();lifetime.Dispose();
    }
}
