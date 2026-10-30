using System.Diagnostics;

namespace CatchLessons;

internal static class CodexSessionLink
{
    internal static string Create(string id)
    {
        if(!Guid.TryParse(id,out var thread))throw new ArgumentException("Invalid Codex session ID.");
        return "codex://threads/"+thread.ToString("D");
    }
    internal static void Open(string id)=>Process.Start(new ProcessStartInfo(Create(id)){UseShellExecute=true});
}
