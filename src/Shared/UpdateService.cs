using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
namespace CatchLessons;
public sealed record UpdateResult(string Version,Uri? Download,bool Newer);
public static class UpdateService
{
    public static string CurrentVersion=>typeof(UpdateService).Assembly.GetName().Version?.ToString(3)??"1.0.0";
    public static string BuildRepository=>typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a=>a.Key=="CatchLessonsUpdateRepository")?.Value?.Trim()??"";
    public static bool RepositoryConfigured=>NormalizeRepository(BuildRepository)!=null;
    public static bool ValidRepository(string? value)=>!string.IsNullOrWhiteSpace(value)&&Regex.IsMatch(value,@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")&&!value.Split('/').Any(s=>s is "." or "..");
    public static string? NormalizeRepository(string? value)
    {
        value=value?.Trim();if(string.IsNullOrEmpty(value))return null;
        if(ValidRepository(value))return value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0)return null;
        var path=uri.AbsolutePath.Trim('/');return ValidRepository(path)?path:null;
    }
    public static UpdateResult Parse(string repository,string json,string current,bool mac,string? architecture=null)
    {
        if(!ValidRepository(repository))throw new ArgumentException("Enter the repository as owner/name.");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var tag=root.GetProperty("tag_name").GetString()??"";
        if(!Version.TryParse(tag.TrimStart('v','V'),out var version)||!Version.TryParse(current,out var installed))throw new IOException("Release version is invalid.");
        version=new Version(version.Major,version.Minor,Math.Max(version.Build,0),Math.Max(version.Revision,0));installed=new Version(installed.Major,installed.Minor,Math.Max(installed.Build,0),Math.Max(installed.Revision,0));
        Uri? download=null;
        architecture??=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.Arm64?"arm64":"x64";
        if(root.TryGetProperty("assets",out var assets))foreach(var asset in assets.EnumerateArray())
        {
            var name=asset.GetProperty("name").GetString()??"";if(!name.EndsWith(mac?".pkg":".exe",StringComparison.OrdinalIgnoreCase)||!name.Contains(mac?"macOS":"Windows",StringComparison.OrdinalIgnoreCase))continue;
            if(mac&&!name.Contains("universal",StringComparison.OrdinalIgnoreCase)&&!name.Contains(architecture,StringComparison.OrdinalIgnoreCase))continue;
            if(!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!uri.AbsolutePath.StartsWith("/"+repository+"/releases/download/",StringComparison.OrdinalIgnoreCase))throw new IOException("Release download URL is invalid.");
            download=uri;break;
        }
        return new(tag,download,version>installed);
    }
    public static async Task<UpdateResult> CheckAsync(bool mac,CancellationToken token)
    {
        var repository=NormalizeRepository(BuildRepository)??throw new InvalidOperationException("Update repository is not configured in the build.");
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CatchLessons",CurrentVersion));
        var json=await client.GetStringAsync("https://api.github.com/repos/"+repository+"/releases/latest",token);
        return Parse(repository,json,CurrentVersion,mac);
    }
    public static void OpenDownload(Uri uri)=>Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
}
