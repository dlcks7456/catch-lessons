namespace CatchLessons;

public sealed record ShortcutAction(string Id,string Name);
public static class ShortcutKeys
{
    public static string Capture(int modifiers,string key,bool mac)
    {
        key=NormalizeKey(key);
        if(key is "BACK" or "BACKSPACE" or "DELETE" && modifiers==0)return "";
        var parts=new List<string>();
        if((modifiers&(mac?8:1))!=0)parts.Add("Primary");
        if((modifiers&(mac?1:8))!=0)parts.Add(mac?"Ctrl":"Win");
        if((modifiers&2)!=0)parts.Add("Alt");if((modifiers&4)!=0)parts.Add("Shift");parts.Add(key);
        var value=string.Join('+',parts);Parse(value,mac);return value;
    }
    public static string Display(string value,bool mac)=>value.Replace("Primary",mac?"⌘":"Ctrl").Replace("Command","⌘").Replace("Cmd","⌘").Replace("Meta",mac?"⌘":"Win");
    public static string NormalizeKey(string name)
    {
        var key=name.ToUpperInvariant();if(key.Length==2&&key[0]=='D'&&char.IsDigit(key[1]))return key[1..];
        return key switch{"OEMCOMMA"=>"COMMA","OEMPERIOD"=>"PERIOD","RETURN"=>"ENTER",_=>key};
    }
    public static readonly ShortcutAction[] Actions = [new("startStop","시작 / 중지"),new("pauseResume","일시정지 / 재개"),new("settings","설정 열기"),new("mode","번역 / 녹취 모드 전환"),new("summary","요약 켜기 / 끄기"),new("latest","최신 자막으로"),new("folder","저장 폴더 열기"),new("minimize","최소화")];
    public static (int Modifiers,string Key) Parse(string value,bool mac)
    {
        var parts=value.Split('+',StringSplitOptions.TrimEntries);int modifiers=0;
        if(parts.Length==0||parts[^1].Length==0)throw new ArgumentException("단축키 형식을 확인하세요.");
        foreach(var part in parts[..^1])modifiers|=part.ToLowerInvariant() switch {"primary"=>mac?8:1,"ctrl" or "control"=>1,"alt" or "option"=>2,"shift"=>4,"cmd" or "command" or "meta" or "win"=>8,_=>throw new ArgumentException("알 수 없는 단축키 보조키: "+part)};
        var key=parts[^1].ToUpperInvariant();
        if(!(key.Length==1&&char.IsAsciiLetterOrDigit(key[0]))&&!System.Text.RegularExpressions.Regex.IsMatch(key,@"^F([1-9]|1[0-9]|2[0-4])$")&&!new[]{"SPACE","ENTER","ESCAPE","TAB","COMMA","PERIOD","UP","DOWN","LEFT","RIGHT","HOME","END","PAGEUP","PAGEDOWN"}.Contains(key))throw new ArgumentException("지원하지 않는 단축키: "+key);
        return(modifiers,key);
    }
    public static void Validate(Dictionary<string,string> keys)
    {
        foreach(bool mac in new[]{false,true})
        {
            var used=new HashSet<(int,string)>();
            foreach(var action in Actions)if(keys.TryGetValue(action.Id,out var key)&&!string.IsNullOrWhiteSpace(key)&&!used.Add(Parse(key,mac)))throw new ArgumentException("중복된 단축키가 있습니다: "+key);
        }
    }
}
