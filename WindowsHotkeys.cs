using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace CatchLessons;

internal sealed class WindowsHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int,string> actions=[];
    private readonly Action<string> invoke;
    internal IntPtr Handle=>source.Handle;
    public WindowsHotkeys(Action<string> invoke)
    {
        this.invoke=invoke;
        source=new HwndSource(new HwndSourceParameters("CatchLessons hotkeys"){ParentWindow=new IntPtr(-3)});
        source.AddHook(Message);
    }
    internal static uint NativeModifiers(int flags)=>(uint)(((flags&1)!=0?2:0)|((flags&2)!=0?1:0)|((flags&4)!=0?4:0)|((flags&8)!=0?8:0)|0x4000);
    public string? Bind(Dictionary<string,string> shortcuts)
    {
        Clear();var errors=new List<string>();int id=1;
        foreach(var action in ShortcutKeys.Actions)
        {
            if(!shortcuts.TryGetValue(action.Id,out var value)||string.IsNullOrWhiteSpace(value))continue;
            try
            {
                var parsed=ShortcutKeys.Parse(value,false);
                if((parsed.Modifiers&11)==0)throw new ArgumentException("Ctrl, Alt 또는 Win 조합이 필요합니다.");
                var name=parsed.Key switch{"COMMA"=>"OemComma","PERIOD"=>"OemPeriod",_=>parsed.Key.Length==1&&char.IsDigit(parsed.Key[0])?"D"+parsed.Key:parsed.Key};
                var key=Enum.Parse<Key>(name,true);int vk=KeyInterop.VirtualKeyFromKey(key);
                if(!RegisterHotKey(source.Handle,id,NativeModifiers(parsed.Modifiers),(uint)vk))throw new InvalidOperationException("다른 앱에서 사용 중이거나 예약된 키입니다.");
                actions[id++]=action.Id;
            }
            catch(Exception ex){errors.Add($"{UiText.T(action.Name)} ({ShortcutKeys.Display(value,false)}): {UiText.T(ex.Message)}");}
        }
        return errors.Count==0?null:string.Join("\n",errors);
    }
    public static string? Check(Dictionary<string,string> shortcuts){using var probe=new WindowsHotkeys(_=>{});return probe.Bind(shortcuts);}
    public void Clear(){foreach(var id in actions.Keys)UnregisterHotKey(source.Handle,id);actions.Clear();}
    private IntPtr Message(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg==0x312&&actions.TryGetValue(wParam.ToInt32(),out var action)){handled=true;source.Dispatcher.BeginInvoke(()=>invoke(action));}
        return IntPtr.Zero;
    }
    public void Dispose(){Clear();source.RemoveHook(Message);source.Dispose();}
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd,int id);
}
