using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GimDvr;

// Structured local diagnostics only; callers pass IDs/counters, never paths, commands or stderr.
public sealed class DetectionLog
{
    readonly string directory;
    readonly ILogger logger;
    readonly object gate=new();
    readonly string runId=Guid.NewGuid().ToString("N");
    readonly long maxBytes;
    readonly int maxFiles;
    string? current;
    string day="";
    int sequence;
    DateTimeOffset nextWarning;
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    public DetectionLog(string directory,ILogger logger,long maxBytes=8*1024*1024,int maxFiles=32)
    {this.directory=directory;this.logger=logger;this.maxBytes=maxBytes;this.maxFiles=maxFiles;}
    public void Write(string kind,object data)
    {
        lock(gate)
        try
        {
            var now=DateTimeOffset.UtcNow;Directory.CreateDirectory(directory);
            var today=now.ToString("yyyyMMdd");
            if(current is null||!File.Exists(current)||day!=today||new FileInfo(current).Length>=maxBytes)
            {
                day=today;current=Path.Combine(directory,$"detection-{today}-{Environment.ProcessId}-{runId[..8]}-{sequence++:D4}.jsonl");
            }
            var line=JsonSerializer.Serialize(new{timeUtc=now,kind,version="1.10.3",pid=Environment.ProcessId,runId,data},Json);
            File.AppendAllText(current,line+"\n",new UTF8Encoding(false));
            var files=new DirectoryInfo(directory).EnumerateFiles("detection-*.jsonl")
                .Where(f=>Regex.IsMatch(f.Name,@"^detection-\d{8}-\d+-[a-f0-9]{8}-\d{4,}\.jsonl$"))
                .OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();
            foreach(var file in files.Skip(maxFiles).Concat(files.Where(f=>f.LastWriteTimeUtc<now.UtcDateTime.AddDays(-14))).DistinctBy(f=>f.FullName))
                if(file.FullName!=current)file.Delete();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {
            if(DateTimeOffset.UtcNow>=nextWarning){nextWarning=DateTimeOffset.UtcNow.AddMinutes(5);logger.LogWarning("Cannot write detection diagnostic log ({Type})",e.GetType().Name);}
        }
    }
}
