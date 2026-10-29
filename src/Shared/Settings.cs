using System.IO;
using System.Text.Json;

namespace CatchLessons;

public sealed class Settings
{
    // Keep the previous app-data location so existing settings and protected keys remain available.
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptionGlass");
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CatchLessons");
    public bool OutputFolderMigrated { get; set; }
    public string CodexPath { get; set; } = "";
    public string AgentProvider { get; set; } = "chatgpt";
    public string UiLanguage {get;set;}="en";
    public string CodexModel {get;set;}="gpt-6-luna";
    public string CodexEffort {get;set;}="low";
    public bool CodexFastMode {get;set;}
    public string ClaudeModel {get;set;}="haiku";
    public string ClaudeEffort {get;set;}="low";
    [System.Text.Json.Serialization.JsonIgnore] public string AgentModel=>AgentProvider=="claude"?ClaudeModel:CodexModel;
    [System.Text.Json.Serialization.JsonIgnore] public string AgentEffort=>AgentProvider=="claude"?ClaudeEffort:CodexEffort;
    [System.Text.Json.Serialization.JsonIgnore] public string AgentKey=>string.Join('|',AgentProvider,AgentProvider=="claude"?ClaudePath:CodexPath,AgentModel,AgentEffort,AgentProvider=="chatgpt"&&CodexFastMode);
    public string ClaudePath { get; set; } = "";
    public string CustomInstruction { get; set; } = "";
    public string PostStopPrompt { get; set; } = "";
    public Dictionary<string,string> Shortcuts { get; set; } = new()
    {
        ["startStop"]="Primary+Alt+R",["pauseResume"]="Primary+Alt+P",["settings"]="Primary+Alt+S",
        ["mode"]="Primary+Alt+M",["summary"]="Primary+Alt+Y",["latest"]="Primary+Alt+L",
        ["folder"]="Primary+Alt+O",["minimize"]="Primary+Alt+N"
    };
    public string SpeechEngine { get; set; } = "whisper";
    public string GetKey()
    {
        if(OperatingSystem.IsMacOS())return MacKeychain.Read();
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        return string.IsNullOrEmpty(ProtectedKey)?"":System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey),null,System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }
    public void SetKey(string key)
    {
        if(OperatingSystem.IsMacOS()){MacKeychain.Write(key);return;}
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        ProtectedKey=string.IsNullOrWhiteSpace(key)?"":Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(key.Trim()),null,System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }
    public string? DeviceId { get; set; }
    public bool RecordAudio { get; set; }
    public string CaptionPace { get; set; } = "balanced";
    public string Topic { get; set; } = "";
    public string Terms { get; set; } = "";
    public string SummaryCategory { get; set; } = "lecture";
    public bool SummaryEnabled { get; set; } = true;
    public bool OpenFolderAfterSummary { get; set; } = true;
    public List<SummaryCategory> SummaryCategories { get; set; } = SummaryNotes.Categories.Select(c => c with { }).ToList();
    [System.Text.Json.Serialization.JsonIgnore]
    public SummaryCategory SelectedSummary => SummaryCategories.FirstOrDefault(c => c.Id == SummaryCategory) ?? SummaryNotes.None;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShouldSummarize => SummaryEnabled && SelectedSummary.Id != "none";
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "ko";
    public string Theme { get; set; } = "system";
    private double captionFontSize=24,windowOpacity=1;
    public double CaptionFontSize {get=>captionFontSize;set=>captionFontSize=double.IsFinite(value)?Math.Clamp(value,18,36):24;}
    public double WindowOpacity {get=>windowOpacity;set=>windowOpacity=double.IsFinite(value)?Math.Clamp(value,.45,1):1;}
    public string LocalModel { get; set; } = "balanced";
    public bool LocalCpuOnly { get; set; }
    public bool StreamingPreview { get; set; } = true;
    public bool TranscriptionOnly { get; set; }
    public string SummaryLanguage { get; set; } = "ko";
    public string SummaryFormat { get; set; } = "md";
    public (double Silence, double Maximum) Segmentation => CaptionPace switch
    {
        "fast" => (.8, 8),
        "context" => (1.6, 20),
        _ => (1.2, 12)
    };
    public string[] Keywords => Terms.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
    // Windows stores only an encrypted value; macOS uses its Keychain.
    public string ProtectedKey { get; set; } = "";
    public void Save()
    {
        if(UiLanguage is not ("en" or "ko"))throw new InvalidOperationException("Invalid app language.");
        foreach(var pair in new[]{(CodexModel,CodexEffort),(ClaudeModel,ClaudeEffort)})
            if(string.IsNullOrWhiteSpace(pair.Item1)||!new[]{"minimal","none","low","medium","high","xhigh","max","ultra"}.Contains(pair.Item2))throw new InvalidOperationException("모델과 effort를 선택하세요.");
        if (CustomInstruction.Length > 8000) throw new InvalidOperationException("Custom Instruction은 8,000자 이내로 입력하세요.");
        if (PostStopPrompt.Length > 8000) throw new InvalidOperationException("후속 작업 프롬프트는 8,000자 이내로 입력하세요.");
        ShortcutKeys.Validate(Shortcuts);
        if (SummaryCategories.Any(c => c.Id == "none" || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 60 || string.IsNullOrWhiteSpace(c.Prompt) || c.Prompt.Length > 12000) || SummaryCategories.Select(c => c.Id).Distinct().Count() != SummaryCategories.Count)
            throw new InvalidOperationException("카테고리 이름(1~60자)과 프롬프트(1~12,000자)를 확인하세요.");
        if (SummaryCategories.Select(c => c.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != SummaryCategories.Count)
            throw new InvalidOperationException("카테고리 이름이 중복되지 않게 입력하세요.");
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
    internal void EnsurePauseShortcut()
    {
        if(Shortcuts.ContainsKey("pauseResume"))return;
        const string candidate="Primary+Alt+P";
        foreach(var shortcut in Shortcuts.Values.Where(v=>!string.IsNullOrWhiteSpace(v)))
            try{if(ShortcutKeys.Parse(shortcut,false)==ShortcutKeys.Parse(candidate,false)||ShortcutKeys.Parse(shortcut,true)==ShortcutKeys.Parse(candidate,true))return;}catch(ArgumentException){}
        Shortcuts["pauseResume"]=candidate;
    }
    internal static string InitialUiLanguage()
    {
        var marker=OperatingSystem.IsMacOS()?"/Library/Application Support/CatchLessons/install-language.txt":Path.Combine(AppContext.BaseDirectory,"install-language.txt");
        return InitialUiLanguage(marker);
    }
    internal static string InitialUiLanguage(string marker)=>File.Exists(marker)&&File.ReadAllText(marker).Trim()=="ko" ? "ko" : "en";
    public static Settings Load()
    {
        var path = Path.Combine(Root, "settings.json");
        var value=File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new() : new Settings { UiLanguage=InitialUiLanguage() };
        value.MigrateOutputFolder();
        if(!LocalModels.WhisperEnabled)value.SpeechEngine="openai";
        value.EnsurePauseShortcut();
        return value;
    }
    internal void MigrateOutputFolder()
    {
        if(OutputFolderMigrated)return;
        var documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var oldDefault=Path.Combine(documents,"CaptionGlass");
        if(string.Equals(Path.TrimEndingDirectorySeparator(OutputFolder),oldDefault,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            OutputFolder=Path.Combine(documents,"CatchLessons");
        OutputFolderMigrated=true;
    }
}
