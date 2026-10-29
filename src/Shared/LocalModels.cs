using System.Security.Cryptography;

namespace CatchLessons;

public sealed record LocalModel(string Id, string Name, string File, long Bytes, string Sha256, string Repository = "ggerganov/whisper.cpp") {public string DisplayName=>UiText.T(Name);public override string ToString()=>DisplayName;}

public static class LocalModels
{
    public static readonly LocalModel[] Choices =
    [
        new("balanced", "균형 · large-v3-turbo / 약 548 MB", "ggml-large-v3-turbo-q5_0.bin", 574041195, "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"),
        new("fast", "가볍게 · small / 약 181 MB", "ggml-small-q5_1.bin", 190085487, "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb"),
        new("quality", "품질 우선 · large-v3 / 약 1 GB", "ggml-large-v3-q5_0.bin", 1081140203, "d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1")
    ];
    public static readonly LocalModel Vad = new("vad", "말소리 구간 검출", "ggml-silero-v6.2.0.bin", 885098,
        "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987", "ggml-org/whisper-vad");
    public static string Folder => Path.Combine(OperatingSystem.IsMacOS()?Settings.Root:AppContext.BaseDirectory, "models");
    internal static bool HasAnyExistingModel=>HasExistingModel(Folder)||(OperatingSystem.IsMacOS()&&HasExistingModel("/Library/Application Support/CatchLessons/models"));
    public static bool WhisperEnabled=>(WhisperEnabledAt(AppContext.BaseDirectory)&&(!OperatingSystem.IsMacOS()||WhisperEnabledAt("/Library/Application Support/CatchLessons")))||HasAnyExistingModel;
    internal static bool HasExistingModel(string folder)=>Choices.Any(model=>{var path=Path.Combine(folder,model.File);try{return File.Exists(path)&&new FileInfo(path).Length==model.Bytes;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}});
    internal static bool WhisperEnabledAt(string directory)=>!File.Exists(Path.Combine(directory,"whisper-disabled.flag"));
    public static LocalModel Resolve(string id) => Choices.FirstOrDefault(m => m.Id == id) ?? Choices[0];
    public static async Task<bool> ValidAsync(string path, LocalModel model, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != model.Bytes) return false;
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public static async Task<string> EnsureAsync(LocalModel model, Action<string> progress, CancellationToken token)
    {
        if(OperatingSystem.IsMacOS())
        {
            var bundled=Path.Combine("/Library/Application Support/CatchLessons/models",model.File);
            if(await ValidAsync(bundled,model,token))return bundled;
        }
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, model.File);
        progress($"로컬 모델 확인 중: {model.DisplayName}");
        if (await ValidAsync(path, model, token)) return path;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await http.GetAsync($"https://huggingface.co/{model.Repository}/resolve/main/{model.File}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[128 * 1024];
                long total = 0; int lastPercent = -1, read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > model.Bytes) throw new IOException("모델 다운로드 크기가 예상과 다릅니다.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    int percent = (int)(100 * total / model.Bytes);
                    if (percent != lastPercent)
                    {
                        progress($"모델 다운로드 {percent}% ({total / 1048576d:F1} / {model.Bytes / 1048576d:F1} MB) · {model.Id}");
                        lastPercent = percent;
                    }
                }
            }
            progress("다운로드한 모델의 SHA-256을 검증하고 있습니다…");
            if (!await ValidAsync(temporary, model, timeout.Token)) throw new IOException("모델 무결성 확인에 실패했습니다. 다시 시작하면 재다운로드합니다.");
            File.Move(temporary, path, true);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
