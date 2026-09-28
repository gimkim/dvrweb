using System.Diagnostics;
namespace GimDvr;
public sealed partial class MediaService
{
    async Task UpdateEncoder(Run run)
    {
        var wanted=LastWatch(run.Camera.Id,true)>DateTimeOffset.UtcNow.AddSeconds(-8);
        if(run.Encoder is not null&&wanted&&run.EncoderName=="h264_qsv"&&(run.Encoder.HasExited||DateTimeOffset.UtcNow-run.EncoderStarted>TimeSpan.FromSeconds(15)&&(!File.Exists(Path.Combine(run.Live,"index.m3u8"))||File.GetLastWriteTimeUtc(Path.Combine(run.Live,"index.m3u8"))<DateTime.UtcNow.AddSeconds(-10))))
        {run.QsvFailed=true;log.LogWarning("QSV live encoder failed for {Id}; using CPU fallback",run.Camera.Id);await StopEncoder(run);}
        if(run.Encoder is not null&&(!wanted||run.Encoder.HasExited))await StopEncoder(run);
        if(!wanted||run.Encoder is not null)return;
        var qsv=!run.QsvFailed&&await QsvAvailable();
        run.Live=Path.Combine(paths.Live,run.Camera.Id,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run.Live);
        var info=new ProcessStartInfo(paths.Ffmpeg){WorkingDirectory=run.Live,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};
        string[] args=["-hide_banner","-loglevel","error","-nostats","-probesize","262144","-analyzeduration","200000","-f","mpegts","-i",$"udp://127.0.0.1:{run.RelayPort}?localaddr=127.0.0.1&fifo_size=4096&overrun_nonfatal=1","-map","0:v:0","-map","0:a:0?",..LiveVideoArguments(qsv,paths.LiveHeight),"-c:a","aac","-ar","16000","-ac","1","-b:a","48k","-f","hls","-hls_time","0.5","-hls_list_size","12","-hls_start_number_source","epoch_us","-hls_flags","delete_segments+independent_segments+temp_file","-hls_segment_filename",Path.Combine(run.Live,"seg%09d.ts"),Path.Combine(run.Live,"index.m3u8")];
        foreach(var arg in args)info.ArgumentList.Add(arg);
        var process=new Process{StartInfo=info};process.ErrorDataReceived+=(_,e)=>{if(!string.IsNullOrEmpty(e.Data))log.LogDebug("Live encoder {Id}: {Message}",run.Camera.Id,Sanitize(e.Data,run.Camera));};
        var started=false;
        try{started=process.Start();if(!started)throw new InvalidOperationException("Live encoder did not start");run.EncoderJob=ProcessJob.Attach(process);process.BeginErrorReadLine();run.Encoder=process;run.EncoderName=qsv?"h264_qsv":"libx264";run.EncoderStarted=DateTimeOffset.UtcNow;}
        catch{if(started&&!process.HasExited)process.Kill(true);process.Dispose();throw;}
    }
    async Task StopEncoder(Run run)
    {
        var process=run.Encoder;if(process is null)return;run.Encoder=null;run.EncoderName="none";
        try{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        finally{run.EncoderJob?.Dispose();run.EncoderJob=null;process.Dispose();}
    }
}
