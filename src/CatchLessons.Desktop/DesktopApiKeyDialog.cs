using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CatchLessons.Desktop;

internal sealed class DesktopApiKeyDialog : Window
{
    private readonly TextBox key;
    private readonly TextBlock status;
    private readonly Button check;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? connectionCheck;
    public string? SavedKey { get; private set; }

    public DesktopApiKeyDialog(string initialKey,bool keyReadError=false)
    {
        Title=UiText.T("OpenAI API Key");Width=460;MinWidth=460;MaxWidth=460;MinHeight=300;MaxHeight=460;SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;CanResize=false;
        var root=new StackPanel{Margin=new Thickness(24),Spacing=6};Content=root;
        Ui.Label(root,UiText.T("공통 OpenAI API Key"));
        root.Children.Add(new TextBlock{Text=UiText.T("음성 인식·번역·요약에서 같은 키를 사용합니다. API 사용 요금은 별도입니다."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        key=Ui.Input(initialKey);key.PasswordChar='●';root.Children.Add(key);
        status=new TextBlock{Text=keyReadError?UiText.T("저장된 API Key를 읽지 못했습니다. 새 키를 입력하고 저장하세요."):"",TextWrapping=TextWrapping.Wrap,MinHeight=32,MaxHeight=72,TextTrimming=TextTrimming.CharacterEllipsis};root.Children.Add(status);
        check=Ui.Button(UiText.T("OpenAI API 연결 확인"),Check);check.HorizontalAlignment=HorizontalAlignment.Left;root.Children.Add(check);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var cancel=Ui.Button(UiText.T("취소"),()=>Close(false));cancel.IsCancel=true;actions.Children.Add(cancel);
        var save=Ui.Button(UiText.T("저장"),()=>{if(string.IsNullOrWhiteSpace(key.Text)){status.Text=UiText.T("API Key를 입력하세요.");return;}SavedKey=key.Text.Trim();Close(true);});save.IsDefault=true;save.Classes.Add("accent");actions.Children.Add(save);
        root.Children.Add(actions);
        key.TextChanged+=(_,_)=>{connectionCheck?.Cancel();status.Text="";};
        Opened+=(_,_)=>{key.Focus();key.CaretIndex=key.Text?.Length??0;};
        Closed+=(_,_)=>{connectionCheck?.Cancel();lifetime.Cancel();lifetime.Dispose();};
    }

    private async void Check()
    {
        if(string.IsNullOrWhiteSpace(key.Text)){status.Text=UiText.T("API Key를 입력하세요.");return;}
        connectionCheck?.Cancel();using var pending=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);connectionCheck=pending;
        var candidate=key.Text;
        check.IsEnabled=false;status.Text=UiText.T("OpenAI API 연결 확인 중…");
        try
        {
            using var api=new OpenAiTranslator(new Settings(),candidate,null);
            await api.CheckConnectionAsync(pending.Token);
            if(!lifetime.IsCancellationRequested&&!pending.IsCancellationRequested&&key.Text==candidate)status.Text="OpenAI API · "+UiText.T("연결됨");
        }
        catch(OperationCanceledException) when(pending.IsCancellationRequested){}
        catch(Exception ex){if(!lifetime.IsCancellationRequested&&!pending.IsCancellationRequested&&key.Text==candidate)status.Text=UiText.Status(ex.Message);}
        finally{if(connectionCheck==pending){connectionCheck=null;check.IsEnabled=!lifetime.IsCancellationRequested;}}
    }
}
