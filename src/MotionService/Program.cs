using GimDvr;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder=WebApplication.CreateBuilder(args);
var data=builder.Configuration["Motion:DataRoot"]??@"C:\Users\tatsa\web-data\MotionService";
builder.Configuration.AddJsonFile(Path.Combine(data,"service.json"),optional:false,reloadOnChange:false);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=256L*1024*1024);
builder.Services.Configure<IISServerOptions>(o=>o.MaxRequestBodySize=256L*1024*1024);
builder.Services.AddSingleton<Engine>();builder.Services.AddHostedService(sp=>sp.GetRequiredService<Engine>());
builder.Logging.AddFilter("Microsoft.AspNetCore",LogLevel.Warning);
var app=builder.Build();
var expected=builder.Configuration["Motion:ApiKey"]??throw new InvalidOperationException("Missing service key");
if(expected.Length<32)throw new InvalidOperationException("Service key too short");
app.Use(async(ctx,next)=>
{
    var supplied=ctx.Request.Headers.Authorization.ToString();
    if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),SHA256.HashData(Encoding.UTF8.GetBytes("Bearer "+expected)))){ctx.Response.StatusCode=401;return;}
    ctx.Response.Headers.CacheControl="no-store";await next();
});
app.MapGet("/health",(Engine engine)=>Results.Json(new{ready=engine.Ready,protocol=1,device=engine.Device},statusCode:engine.Ready?200:503));
app.MapPost("/analyze",async(HttpContext ctx,Engine engine)=>
{
    if(!double.TryParse(ctx.Request.Query["duration"],NumberStyles.Float,CultureInfo.InvariantCulture,out var duration)||!double.IsFinite(duration)||duration<=0||duration>180)return Results.BadRequest(new{error="invalid_duration"});
    if(ctx.Request.ContentType!="application/octet-stream")return Results.StatusCode(415);
    if(ctx.Request.ContentLength is null or <=0 or >256L*1024*1024)return Results.StatusCode(413);
    if(!engine.Ready)return Results.StatusCode(503);
    if(!await engine.Gate.WaitAsync(0,ctx.RequestAborted))return Results.StatusCode(429);
    var temporary=Path.Combine(engine.Incoming,Guid.NewGuid().ToString("N")+".mp4");
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);timeout.CancelAfter(TimeSpan.FromSeconds(110));
    var watch=Stopwatch.StartNew();
    try
    {
        await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
        {
            var buffer=new byte[65536];long total=0;int count;
            while((count=await ctx.Request.Body.ReadAsync(buffer,timeout.Token))>0)
            {total+=count;if(total>256L*1024*1024)return Results.StatusCode(413);await file.WriteAsync(buffer.AsMemory(0,count),timeout.Token);}
        }
        var result=await engine.Analyze(temporary,duration,timeout.Token);
        engine.Events.Write("clip_finish",new{videoSeconds=duration,elapsedSeconds=watch.Elapsed.TotalSeconds,result});
        return Results.Json(result);
    }
    catch(Exception e)
    {
        engine.Ready=false;engine.Events.Write("clip_error",new{errorType=e.GetType().Name,elapsedSeconds=watch.Elapsed.TotalSeconds});
        return Results.StatusCode(503);
    }
    finally{try{File.Delete(temporary);}catch{}engine.Gate.Release();}
});
await app.RunAsync();

sealed class Engine:IHostedService,IAsyncDisposable
{
    public readonly SemaphoreSlim Gate=new(1,1);
    public readonly string Incoming;
    public readonly DetectionLog Events;
    public volatile bool Ready;
    public string? Device;
    readonly DetectionProcess detector;
    readonly CancellationTokenSource stop=new();
    Task? health;
    public Engine(IConfiguration config,ILogger<Engine> log)
    {
        var data=config["Motion:DataRoot"]??@"C:\Users\tatsa\web-data\MotionService";
        var runtime=config["Motion:RuntimeRoot"]??Path.Combine(data,"runtime");Incoming=Path.Combine(data,"incoming");Directory.CreateDirectory(Incoming);
        foreach(var file in Directory.GetFiles(Incoming,"*.mp4"))try{if(File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddHours(-1))File.Delete(file);}catch{}
        Events=new(Path.Combine(data,"logs"),log);
        detector=new(Path.Combine(runtime,"python.exe"),Path.Combine(AppContext.BaseDirectory,"Detection","detect.py"),Path.Combine(runtime,"models","yolox_tiny.onnx"),Path.Combine(runtime,"tools","ffmpeg.exe"),config["Motion:Device"]??"CUDA",true,Events.Write,config["Motion:CudaDllDirectory"],config["Motion:Decoder"]??"cuda");
    }
    public Task StartAsync(CancellationToken ct){health=Maintain();return Task.CompletedTask;}
    async Task Maintain()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                if(!Ready&&await Gate.WaitAsync(0,stop.Token))
                {
                    try
                    {
                        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(60));
                        var result=await detector.Request(new{kind="health"},timeout.Token);
                        Ready=result.GetProperty("ready").GetBoolean();Device=result.GetProperty("device").GetString();
                        Events.Write("engine_health",new{ready=Ready,device=Device});
                        if(!Ready)await detector.DisposeAsync();
                    }
                    catch(Exception e){Ready=false;Events.Write("engine_error",new{errorType=e.GetType().Name});}
                    finally{Gate.Release();}
                }
                await Task.Delay(TimeSpan.FromSeconds(15),stop.Token);
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
    }
    public async Task<DetectionResult> Analyze(string path,double duration,CancellationToken ct)
    {
        var result=await detector.Request(new{path,duration},ct);
        var parsed=result.Deserialize<DetectionResult>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new IOException("Invalid result");
        Device=parsed.Device;
        if(parsed.Error=="model_unavailable"||parsed.Error=="inference_failed"){Ready=false;await detector.DisposeAsync();}
        return parsed;
    }
    public async Task StopAsync(CancellationToken ct){stop.Cancel();if(health is not null)await health.WaitAsync(ct);await Gate.WaitAsync(ct);try{await detector.DisposeAsync();}finally{Gate.Release();}}
    public async ValueTask DisposeAsync(){stop.Cancel();await detector.DisposeAsync();stop.Dispose();}
}
