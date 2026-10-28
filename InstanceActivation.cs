using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace CatchLessons;

internal sealed class InstanceActivation : IDisposable
{
    private static readonly string DefaultName="CatchLessons-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
    private readonly CancellationTokenSource stop=new();
    private readonly Task listener;
    public InstanceActivation(Action restore,string? name=null)=>listener=Listen(restore,name??DefaultName,stop.Token);
    private static async Task Listen(Action restore,string name,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                var buffer=new byte[1];if(await pipe.ReadAsync(buffer,token)==1&&buffer[0]==1)restore();
            }
            catch(OperationCanceledException){return;}
            catch(IOException){if(!token.IsCancellationRequested)await Task.Delay(100,token);}
        }
    }
    public static async Task SignalAsync(string? name=null)
    {
        try{using var pipe=new NamedPipeClientStream(".",name??DefaultName,PipeDirection.Out,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));await pipe.ConnectAsync(timeout.Token);if(OperatingSystem.IsWindows()&&GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var pid))AllowSetForegroundWindow(pid);await pipe.WriteAsync(new byte[]{1},timeout.Token);await pipe.FlushAsync(timeout.Token);}
        catch(IOException){}catch(OperationCanceledException){}catch(UnauthorizedAccessException){}
    }
    public void Dispose(){stop.Cancel();_ = listener.ContinueWith(_=>stop.Dispose(),TaskScheduler.Default);}
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint pid);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
}
