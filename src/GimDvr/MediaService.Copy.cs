using Microsoft.AspNetCore.Http.Features;
using System.Security.Claims;
namespace GimDvr;
public sealed partial class MediaService
{
    string? CopyFolder(string id)
    {
        var overview=paths.ExternalMedia?ReadShared(id)?.Overview:runs.TryGetValue(id,out var run)&&!run.Process.HasExited?run.Overview:null;
        return overview is not null&&Path.GetFullPath(overview).StartsWith(Path.Combine(paths.Live,id)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?Path.Combine(overview,"fragments"):null;
    }
    public async Task CopyStream(Camera camera,HttpContext ctx)
    {
        var ct=ctx.RequestAborted;var deadline=DateTimeOffset.UtcNow.AddSeconds(25);var nextAuth=DateTimeOffset.MinValue;
        string? folder=null;long observed=0,next=0;bool started=false,observedIndex=false;
        ctx.Response.ContentType="application/x-gimdvr-fmp4";ctx.Response.Headers.CacheControl="no-store, no-transform";
        ctx.Response.Headers["X-Accel-Buffering"]="no";ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        try{
            while(!ct.IsCancellationRequested){
                var now=DateTimeOffset.UtcNow;
                if(now>=nextAuth){
                    var user=store.Users().Find(u=>u.Id==ctx.User.FindFirstValue(ClaimTypes.NameIdentifier));
                    if(user is null||!user.Enabled||user.Stamp!=ctx.User.FindFirstValue("stamp")||!store.Camera(camera.Id).Enabled){ctx.Abort();return;}
                    Watch(camera.Id);nextAuth=now.AddSeconds(2);
                }
                var current=CopyFolder(camera.Id);
                if(folder is not null&&current!=folder)throw new IOException("Camera stream restarted");
                if(current is not null){
                    folder=current;var index=FragmentCache.ReadIndex(folder);
                    if(index is not null&&index.Fragments.Length>0){
                        // New viewers wait for the NEXT random-access fragment, never replay an old GOP.
                        if(!observedIndex){observed=index.Fragments[^1].Sequence;observedIndex=true;}
                        if(!started&&FragmentCache.NextKeyframe(index,observed) is {} key){
                            next=key;await FragmentCache.SendPacket(ctx.Response.Body,await FragmentCache.ReadPacket(Path.Combine(folder,"init.mp4"),ct),ct);started=true;
                        }
                        if(started){
                            if(next<index.Fragments[0].Sequence)throw new IOException("Viewer fell behind live cache");
                            foreach(var entry in index.Fragments.Where(f=>f.Sequence>=next)){
                                if(entry.Sequence!=next)throw new IOException("Missing live fragment");
                                await FragmentCache.SendPacket(ctx.Response.Body,await FragmentCache.ReadPacket(FragmentCache.Chunk(folder,next),ct),ct);
                                next++;deadline=DateTimeOffset.UtcNow.AddSeconds(10);
                            }
                        }
                    }
                }
                if(now>deadline)throw new IOException("Timed out waiting for camera keyframe/data");
                await Task.Delay(50,ct);
            }
        }catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(IOException){if(ctx.Response.HasStarted)ctx.Abort();else ctx.Response.StatusCode=503;}
    }
}
