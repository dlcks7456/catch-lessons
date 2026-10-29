using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Security.Cryptography;
namespace CatchLessons;
public sealed record UpdateResult(string Version,Uri? Download,bool Newer,string? Sha256=null);
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
        Uri? download=null;string? sha256=null;
        architecture??=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.Arm64?"arm64":"x64";
        if(root.TryGetProperty("assets",out var assets))foreach(var asset in assets.EnumerateArray())
        {
            var name=asset.GetProperty("name").GetString()??"";if(!name.EndsWith(mac?".pkg":".exe",StringComparison.OrdinalIgnoreCase)||!name.Contains(mac?"macOS":"Windows",StringComparison.OrdinalIgnoreCase))continue;
            if(mac&&!name.Contains("universal",StringComparison.OrdinalIgnoreCase)&&!name.Contains(architecture,StringComparison.OrdinalIgnoreCase))continue;
            if(!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!uri.AbsolutePath.StartsWith("/"+repository+"/releases/download/",StringComparison.OrdinalIgnoreCase))throw new IOException("Release download URL is invalid.");
            download=uri;var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
            if(digest!=null&&Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$"))sha256=digest[7..];break;
        }
        return new(tag,download,version>installed,sha256);
    }
    public static async Task<UpdateResult> CheckAsync(bool mac,CancellationToken token)
    {
        var repository=NormalizeRepository(BuildRepository)??throw new InvalidOperationException("Update repository is not configured in the build.");
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CatchLessons",CurrentVersion));
        var json=await client.GetStringAsync("https://api.github.com/repos/"+repository+"/releases/latest",token);
        return Parse(repository,json,CurrentVersion,mac);
    }
    public static Task<string> DownloadAsync(UpdateResult update,IProgress<int?> progress,CancellationToken token)=>DownloadAsync(update,progress,token,null);
    internal static async Task<string> DownloadAsync(UpdateResult update,IProgress<int?> progress,CancellationToken token,HttpMessageHandler? handler)
    {
        if(!update.Newer||update.Download==null||update.Sha256==null)throw new IOException("The update does not have a valid SHA-256 digest.");
        var path=Path.Combine(Path.GetTempPath(),"CatchLessons-update-"+Guid.NewGuid().ToString("N")+(OperatingSystem.IsMacOS()?".pkg":".exe"));
        try
        {
            using var client=handler==null?new HttpClient():new HttpClient(handler);client.Timeout=Timeout.InfiniteTimeSpan;client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CatchLessons",CurrentVersion));
            using var response=await client.GetAsync(update.Download,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            var length=response.Content.Headers.ContentLength;if(length is > 2_000_000_000)throw new IOException("Update is too large.");
            await using var source=await response.Content.ReadAsStreamAsync(token);await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[81920];long total=0;int count;
            while((count=await source.ReadAsync(buffer,token))>0){total+=count;if(total>2_000_000_000)throw new IOException("Update is too large.");hash.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),token);progress.Report(length is > 0?(int)Math.Min(100,total*100/length.Value):null);}
            if(!string.Equals(Convert.ToHexString(hash.GetHashAndReset()),update.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Update integrity check failed.");
            return path;
        }
        catch{try{File.Delete(path);}catch(IOException){}throw;}
    }
    public static string WindowsInstallLocation()
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Automatic updates are not yet available on macOS.");
        using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{C39D01BF-F322-45D5-93AB-4402D96C828A}_is1");
        var install=key?.GetValue("InstallLocation") as string;
        if(string.IsNullOrWhiteSpace(install)||!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(install)),Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),StringComparison.OrdinalIgnoreCase))throw new IOException("Automatic updates require an installed copy of CatchLessons.");
        return install;
    }
    public static void StartWindowsInstaller(string path)
    {
        var install=WindowsInstallLocation();
        var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in WindowsInstallerArguments(install,LocalModels.WhisperEnabled&&!LocalModels.HasAnyExistingModel))start.ArgumentList.Add(arg);
        _=Process.Start(start)??throw new IOException("Could not start the updater.");
    }
    internal static string[] WindowsInstallerArguments(string install,bool whisper)=>["/SILENT","/NORESTART","/CLOSEAPPLICATIONS","/RESTARTAPPLICATIONS","/AUTOMATICUPDATE",whisper?"/TASKS=whisper":"/TASKS=!whisper","/DIR="+install];
    public static void EnsureMacInstalled()
    {
        if(!OperatingSystem.IsMacOS()||!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),"/Applications/CatchLessons.app/Contents/MacOS",StringComparison.Ordinal))
            throw new IOException("Automatic updates require CatchLessons to be installed in Applications.");
    }
    public static async Task InstallMacAsync(string path)
    {
        EnsureMacInstalled();
        var choices=Path.Combine(Path.GetTempPath(),"CatchLessons-choices-"+Guid.NewGuid().ToString("N")+".plist");
        var xml=new System.Xml.Linq.XDocument(new System.Xml.Linq.XDeclaration("1.0","UTF-8",null),new System.Xml.Linq.XElement("plist",new System.Xml.Linq.XAttribute("version","1.0"),new System.Xml.Linq.XElement("array",new System.Xml.Linq.XElement("dict",new System.Xml.Linq.XElement("key","choiceIdentifier"),new System.Xml.Linq.XElement("string","whisper"),new System.Xml.Linq.XElement("key","choiceAttribute"),new System.Xml.Linq.XElement("string","selected"),new System.Xml.Linq.XElement("key","attributeSetting"),new System.Xml.Linq.XElement("integer",LocalModels.WhisperEnabled&&!LocalModels.HasAnyExistingModel?1:0)))));
        xml.Save(choices);
        static string Quote(string value)=>"'"+value.Replace("'","'\\''")+"'";
        var command="/usr/sbin/installer -pkg "+Quote(path)+" -target / -applyChoiceChangesXML "+Quote(choices);
        var appleScript="do shell script \""+command.Replace("\\","\\\\").Replace("\"","\\\"")+"\" with administrator privileges";
        var start=new ProcessStartInfo("/usr/bin/osascript"){UseShellExecute=false,RedirectStandardError=true};start.ArgumentList.Add("-e");start.ArgumentList.Add(appleScript);
        try{using var process=Process.Start(start)??throw new IOException("Could not start the updater.");var errors=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new IOException("Update installation failed: "+(await errors).Trim());}finally{try{File.Delete(choices);}catch(IOException){}}
        var restart=new ProcessStartInfo("/bin/sh"){UseShellExecute=false};restart.ArgumentList.Add("-c");restart.ArgumentList.Add("while kill -0 \"$1\" 2>/dev/null; do sleep 1; done; /usr/bin/open -a /Applications/CatchLessons.app");restart.ArgumentList.Add("catchlessons-update");restart.ArgumentList.Add(Environment.ProcessId.ToString());
        _=Process.Start(restart)??throw new IOException("Could not restart CatchLessons.");
    }
}
