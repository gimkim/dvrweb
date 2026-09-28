using System.Diagnostics;
using System.Text.Json;

namespace GimDvr;

// Completed files reuse the recorder input with no extra camera connection or video encoding.
// Bounded persistent detector lanes; one owner lock excludes IIS recycle overlap.
public sealed class DetectionService(Store store,Paths paths,IConfiguration config,ILogger<DetectionService> log):BackgroundService
{
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    readonly DetectionLog diagnostics=new(Path.Combine(paths.Data,"logs","detection"),log);
    readonly Stopwatch uptime=Stopwatch.StartNew();
    readonly object metricsGate=new();
    long completed,partial,failed;
    double completedVideoSeconds,totalWorkSeconds,lastVideoSeconds,lastSummarySeconds;
    string workerState="starting";
    readonly System.Collections.Concurrent.ConcurrentDictionary<int,string> activeRecordings=new();
    readonly SemaphoreSlim remoteGate=new(1,1);
    int scheduleTurn;
    double readRate=>Math.Clamp(config.GetValue<double?>("Dvr:DetectionReadRate")??4,0,32);
    int concurrency=>Math.Clamp(config.GetValue<int?>("Dvr:DetectionConcurrency")??1,1,4);
    void Summary()
    {
        object stats;
        lock(metricsGate)
        {
            var elapsed=uptime.Elapsed.TotalSeconds;var window=elapsed-lastSummarySeconds;
            stats=new{workerState=activeRecordings.IsEmpty?workerState:"processing",concurrency,readRate,activeJobs=activeRecordings.Count,currentRecordings=activeRecordings.Values.ToArray(),uptimeSeconds=Math.Round(elapsed,2),windowSeconds=Math.Round(window,2),completed,partial,failed,
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
        using var remote=new RemoteDetection(config,diagnostics.Write);
        var remotePoll=remote.Poll(heartbeatStop.Token);
        var heartbeat=Heartbeat(heartbeatStop.Token);
        Summary();
        try
        {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if((!File.Exists(python)||!File.Exists(model))&&!remote.Available){workerState="waiting_runtime";diagnostics.Write("runtime_missing",new{pythonPresent=File.Exists(python),modelPresent=File.Exists(model)});await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);continue;}
                workerState="waiting_owner";
                using var gate=new FileStream(Path.Combine(paths.Runtime,"detection.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                diagnostics.Write("owner_acquired",new{});
                store.ResetInterruptedDetections();
                await Task.WhenAll(Enumerable.Range(0,concurrency).Select(lane=>RunLane(lane,python,script,model,remote,stoppingToken)));
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){workerState="retrying";diagnostics.Write("worker_error",new{errorType=e.GetType().Name});log.LogWarning("Detection worker unavailable ({Type}); retrying",e.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);}catch(OperationCanceledException){break;}
        }
        }
        finally{heartbeatStop.Cancel();await heartbeat;await remotePoll;workerState="stopped";Summary();diagnostics.Write("service_stop",new{});}
    }
    async Task RunLane(int lane,string python,string script,string model,RemoteDetection remote,CancellationToken ct)
    {
        while(!ct.IsCancellationRequested){try{await Run(lane,python,script,model,remote,ct);}catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}catch(Exception e){diagnostics.Write("lane_error",new{lane,errorType=e.GetType().Name});try{await Task.Delay(15000,ct);}catch(OperationCanceledException){break;}}}
    }
    async Task Run(int lane,string python,string script,string model,RemoteDetection remote,CancellationToken ct)
    {
        await using var detector=new DetectionProcess(python,script,model,paths.Ffmpeg,config["Dvr:DetectionDevice"]??"GPU",events:diagnostics.Write);

        while(!ct.IsCancellationRequested)
        {
            var recording=store.ClaimDetection(Interlocked.Increment(ref scheduleTurn)%4==0);
            if(recording is null){workerState="idle";await Task.Delay(3000,ct);continue;}
            workerState="processing";activeRecordings[lane]=recording.Id;
            var watch=Stopwatch.StartNew();
            diagnostics.Write("clip_start",new{lane,recordingId=recording.Id,cameraId=recording.CameraId,videoSeconds=recording.Duration,
                ageAtStartSeconds=Math.Round((DateTimeOffset.UtcNow-recording.Start.AddSeconds(recording.Duration)).TotalSeconds,2)});

            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(5));
                DetectionResult? result=null;
                if(remote.Available){await remoteGate.WaitAsync(timeout.Token);try{result=await remote.Analyze(recording,timeout.Token);}finally{remoteGate.Release();}}
                if(result is null)
                {
                    var reply=await detector.Request(new{path=recording.Path,duration=recording.Duration,readrate=readRate},timeout.Token);
                    result=reply.Deserialize<DetectionResult>(Json)??throw new IOException("No result");
                }
                if(result.State is not("complete" or "partial" or "error"))throw new IOException("Invalid result");
                store.SaveDetection(recording.Id,result);
                lock(metricsGate){totalWorkSeconds+=watch.Elapsed.TotalSeconds;if(result.State=="complete"){completed++;completedVideoSeconds+=recording.Duration;}else if(result.State=="partial")partial++;else failed++;}
                diagnostics.Write("clip_finish",new{lane,recordingId=recording.Id,cameraId=recording.CameraId,videoSeconds=recording.Duration,
                    elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,3),videoSecondsPerWorkSecond=Math.Round(recording.Duration/Math.Max(watch.Elapsed.TotalSeconds,0.001),3),result});
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){diagnostics.Write("clip_cancelled",new{lane,recordingId=recording.Id,elapsedSeconds=watch.Elapsed.TotalSeconds});store.SaveDetection(recording.Id,new("pending"));throw;}
            catch(Exception e){lock(metricsGate){failed++;totalWorkSeconds+=watch.Elapsed.TotalSeconds;}diagnostics.Write("clip_error",new{lane,recordingId=recording.Id,elapsedSeconds=watch.Elapsed.TotalSeconds,errorType=e.GetType().Name,timeout=e is OperationCanceledException});store.SaveDetection(recording.Id,new("error",Error:"worker_failed"));throw;}
            finally{activeRecordings.TryRemove(lane,out _);}
            workerState="between_clips";
            await Task.Delay(1000,ct);
        }
    }
}
