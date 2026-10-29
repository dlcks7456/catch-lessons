using System.Text.Json;


namespace CatchLessons;
public sealed record AgentModel(string Id,string[] Efforts){public override string ToString()=>Id;}
public sealed record AgentEffort(string Id,string Label){public override string ToString()=>Label;}
public static class AgentModels
{
    private static string Home=>Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string CodexHome=>Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Home,".codex");
    public static AgentModel[] Read(string provider)
    {
        if(provider=="claude")
        {
            var ids=new List<string>{"haiku","sonnet","opus"};
            try{using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(Home,".claude","settings.json")));var root=json.RootElement;if(root.TryGetProperty("availableModels",out var allowed)&&allowed.ValueKind==JsonValueKind.Array)ids=allowed.EnumerateArray().Select(x=>x.GetString()!).ToList();if(root.TryGetProperty("model",out var model)&&!ids.Contains(model.GetString()!))ids.Add(model.GetString()!);}catch(IOException){}catch(JsonException){}
            return ids.Distinct().Select(id=>new AgentModel(id,["low","medium","high","xhigh","max"])).ToArray();
        }
        try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(CodexHome,"models_cache.json")));
            var models=json.RootElement.GetProperty("models").EnumerateArray().Where(m=>!m.TryGetProperty("visibility",out var v)||v.GetString()=="list").Select(m=>new AgentModel(m.GetProperty("slug").GetString()!,m.GetProperty("supported_reasoning_levels").EnumerateArray().Select(e=>e.GetProperty("effort").GetString()!).ToArray())).ToArray();
            if(models.Length>0)return models;
        }catch(IOException){}catch(JsonException){}catch(KeyNotFoundException){}
        return [new("gpt-6-luna",["low","medium","high","xhigh","max"]),new("gpt-6.1-sol",["low","medium","high","xhigh","max"])];
    }
    public static AgentEffort[] Efforts(string provider,string model)=> (Read(provider).FirstOrDefault(m=>m.Id==model)?.Efforts??["low","medium","high"]).Select(e=>new AgentEffort(e,provider=="chatgpt"&&e=="low"?"Light (low)":e)).ToArray();
}

