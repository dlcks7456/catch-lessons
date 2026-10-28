using System.Globalization;

namespace CatchLessons;

public enum CaptionDirection { LeftToRight, RightToLeft }

public sealed record LanguageOption(string Code, string Name)
{
    public string Label => Code == "auto" ? UiText.LanguageLabel(Code,Name) : $"{UiText.LanguageLabel(Code,Name)} ({Code})";
    public override string ToString()=>Label;
}

public static class Languages
{
    public static CaptionDirection Direction(string text)
    {
        foreach (char c in text)
            if (char.IsLetter(c))
                return c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF'
                    ? CaptionDirection.RightToLeft : CaptionDirection.LeftToRight;
        return CaptionDirection.LeftToRight;
    }
    public static readonly LanguageOption Auto = new("auto", "언어 자동 감지");
    private static readonly Dictionary<string, string> Names = new()
    {
        ["ko"] = "한국어", ["en"] = "영어", ["ja"] = "일본어", ["zh"] = "중국어",
        ["es"] = "스페인어", ["fr"] = "프랑스어", ["de"] = "독일어", ["ru"] = "러시아어",
        ["yue"] = "광둥어", ["haw"] = "하와이어", ["ht"] = "아이티 크레올어"
    };
    public static readonly LanguageOption[] All = (
        "ko en ja zh es fr de ru pt ar hi vi th id it tr pl ca nl sv fi he uk el ms cs ro da hu ta no ur hr bg lt la mi ml cy sk te fa lv bn sr az sl kn et mk br eu is hy ne mn bs kk sq sw gl mr pa si km sn yo so af oc ka be tg sd gu am yi lo uz fo ht ps tk nn mt sa lb my bo tl mg as tt haw ln ha ba jv su yue")
        .Split(' ').Select(code => new LanguageOption(code, DisplayName(code))).ToArray();
    public static IEnumerable<LanguageOption> Sources => new[] { Auto }.Concat(All);
    private static string DisplayName(string code)
    {
        if (Names.TryGetValue(code, out var name)) return name;
        try { return CultureInfo.GetCultureInfo(code).DisplayName; }
        catch (CultureNotFoundException) { return code; }
    }
    public static LanguageOption Source(string? code) => code == "auto" || code == null ? Auto : All.FirstOrDefault(l => l.Code == code) ?? Auto;
    public static LanguageOption Target(string? code) => All.FirstOrDefault(l => l.Code == code) ?? All[0];
    public static string ModelName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).EnglishName; }
        catch (CultureNotFoundException) { return DisplayName(code); }
    }
}
