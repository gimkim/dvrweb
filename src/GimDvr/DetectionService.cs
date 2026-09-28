using System.Diagnostics;
using System.Text.Json;

namespace GimDvr;

// Completed files reuse the recorder input with no extra camera connection or video encoding.
// One model/process and one clip at a time; the disk lock also excludes IIS recycle overlap.
public sealed class DetectionService(Store store,Paths paths,IConfiguration config,ILogger<DetectionService> log):BackgroundService
{
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    readonly DetectionLog diagnostics=new(Path.Combine(paths.Data,"logs","detection"),log);
    readonly Stopwatch uptime=Stopwatch.StartNew();
    readonly object metricsGate=new();
    long completed,partial,failed;
    double completedVideoSeconds,totalWorkSeconds,lastVideoSeconds,lastSummarySeconds;
    string workerState="starting";
    string? currentRecording;
    void Summary()
    {
        object stats;
        lock(metricsGate)
        {
            var elapsed=uptime.Elapsed.TotalSeconds;var window=elapsed-lastSummarySeconds;
            stats=new{workerState,currentRecording,uptimeSeconds=Math.Round(elapsed,2),windowSeconds=Math.Round(window,2),completed,partial,failed,
                completedVideoSeconds=Math.Round(completedVideoSeconds,2),totalWorkSeconds=Math.Round(totalWorkSeconds,2),
                completedVideoSecondsPerWallSecond=window>0?Math.Round((completedVideoSeconds-lastVideoSeconds)/window,3):0,
                lifetimeVideoSecondsPerWallSecond=elapsed>0?Math.Round(completedVideoSeconds/elapsed,3):0};
            lastVideoSeconds=completedVideoSeconds;lastSummarySeconds=elapsed;
        }
        try{diagnostics.Write("summary",new{stats,queue=store.DetectionQueueSnapshot()});}
        catch(Exception e){diagnostics.Write("summary_error",new{errorType=e.GetType().Name});}
    }
    async Task Heartbeat(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(60));
        try{while(await timer.WaitForNextTickAsync(ct))Summary();}catch(OperationCanceledException) when(ct.IsCancellationRequested){}
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        diagnostics.Write("service_start",new{externalMedia=paths.ExternalMedia,enabled=config.GetValue<bool?>("Dvr:DetectionEnabled")!=false,preferredDevice=config["Dvr:DetectionDevice"]??"GPU"});
        if(paths.ExternalMedia){diagnostics.Write("service_skipped",new{reason="external_media_owner"});return;}
        var runtime=config["Dvr:DetectionRuntime"]??Path.Combine(Path.GetDirectoryName(paths.Data)!,"GimDvrDetection");
        var python=Path.Combine(runtime,"python.exe");
        if(config.GetValue<bool?>("Dvr:DetectionEnabled")==false){diagnostics.Write("service_skipped",new{reason="disabled"});return;}
        var script=Path.Combine(AppContext.BaseDirectory,"Detection","detect.py");
        var model=Path.Combine(runtime,"models","person.xml");
        Directory.CreateDirectory(paths.Runtime);
        using var heartbeatStop=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat=Heartbeat(heartbeatStop.Token);
        Summary();
        try
        {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(!File.Exists(python)||!File.Exists(model)){workerState="waiting_runtime";diagnostics.Write("runtime_missing",new{pythonPresent=File.Exists(python),modelPresent=File.Exists(model)});await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);continue;}
                workerState="waiting_owner";
                using var gate=new FileStream(Path.Combine(paths.Runtime,"detection.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                diagnostics.Write("owner_acquired",new{});
                store.ResetInterruptedDetections();
                await Run(python,script,model,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){workerState="retrying";diagnostics.Write("worker_error",new{errorType=e.GetType().Name});log.LogWarning("Detection worker unavailable ({Type}); retrying",e.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);}catch(OperationCanceledException){break;}
        }
        }
        finally{heartbeatStop.Cancel();await heartbeat;workerState="stopped";Summary();diagnostics.Write("service_stop",new{});}
    }
    async Task Run(string python,string script,string model,CancellationToken ct)
    {
        var start=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var a in new[]{"-u",script,"--model",model,"--ffmpeg",paths.Ffmpeg,"--device",config["Dvr:DetectionDevice"]??"GPU"})start.ArgumentList.Add(a);
        using var process=new Process{StartInfo=start};process.Start();
        workerState="running";diagnostics.Write("process_start",new{childPid=process.Id});
        IDisposable? job=null;
        var drain=process.StandardError.BaseStream.CopyToAsync(Stream.Null,ct); // Bounded; never log private paths.
        try
        {
            job=ProcessJob.Attach(process);
            try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
            var turn=0;
            while(!ct.IsCancellationRequested&&!process.HasExited)
            {
                var recording=store.NextDetection(++turn%4==0);
                if(recording is null){workerState="idle";await Task.Delay(3000,ct);continue;}
                workerState="processing";currentRecording=recording.Id;
                var watch=Stopwatch.StartNew();
                diagnostics.Write("clip_start",new{recordingId=recording.Id,cameraId=recording.CameraId,videoSeconds=recording.Duration,
                    ageAtStartSeconds=Math.Round((DateTimeOffset.UtcNow-recording.Start.AddSeconds(recording.Duration)).TotalSeconds,2)});
                store.SaveDetection(recording.Id,new("processing"));
                try
                {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{path=recording.Path,duration=recording.Duration}).AsMemory(),timeout.Token);
                    await process.StandardInput.FlushAsync(timeout.Token);
                    var line=await process.StandardOutput.ReadLineAsync(timeout.Token)??throw new IOException("Detector exited");
                    var result=JsonSerializer.Deserialize<DetectionResult>(line,Json)??throw new IOException("No result");
                    if(result.State is not("complete" or "partial" or "error"))throw new IOException("Invalid result");
                    store.SaveDetection(recording.Id,result);
                    lock(metricsGate){totalWorkSeconds+=watch.Elapsed.TotalSeconds;if(result.State=="complete"){completed++;completedVideoSeconds+=recording.Duration;}else if(result.State=="partial")partial++;else failed++;}
                    diagnostics.Write("clip_finish",new{recordingId=recording.Id,cameraId=recording.CameraId,videoSeconds=recording.Duration,
                        elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,3),videoSecondsPerWorkSecond=Math.Round(recording.Duration/Math.Max(watch.Elapsed.TotalSeconds,0.001),3),result});
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested){diagnostics.Write("clip_cancelled",new{recordingId=recording.Id,elapsedSeconds=watch.Elapsed.TotalSeconds});store.SaveDetection(recording.Id,new("pending"));throw;}
                catch(Exception e){lock(metricsGate){failed++;totalWorkSeconds+=watch.Elapsed.TotalSeconds;}diagnostics.Write("clip_error",new{recordingId=recording.Id,elapsedSeconds=watch.Elapsed.TotalSeconds,errorType=e.GetType().Name,timeout=e is OperationCanceledException});store.SaveDetection(recording.Id,new("error",Error:"worker_failed"));throw;}
                finally{currentRecording=null;}
                workerState="between_clips";
                await Task.Delay(1000,ct);
            }
        }
        finally
        {
            try{if(!process.HasExited)process.Kill(true);}catch{}
            await process.WaitForExitAsync(CancellationToken.None);
            diagnostics.Write("process_stop",new{childPid=process.Id,exitCode=process.ExitCode});
            job?.Dispose();
            try{await drain;}catch(OperationCanceledException){}
        }
    }
}
