using System.Collections.Concurrent;
namespace GimDvr;

// Latest desired state wins. There is no movement-command queue to replay later.
public sealed class PtzService(CameraClient client,ILogger<PtzService> log):BackgroundService
{
    sealed class State(Camera camera)
    {
        public readonly object Gate=new();
        public Camera Camera=camera;
        public string Token="",Owner="";
        public int Desired=-1,Applied=-1;
        public DateTimeOffset Until;
        public Task Work=Task.CompletedTask;
        public readonly Dictionary<string,DateTimeOffset> Closed=new();
    }
    readonly ConcurrentDictionary<string,State> states=new();
    public bool Set(Camera c,string owner,PtzInput input)
    {
        if(!Guid.TryParse(input.Token,out _))throw new ArgumentException("PTZ token ไม่ถูกต้อง");
        var direction=input.Direction switch{"up"=>0,"down"=>2,"left"=>4,"right"=>6,"stop" or "halt"=>-1,_=>throw new ArgumentException("ทิศทางไม่ถูกต้อง")};
        var s=states.GetOrAdd(c.Id,_=>new(c));
        lock(s.Gate)
        {
            var now=DateTimeOffset.UtcNow;
            foreach(var token in s.Closed.Where(x=>x.Value<now).Select(x=>x.Key).ToArray())s.Closed.Remove(token);
            if(direction<0)
            {
                if(input.Direction=="halt"){s.Closed[s.Token]=now.AddMinutes(2);s.Desired=-1;s.Until=now;}
                s.Closed[input.Token]=now.AddMinutes(2);
                if(s.Token==input.Token&&s.Owner==owner){s.Desired=-1;s.Until=now;}
                return true;
            }
            if(s.Closed.ContainsKey(input.Token))return false; // release may arrive before delayed start
            if(s.Until>now&&s.Token!=input.Token)return false;
            s.Camera=c;s.Token=input.Token;s.Owner=owner;s.Desired=direction;s.Until=now.AddMilliseconds(650);
            return true;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                foreach(var s in states.Values)
                lock(s.Gate)
                {
                    if(s.Desired>=0&&s.Until<=DateTimeOffset.UtcNow){s.Closed[s.Token]=DateTimeOffset.UtcNow.AddMinutes(2);s.Desired=-1;}
                    if(s.Work.IsCompleted&&s.Desired!=s.Applied)s.Work=Apply(s,ct);
                }
                await Task.Delay(20,ct);
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        finally
        {
            await Task.WhenAll(states.Values.Select(s=>s.Work));
            foreach(var s in states.Values){lock(s.Gate)s.Desired=-1;await Apply(s,CancellationToken.None);}
        }
    }
    async Task Apply(State s,CancellationToken ct)
    {
        int previous;Camera camera;
        lock(s.Gate){previous=s.Applied;camera=s.Camera;}
        try
        {
            if(previous>=0)
            {
                using var timeout=new CancellationTokenSource(700);
                await client.PtzCommand(camera,previous+1,timeout.Token);
                lock(s.Gate)s.Applied=-1;
            }
            int desired;
            lock(s.Gate)desired=s.Until>DateTimeOffset.UtcNow?s.Desired:-1;
            if(desired<0||ct.IsCancellationRequested)return;
            // Mark before sending, so even a lost response must be followed by Stop.
            lock(s.Gate)s.Applied=desired;
            using var startTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);startTimeout.CancelAfter(700);
            await client.PtzCommand(camera,desired,startTimeout.Token);
        }
        catch(Exception e)
        {
            lock(s.Gate){s.Desired=-1;s.Until=DateTimeOffset.MinValue;s.Closed[s.Token]=DateTimeOffset.UtcNow.AddMinutes(2);}
            log.LogWarning("PTZ {Id}: {Type}; stop will be retried",camera.Id,e.GetType().Name);
        }
    }
}
public sealed record PtzInput(string Direction,string Token);
