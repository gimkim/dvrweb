using System.Diagnostics;
using System.Text.Json;
namespace GimDvr;
public sealed partial class MediaService
{
    Task<bool>? qsvProbe;
    internal static string[] LiveVideoArguments(bool qsv,int height)=>qsv
        ? ["-c:v","h264_qsv","-preset","veryfast","-async_depth","1","-look_ahead","0","-b:v","4000k","-maxrate","4000k","-bufsize","1000k","-low_delay_brc","1","-vf",$"fps=15,scale=-2:{height},format=nv12","-profile:v","baseline","-g","8","-bf","0"]
        : ["-c:v","libx264","-preset","ultrafast","-tune","zerolatency","-crf","24","-vf",$"fps=15,scale=-2:{height}","-pix_fmt","yuv420p","-profile:v","baseline","-g","8","-keyint_min","8","-sc_threshold","0","-bf","0"];
    Task<bool> QsvAvailable()=>paths.LiveEncoder.Equals("cpu",StringComparison.OrdinalIgnoreCase)?Task.FromResult(false):qsvProbe??=ProbeQsv();
    async Task<bool> ProbeQsv()
    {
        var info=new ProcessStartInfo(paths.Ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-hide_banner","-loglevel","error","-f","lavfi","-i",$"color=size=1920x{paths.LiveHeight}:rate=15","-frames:v","3"}.Concat(LiveVideoArguments(true,paths.LiveHeight)).Concat(new[]{"-progress","pipe:1","-f","null","-"}))info.ArgumentList.Add(arg);
        bool supported=false;string reason="Quick Sync probe failed";using var process=new Process{StartInfo=info};bool started=false;
        try
        {
            started=process.Start();if(!started)throw new InvalidOperationException();
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(8));await process.WaitForExitAsync(limit.Token);
            var progress=await output;var diagnostic=await error;
            supported=process.ExitCode==0&&System.Text.RegularExpressions.Regex.IsMatch(progress,@"(?m)^frame=\s*[3-9]\d*\r?$");
            reason=supported?"Hardware H264 encode verified (3 frames)":diagnostic.Length>1500?diagnostic[..1500]:diagnostic;
        }
        catch(Exception e){reason=e.GetType().Name;}
        finally{if(started&&!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        Directory.CreateDirectory(paths.Runtime);
        File.WriteAllText(Path.Combine(paths.Runtime,"qsv-status.json"),JsonSerializer.Serialize(new{checkedAt=DateTimeOffset.UtcNow,supported,reason,mode=paths.LiveEncoder}));
        log.LogInformation("Quick Sync probe supported={Supported}; CPU fallback available",supported);return supported;
    }
}


