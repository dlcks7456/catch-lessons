namespace CatchLessons;

internal sealed class SettingsSaveGate
{
    public bool HasTextEdits {get;private set;}
    public bool TextChanged(bool focused)
    {
        if(!focused)return true;
        HasTextEdits=true;return false;
    }
    public static bool CanRunTimer(bool textFocused,bool dropdownOpen)=>!textFocused&&!dropdownOpen;
    public void Saved()=>HasTextEdits=false;
}
