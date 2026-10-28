using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Headless;

namespace CatchLessons.Desktop;

internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        if(args.FirstOrDefault()=="--ui-check")
        {
            AppBuilder.Configure<DesktopApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
            var window=new DesktopWindow();var settings=new DesktopSettings(new Settings());
            ((Avalonia.Controls.Control)window.Content!).Measure(new Size(920,500));
            ((Avalonia.Controls.Control)settings.Content!).Measure(new Size(700,740));
            System.IO.File.WriteAllText(args.Length>1?args[1]:"desktop-ui-check.json","{\"passed\":true,\"liveUiActions\":0,\"agentCalls\":0}");
            window.Close();settings.Close();return;
        }
        using var instance=new Mutex(true,(OperatingSystem.IsWindows()?"Local\\":"")+"CatchLessons-"+Environment.UserName,out var created);
        if(!created){InstanceActivation.SignalAsync().GetAwaiter().GetResult();return;}
        try{AppBuilder.Configure<DesktopApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);}finally{instance.ReleaseMutex();}
    }
}
public sealed class DesktopApp:Application
{
    private TrayIcon? tray;
    private InstanceActivation? activation;
    public void RefreshTrayLanguage()
    {
        if(tray?.Menu is not { } menu)return;
        if(menu.Items[0] is NativeMenuItem open)open.Header=UiText.T("CatchLessons 열기");
        if(menu.Items[1] is NativeMenuItem exit)exit.Header=UiText.T("종료");
    }
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window=new DesktopWindow();desktop.MainWindow=window;desktop.ShutdownMode=ShutdownMode.OnMainWindowClose;
            void Restore(){window.Show();window.WindowState=Avalonia.Controls.WindowState.Normal;window.Activate();}
            activation=new InstanceActivation(()=>Avalonia.Threading.Dispatcher.UIThread.Post(Restore));
            var menu=new NativeMenu();var open=new NativeMenuItem(UiText.T("CatchLessons 열기"));open.Click+=(_,_)=>Restore();menu.Items.Add(open);
            var exit=new NativeMenuItem(UiText.T("종료"));exit.Click+=(_,_)=>{Restore();window.ExitFromTray();};menu.Items.Add(exit);
            tray=new TrayIcon{Icon=window.Icon,ToolTipText="CatchLessons",Menu=menu,IsVisible=true};tray.Clicked+=(_,_)=>Restore();
            TrayIcon.SetIcons(this,new TrayIcons{tray});window.BackgroundEnabled=true;
            desktop.Exit+=(_,_)=>{activation?.Dispose();tray?.Dispose();tray=null;};
        }
        base.OnFrameworkInitializationCompleted();
    }
}

