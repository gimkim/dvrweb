using System.Diagnostics;
using System.Text.Json;

namespace GimDvr;

// Caller serializes requests. Lazy startup keeps the NAS detector dormant while remote is healthy.
public sealed class DetectionProcess(string python,string script,string model,string ffmpeg,string device="GPU",bool cuda=false,Action<string,object>? events=null,string? cudaDirectory=null,string decoder="cuda"):IAsyncDisposable
{
    Process? process;
    IDisposable? job;
    Task? drain;
    public async Task<JsonElement> Request(object request,CancellationToken ct)
    {
        try
        {
            if(process is null)
            {
                var start=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var a in new[]{"-u",script,"--model",model,"--ffmpeg",ffmpeg,"--device",device})start.ArgumentList.Add(a);
                if(cuda)foreach(var a in new[]{"--backend","onnx","--decoder",decoder,"--readrate","0"})start.ArgumentList.Add(a);
                if(cuda&&!string.IsNullOrWhiteSpace(cudaDirectory))
                {
                    start.Environment["MOTION_CUDA_DLL_DIRECTORY"]=cudaDirectory;
                    start.Environment.TryGetValue("PATH",out var inheritedPath);
                    start.Environment["PATH"]=cudaDirectory+Path.PathSeparator+inheritedPath;
                }
                process=new(){StartInfo=start};process.Start();
                drain=process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                job=ProcessJob.Attach(process);
                try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
                events?.Invoke("process_start",new{childPid=process.Id});
            }
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),ct);
            await process.StandardInput.FlushAsync(ct);
            var line=await process.StandardOutput.ReadLineAsync(ct)??throw new IOException("Detector exited");
            if(line.Length>65536)throw new IOException("Detector response too large");
            return JsonSerializer.Deserialize<JsonElement>(line);
        }
        catch{await DisposeAsync();throw;}
    }
    public async ValueTask DisposeAsync()
    {
        if(process is null)return;
        var old=process;process=null;
        try{if(!old.HasExited)old.Kill(true);}catch{}
        job?.Dispose();job=null;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try{await old.WaitForExitAsync(timeout.Token);events?.Invoke("process_stop",new{childPid=old.Id,exitCode=old.ExitCode});}catch{}
        if(drain is not null)try{await drain.WaitAsync(timeout.Token);}catch{}
        old.Dispose();drain=null;
    }
}
