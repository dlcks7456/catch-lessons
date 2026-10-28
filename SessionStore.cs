using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;

namespace CatchLessons;

public sealed record Caption(int Id, string ItemId, double Start, double End, string Original, string Korean = "", string State = "pending");

public sealed class SessionStore : IDisposable
{
    private readonly object gate = new();
    private readonly SqliteConnection db;
    private readonly string targetLanguage;
    private readonly bool translationEnabled;
    private DateTime lastExport = DateTime.MinValue;
    private bool readOnly;
    public string Folder { get; }
    public string InternalFolder=>InternalFor(Folder);
    public static string InternalFor(string folder)
    {
        var path=Path.GetFullPath(folder);if(OperatingSystem.IsWindows())path=path.ToUpperInvariant();
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..20];
        return Path.Combine(Settings.Root,"sessions",hash);
    }
    public static string OriginalPath(string folder)
    {
        var current=Path.Combine(folder,"txt","original.txt");return File.Exists(current)?current:Path.Combine(folder,"original.txt");
    }
    public string TargetLanguage => targetLanguage;
    public SessionStore(string root, string targetLanguage = "ko", bool translationEnabled = true)
    {
        this.translationEnabled = translationEnabled;
        this.targetLanguage = Languages.Target(targetLanguage).Code;
        Folder = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(InternalFolder);Directory.CreateDirectory(Path.Combine(Folder,"txt"));Directory.CreateDirectory(Path.Combine(Folder,"srt"));
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(InternalFolder, "session.db") }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE captions(id INTEGER PRIMARY KEY, item TEXT UNIQUE, start REAL, end REAL, original TEXT, korean TEXT, state TEXT);
            """;
        command.ExecuteNonQuery();
        Export();
    }
    public void Save(Caption c)
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                INSERT INTO captions VALUES($id,$item,$start,$end,$original,$korean,$state)
                ON CONFLICT(id) DO UPDATE SET korean=excluded.korean,state=excluded.state;
                """;
            command.Parameters.AddWithValue("$id", c.Id);
            command.Parameters.AddWithValue("$item", c.ItemId);
            command.Parameters.AddWithValue("$start", c.Start);
            command.Parameters.AddWithValue("$end", c.End);
            command.Parameters.AddWithValue("$original", c.Original);
            command.Parameters.AddWithValue("$korean", c.Korean);
            command.Parameters.AddWithValue("$state", c.State);
            command.ExecuteNonQuery();
            if(DateTime.UtcNow-lastExport >= TimeSpan.FromSeconds(30)) Export();
        }
    }
    public List<Caption> Read()
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT * FROM captions ORDER BY start,id";
            using var reader = cmd.ExecuteReader();
            var rows = new List<Caption>();
            while (reader.Read())
                rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            return rows;
        }
    }
    public static string Timestamp(double seconds)
    {
        var t = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, seconds) * 1000));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }
    private void Export()
    {
        lastExport=DateTime.UtcNow;
        var rows = Read();
        var original = new StringBuilder();
        var korean = new StringBuilder();
        var osrt = new StringBuilder();
        var ksrt = new StringBuilder();
        int index = 0;
        foreach (var row in rows)
        {
            index++;
            var translation = row.State == "done" ? row.Korean : $"[번역 {row.State}]";
            original.AppendLine(row.Original);
            korean.AppendLine(translation);
            var cue = $"{index}\n{Timestamp(row.Start)} --> {Timestamp(row.End)}\n";
            osrt.Append(cue).AppendLine(SrtText(row.Original)).AppendLine();
            ksrt.Append(cue).AppendLine(SrtText(translation)).AppendLine();
        }
        Write(Path.Combine("txt","original.txt"), original.ToString());
        if (translationEnabled) Write(Path.Combine("txt","translation.txt"), korean.ToString());
        Write(Path.Combine("srt","original.srt"), osrt.ToString());
        if (translationEnabled) Write(Path.Combine("srt","translation.srt"), ksrt.ToString());
    }
    private static string SrtText(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private void Write(string name, string text)
    {
        var path = Path.Combine(Folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,text,new UTF8Encoding(false));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public void Flush() { lock(gate) {if(!readOnly)Export();} }
    public void UpdateText(Caption caption)
    {
        if(readOnly)throw new InvalidOperationException("읽기 전용 기록입니다.");
        lock(gate)
        {
            using var command=db.CreateCommand();command.CommandText="UPDATE captions SET original=$o,korean=$k,state=$s WHERE id=$id";
            command.Parameters.AddWithValue("$o",caption.Original);command.Parameters.AddWithValue("$k",caption.Korean);command.Parameters.AddWithValue("$s",caption.State);command.Parameters.AddWithValue("$id",caption.Id);
            if(command.ExecuteNonQuery()!=1)throw new IOException("수정할 구간을 찾지 못했습니다.");Export();
        }
    }
    private SessionStore(string folder,bool existing,bool readOnly)
    {
        Folder=Path.GetFullPath(folder); this.readOnly=readOnly; targetLanguage="ko"; translationEnabled=true;
        var metadata=Path.Combine(InternalFolder,"session.json");if(!File.Exists(metadata))metadata=Path.Combine(Folder,"session.json");
        if(File.Exists(metadata))
        {
            using var json=JsonDocument.Parse(File.ReadAllText(metadata));
            if(json.RootElement.TryGetProperty("targetLanguage",out var language))targetLanguage=Languages.Target(language.GetString()).Code;
            if(json.RootElement.TryGetProperty("mode",out var mode))translationEnabled=mode.GetString()!="transcription";
        }
        var dbPath=Path.Combine(InternalFolder,"session.db");if(!File.Exists(dbPath))dbPath=Path.Combine(Folder,"session.db");
        db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=dbPath,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite}.ToString());db.Open();
    }
    public static SessionStore OpenExisting(string folder,bool readOnly=true)=>new(folder,true,readOnly);
    public void Dispose() { lock (gate) {try{if(!readOnly)Export();}finally{db.Dispose();}} }
}
