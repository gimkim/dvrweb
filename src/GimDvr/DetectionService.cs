using System.Diagnostics;
using System.Text.Json;

namespace GimDvr;

// Completed files reuse the recorder input with no extra camera connection or video encoding.
// One model/process and one clip at a time; the disk lock also excludes IIS recycle overlap.
public sealed class DetectionService(Store store,Paths paths,IConfiguration config,ILogger<DetectionService> log):BackgroundService
{
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(paths.ExternalMedia)return;
        var runtime=config["Dvr:DetectionRuntime"]??Path.Combine(Path.GetDirectoryName(paths.Data)!,"GimDvrDetection");
        var python=Path.Combine(runtime,"python.exe");
        if(config.GetValue<bool?>("Dvr:DetectionEnabled")==false)return;
        var script=Path.Combine(AppContext.BaseDirectory,"Detection","detect.py");
        var model=Path.Combine(runtime,"models","person.xml");
        Directory.CreateDirectory(paths.Runtime);
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(!File.Exists(python)||!File.Exists(model)){await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);continue;}
                using var gate=new FileStream(Path.Combine(paths.Runtime,"detection.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                store.ResetInterruptedDetections();
                await Run(python,script,model,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){log.LogWarning("Detection worker unavailable ({Type}); retrying",e.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
    async Task Run(string python,string script,string model,CancellationToken ct)
    {
        var start=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var a in new[]{"-u",script,"--model",model,"--ffmpeg",paths.Ffmpeg,"--device",config["Dvr:DetectionDevice"]??"GPU"})start.ArgumentList.Add(a);
        using var process=new Process{StartInfo=start};process.Start();
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
                if(recording is null){await Task.Delay(3000,ct);continue;}
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
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested){store.SaveDetection(recording.Id,new("pending"));throw;}
                catch{store.SaveDetection(recording.Id,new("error",Error:"worker_failed"));throw;}
                await Task.Delay(1000,ct);
            }
        }
        finally
        {
            try{if(!process.HasExited)process.Kill(true);}catch{}
            await process.WaitForExitAsync(CancellationToken.None);
            job?.Dispose();
            try{await drain;}catch(OperationCanceledException){}
        }
    }
}
