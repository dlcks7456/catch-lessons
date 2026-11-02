using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CatchLessons;

internal sealed class ApiKeyWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? check;
    internal PasswordBox KeyInput { get; } = new() { Name="ApiKeyInput", Height=36 };
    internal Button SaveButton { get; }
    public string Key => KeyInput.Password.Trim();

    public ApiKeyWindow(Settings settings, string key, bool keyLoaded = true)
    {
        Title=UiText.T("OpenAI API Key"); Width=460; SizeToContent=SizeToContent.Height;
        MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-60);
        WindowStartupLocation=WindowStartupLocation.CenterOwner; ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        Style=(Style)Application.Current.FindResource(typeof(Window));
        var frame=new Border{CornerRadius=new CornerRadius(20),BorderThickness=new Thickness(1),Padding=new Thickness(26)};
        frame.SetResourceReference(Border.BackgroundProperty,"Surface");frame.SetResourceReference(Border.BorderBrushProperty,"Line");Content=frame;
        var panel=new StackPanel();frame.Child=panel;
        var header=new DockPanel{Margin=new Thickness(0,0,0,12)};panel.Children.Add(header);
        var close=new Button{Content=UiText.T("취소"),Tag=FindResource("Icon-x"),Style=(Style)FindResource("IconButton")};
        System.Windows.Automation.AutomationProperties.SetName(close,UiText.T("취소"));
        DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);close.Click+=(_,_)=>{DialogResult=false;};
        header.Children.Add(new TextBlock{Text=Title,FontSize=22,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        header.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&e.OriginalSource is TextBlock)DragMove();};
        var help=new TextBlock{Text=UiText.T("음성 인식·번역·요약에서 같은 키를 사용합니다. API 사용 요금은 별도입니다."),TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,0,0,16)};
        help.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(help);
        KeyInput.Password=key;panel.Children.Add(KeyInput);
        System.Windows.Automation.AutomationProperties.SetName(KeyInput,Title);
        var status=new TextBlock{Name="ApiKeyStatus",TextWrapping=TextWrapping.Wrap,MaxHeight=72,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,10,0,8)};
        status.SetBinding(ToolTipProperty,new System.Windows.Data.Binding("Text"){Source=status});
        if(!keyLoaded)status.Text=UiText.T("저장된 API Key를 읽지 못했습니다. 새 키를 입력하고 저장하세요.");
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(status);
        var test=new Button{Name="ApiKeyCheck",Content=UiText.T("OpenAI API 연결 확인"),HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=Key.Length>0};panel.Children.Add(test);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};panel.Children.Add(actions);
        var cancel=new Button{Content=UiText.T("취소"),MinWidth=80,IsCancel=true,Margin=new Thickness(0,0,8,0)};actions.Children.Add(cancel);
        SaveButton=new Button{Name="ApiKeySave",Content=UiText.T("저장"),MinWidth=80,IsDefault=true,IsEnabled=Key.Length>0,Style=(Style)FindResource("PrimaryButton")};actions.Children.Add(SaveButton);
        SaveButton.Click+=(_,_)=>{if(Key.Length>0)DialogResult=true;};
        KeyInput.PasswordChanged+=(_,_)=>{check?.Cancel();status.Text="";SaveButton.IsEnabled=Key.Length>0;test.IsEnabled=check==null&&Key.Length>0;};
        test.Click+=async(_,_)=>
        {
            check?.Cancel();using var pending=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);check=pending;
            var candidate=Key;test.IsEnabled=false;status.Text=UiText.T("연결 확인 중…");
            try
            {
                using var api=new OpenAiTranslator(settings,candidate,null);await api.CheckConnectionAsync(pending.Token);
                if(!lifetime.IsCancellationRequested&&!pending.IsCancellationRequested&&Key==candidate)status.Text="OpenAI API · "+UiText.T("연결됨");
            }
            catch(OperationCanceledException) when(pending.IsCancellationRequested) { }
            catch(Exception ex){if(!lifetime.IsCancellationRequested&&!pending.IsCancellationRequested&&Key==candidate)status.Text=UiText.Status(ex.Message);}
            finally{if(check==pending){check=null;test.IsEnabled=Key.Length>0;}}
        };
        Loaded+=(_,_)=>{KeyInput.Focus();};
        Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();};
    }
}
