using System.IO;
using System.Windows;

namespace CatchLessons;

public partial class App : Application
{
    private Mutex? instance;
    private System.Windows.Forms.NotifyIcon? tray;
    private InstanceActivation? activation;
    public void RefreshTrayLanguage()
    {
        if(tray?.ContextMenuStrip is not { } menu)return;
        menu.Items[0].Text=UiText.T("CatchLessons 열기");menu.Items[1].Text=UiText.T("종료");
        tray.Text=UiText.T("CatchLessons · 닫으면 백그라운드에서 계속 실행");
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var result = await Diagnostics.RunAsync(e.Args[0]);
                var path = e.Args.Length > 1 ? e.Args[1] : "diagnostics.json";
                File.WriteAllText(path, result);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(e.Args.Length > 1 ? e.Args[1] : "diagnostics.json", ex.ToString());
                Shutdown(1);
            }
            return;
        }
        instance = new Mutex(true, @"Local\CatchLessons-" + Environment.UserName, out var created);
        if (!created)
        {
            await InstanceActivation.SignalAsync();
            instance.Dispose();
            instance = null;
            Shutdown();
            return;
        }
        ShutdownMode=ShutdownMode.OnMainWindowClose;
        var window=new MainWindow();MainWindow=window;
        var menu=new System.Windows.Forms.ContextMenuStrip();
        void Restore(){window.Show();window.WindowState=WindowState.Normal;window.Activate();}
        activation=new InstanceActivation(()=>Dispatcher.BeginInvoke(Restore));
        menu.Items.Add(UiText.T("CatchLessons 열기"),null,(_,_)=>Dispatcher.Invoke(Restore));
        menu.Items.Add(UiText.T("종료"),null,(_,_)=>Dispatcher.Invoke(()=>{Restore();window.ExitFromTray();}));
        using(var stream=GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream)
            tray=new System.Windows.Forms.NotifyIcon{Icon=new System.Drawing.Icon(stream),Text=UiText.T("CatchLessons · 닫으면 백그라운드에서 계속 실행"),ContextMenuStrip=menu,Visible=true};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Restore);
        window.BackgroundEnabled=true;window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activation?.Dispose();
        if(tray!=null){tray.Visible=false;tray.Icon?.Dispose();tray.ContextMenuStrip?.Dispose();tray.Dispose();}
        instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}

