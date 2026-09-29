using GimDvr;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Paths>();
builder.Services.AddSingleton<AssetVersions>();
builder.Services.AddHttpsRedirection(o=>o.HttpsPort=443);
var paths=new Paths(builder.Configuration,builder.Environment);
builder.Configuration.AddJsonFile(Path.Combine(paths.Data,"live-clock.json"),optional:true,reloadOnChange:false);
builder.Configuration.AddJsonFile(Path.Combine(paths.Data,"detection-tuning.json"),optional:true,reloadOnChange:true);
if(OperatingSystem.IsWindows())Console.WriteLine($"GimDVR running as {System.Security.Principal.WindowsIdentity.GetCurrent().Name}; pool={Environment.GetEnvironmentVariable("APP_POOL_ID")??"standalone"}");
Directory.CreateDirectory(paths.Data);
builder.Services.AddDataProtection().SetApplicationName("GimDvr").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Data,"keys")));
builder.Services.AddSingleton<Store>();builder.Services.AddSingleton<CameraClient>();
builder.Services.AddSingleton<PtzService>();builder.Services.AddHostedService(sp=>sp.GetRequiredService<PtzService>());
builder.Services.AddSingleton<MediaService>();builder.Services.AddHostedService(sp=>sp.GetRequiredService<MediaService>());
builder.Services.AddHostedService<DetectionService>();
builder.Services.AddSingleton<WebRtcService>();builder.Services.AddHostedService(sp=>sp.GetRequiredService<WebRtcService>());
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o=>
{
    o.Cookie.Name="GimDvr.Session";o.Cookie.Path=builder.Configuration["Dvr:PathBase"]??"/gimdvr";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;o.Cookie.SecurePolicy=builder.Environment.IsDevelopment()?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;
    o.ExpireTimeSpan=TimeSpan.FromDays(14);o.SlidingExpiration=true;
    o.Events.OnRedirectToLogin=ctx=>{ctx.Response.StatusCode=401;return Task.CompletedTask;};
    o.Events.OnRedirectToAccessDenied=ctx=>{ctx.Response.StatusCode=403;return Task.CompletedTask;};
    o.Events.OnValidatePrincipal=ctx=>
    {
        var row=ctx.HttpContext.RequestServices.GetRequiredService<Store>().Users().Find(u=>u.Id==ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
        if(row is null||!row.Enabled||row.Stamp!=ctx.Principal?.FindFirstValue("stamp"))ctx.RejectPrincipal();
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(o=>{o.AddPolicy("admin",p=>p.RequireRole("admin"));o.AddPolicy("control",p=>p.RequireRole("admin","operator"));});
builder.Services.AddRateLimiter(o=>
{
    o.RejectionStatusCode=429;
    o.AddPolicy("login",ctx=>RateLimitPartition.GetSlidingWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new(){PermitLimit=15,Window=TimeSpan.FromMinutes(1),SegmentsPerWindow=6,QueueLimit=0}));
});
builder.Services.Configure<HostOptions>(o=>o.ShutdownTimeout=TimeSpan.FromSeconds(35));
var app=builder.Build();
if(builder.Configuration.GetValue<bool>("Dvr:InitializeOnly"))
{
    app.Services.GetRequiredService<Store>();
    Console.WriteLine("GimDVR data initialized. Recording was not started.");
    return;
}
var pathBase=builder.Configuration["Dvr:PathBase"]??"/gimdvr";
app.Use(async(ctx,next)=>
{
    if(ctx.Request.PathBase==""&&ctx.Request.Path.StartsWithSegments(pathBase,out var rest)){ctx.Request.PathBase=pathBase;ctx.Request.Path=rest;}
    if(ctx.Request.Path==""){ctx.Response.Redirect(ctx.Request.PathBase+"/");return;}
    ctx.Response.Headers["X-Content-Type-Options"]="nosniff";
    ctx.Response.Headers["Referrer-Policy"]="no-referrer";
    ctx.Response.Headers["X-Frame-Options"]="DENY";
    ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'none'";
    if(ctx.Request.Path.StartsWithSegments("/api"))ctx.Response.Headers.CacheControl="no-store";
    if(ctx.Request.Path.StartsWithSegments("/api")&&!HttpMethods.IsGet(ctx.Request.Method)&&!HttpMethods.IsHead(ctx.Request.Method))
    {
        if(ctx.Request.Headers["X-DVR-Request"]!="1"){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error="Missing request header"});return;}
        if(ctx.Request.Headers.TryGetValue("Origin",out var origin)&&origin.ToString()!=$"{ctx.Request.Scheme}://{ctx.Request.Host}"){ctx.Response.StatusCode=403;return;}
    }
    try{await next(ctx);}
    catch(Exception e) when(e is ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException or UnauthorizedAccessException or IOException)
    {
        if(ctx.Response.HasStarted)throw;
        ctx.Response.StatusCode=e switch{KeyNotFoundException=>404,NotSupportedException=>422,UnauthorizedAccessException=>403,IOException=>503,_=>400};
        await ctx.Response.WriteAsJsonAsync(new{error=e is IOException?"อ่าน/เขียนไฟล์ไม่สำเร็จ ตรวจ path และสิทธิ์ของเซิร์ฟเวอร์":e.Message});
    }
    catch(OperationCanceledException) when(ctx.RequestAborted.IsCancellationRequested){}
    catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
    {
        if(ctx.Response.HasStarted)throw;
        ctx.Response.StatusCode=504;await ctx.Response.WriteAsJsonAsync(new{error="ติดต่อกล้องไม่ได้หรือหมดเวลารอ ตรวจ IP พอร์ต และการเชื่อมต่อ LAN"});
    }
});
if(!app.Environment.IsDevelopment())app.UseHttpsRedirection();
app.UseDefaultFiles();
app.Use(async(ctx,next)=>
{
    if(ctx.Request.Path=="/index.html"&&(HttpMethods.IsGet(ctx.Request.Method)||HttpMethods.IsHead(ctx.Request.Method)))
    {
        ctx.Response.Headers.CacheControl="no-store";ctx.Response.ContentType="text/html; charset=utf-8";
        var html=await ctx.RequestServices.GetRequiredService<AssetVersions>().Index(ctx.RequestAborted);
        if(!HttpMethods.IsHead(ctx.Request.Method))await ctx.Response.WriteAsync(html,ctx.RequestAborted);
        return;
    }
    if(ctx.Request.Path.StartsWithSegments("/api"))ctx.Response.Headers.CacheControl="no-store";
    await next();
});
var staticTypes=new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
staticTypes.Mappings[".apk"]="application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions{ContentTypeProvider=staticTypes,OnPrepareResponse=ctx=>ctx.Context.Response.Headers.CacheControl="no-cache"});app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();app.UseWebSockets();
app.MapGet("/health",()=>Results.Ok(new{status="ok",app="GimDvr",version="1.10.5"}));
app.MapPost("/api/login",async(LoginInput input,HttpContext ctx,Store store)=>
{
    if(input.Username.Length>64)return Results.BadRequest(new{error="ข้อมูลไม่ถูกต้อง"});
    var key=input.Username.Trim().ToLowerInvariant();
    if(!store.LoginAllowed(key))return Results.Json(new{error="ลองรหัสผิดหลายครั้ง กรุณารอ 5 นาที"},statusCode:429);
    var user=store.Users().Find(x=>x.Username.Equals(key,StringComparison.OrdinalIgnoreCase));
    var valid=user is not null&&user.Enabled&&Store.Verify(input.Password,user.Hash);store.LoginResult(key,valid);
    if(!valid)return Results.Json(new{error="ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง"},statusCode:401);
    var claims=new[]{new Claim(ClaimTypes.NameIdentifier,user!.Id),new Claim(ClaimTypes.Name,user.Username),new Claim(ClaimTypes.Role,user.Role),new Claim("stamp",user.Stamp)};
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)),new AuthenticationProperties{IsPersistent=true,AllowRefresh=true,ExpiresUtc=input.RememberDevice?DateTimeOffset.UtcNow.AddYears(10):null});
    store.Audit(user.Username,"login",ctx.Connection.RemoteIpAddress?.ToString()??"");return Results.Ok(new{user.Username,user.Role});
}).RequireRateLimiting("login");
app.MapPost("/api/logout",async(HttpContext ctx)=>{await ctx.SignOutAsync();return Results.Ok();}).RequireAuthorization();
app.MapGet("/api/me",(HttpContext ctx)=>new{id=ctx.User.FindFirstValue(ClaimTypes.NameIdentifier),username=ctx.User.Identity!.Name,role=ctx.User.FindFirstValue(ClaimTypes.Role)}).RequireAuthorization();
app.MapPost("/api/password",(PasswordChange input,HttpContext ctx,Store store)=>
{
    if(string.IsNullOrEmpty(input.NewPassword))return Results.BadRequest(new{error="กรุณาระบุรหัสผ่าน"});
    var user=store.Users().Single(u=>u.Id==ctx.User.FindFirstValue(ClaimTypes.NameIdentifier));
    if(!Store.Verify(input.Current,user.Hash))return Results.BadRequest(new{error="รหัสปัจจุบันไม่ถูกต้อง"});
    store.SaveUser(user.Id,new(user.Username,user.Role,user.Enabled,input.NewPassword));store.Audit(user.Username,"password.change","self");
    var bootstrap=Path.Combine(paths.Data,"bootstrap.txt");if(user.Username=="admin"&&File.Exists(bootstrap))File.Delete(bootstrap);
    return Results.Ok();
}).RequireAuthorization();
object PublicCamera(Camera c,MediaService media)=>new{c.Id,c.Name,c.Driver,c.Host,c.HttpPort,c.RtspPort,c.RtspPath,c.Username,c.Uid,c.Enabled,c.RecordingEnabled,c.RecordingRoot,c.RetentionDays,c.Revision,c.TalkMode,status=media.Status(c.Id)};
app.MapGet("/api/cameras",(Store s,MediaService m)=>s.Cameras().Select(c=>PublicCamera(c,m))).RequireAuthorization();
app.MapPost("/api/cameras",(CameraInput input,Store s,MediaService media,HttpContext ctx)=>
{
    if(input.Camera.RecordingRoot.Length>0)media.ValidateRecordingRoot(input.Camera.RecordingRoot);
    var c=s.SaveCamera(input.Camera,input.Password);s.Audit(ctx.User.Identity!.Name!,"camera.save",c.Name);return Results.Ok(PublicCamera(c,media));
}).RequireAuthorization("admin");
app.MapDelete("/api/cameras/{id}",(string id,Store s,HttpContext ctx)=>{var c=s.Camera(id);s.DeleteCamera(id);s.Audit(ctx.User.Identity!.Name!,"camera.remove",c.Name);return Results.Ok(new{message="ลบรายการกล้องแล้ว ไฟล์บันทึกเดิมยังอยู่"});}).RequireAuthorization("admin");
app.MapGet("/api/cameras/{id}/state",async(string id,Store s,CameraClient camera,CancellationToken ct)=>Results.Ok(await camera.State(s.Camera(id),ct))).RequireAuthorization();
app.MapGet("/api/cameras/{id}/snapshot",async(string id,Store s,MediaService media,CancellationToken ct)=>Results.File(await media.Snapshot(s.Camera(id),ct),"image/jpeg")).RequireAuthorization();
app.MapPost("/api/cameras/{id}/control",async(string id,ControlInput input,Store s,CameraClient camera,HttpContext ctx,CancellationToken ct)=>
{
    await camera.Control(s.Camera(id),input,ct);s.Audit(ctx.User.Identity!.Name!,"camera.control",id+":"+input.Action+":"+input.Value);return Results.Ok(new{accepted=true});
}).RequireAuthorization("control");
app.MapPost("/api/cameras/{id}/watch",(string id,string? mode,Store s,MediaService media)=>{var c=s.Camera(id);if(!c.Enabled)return Results.BadRequest(new{error="กล้องถูกปิดใช้งาน"});if(mode is not null and not "overview" and not "focus")return Results.BadRequest();media.Watch(id,mode=="focus");return Results.Ok(media.Status(id));}).RequireAuthorization();
app.MapPost("/api/cameras/{id}/ptz",(string id,PtzInput input,Store s,PtzService ptz,HttpContext ctx)=>
{
    var c=s.Camera(id);if(!c.Enabled||c.Driver!="vstarcam")return Results.BadRequest(new{error="กล้องไม่พร้อมควบคุม"});
    return ptz.Set(c,ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!,input)?Results.Ok(new{accepted=true}):Results.Conflict(new{error="การกดสิ้นสุดแล้วหรือมีผู้ควบคุมกล้องอยู่"});
}).RequireAuthorization("control");
app.MapGet("/api/cameras/{id}/talk-capability",(string id,Store s)=>
{
    var camera=s.Camera(id);
    return Results.Ok(new{supported=false,reason=camera.Driver=="vstarcam"?"ยังส่งเสียงออกลำโพงผ่านเว็บไม่ได้: กล้องชุดนี้ไม่ประกาศ RTSP audio backchannel และต้องใช้โปรโตคอลเสียงเฉพาะ VStarcam":"ยังไม่มีไดรเวอร์ส่งเสียงย้อนกลับสำหรับกล้องนี้"});
}).RequireAuthorization("control");
app.MapPost("/api/cameras/{id}/webrtc",async(string id,WebRtcOffer offer,Store s,WebRtcService rtc,HttpContext ctx)=>{
    var camera=s.Camera(id);if(!camera.Enabled)return Results.NotFound();
    return Results.Ok(await rtc.Create(camera,offer.Sdp,ctx.User,ctx.RequestAborted));
}).RequireAuthorization().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(131072));
app.MapPut("/api/webrtc/{id}",(string id,WebRtcService rtc,HttpContext ctx)=>rtc.Touch(id,ctx.User)?Results.NoContent():Results.NotFound()).RequireAuthorization();
app.MapDelete("/api/webrtc/{id}",async(string id,WebRtcService rtc,HttpContext ctx)=>{await rtc.CloseOwned(id,ctx.User);return Results.NoContent();}).RequireAuthorization();
app.MapGet("/api/cameras/{id}/copy-stream",async(string id,Store s,MediaService media,HttpContext ctx)=>
{
    var camera=s.Camera(id);if(!camera.Enabled){ctx.Response.StatusCode=404;return;}
    await media.CopyStream(camera,ctx);
}).RequireAuthorization();
app.MapGet("/api/live/{id}/{name}",(string id,string name,Store s,MediaService media,HttpContext ctx)=>
{
    ctx.Response.Headers.CacheControl="no-store";
    if(!s.Camera(id).Enabled)return Results.NotFound();var path=media.LiveFile(id,name);
    if(path is null||!File.Exists(path))return Results.NotFound();
    return Results.File(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete),name.EndsWith("m3u8")?"application/vnd.apple.mpegurl":"video/mp2t");
}).RequireAuthorization();
app.MapGet("/api/focus/{id}/{name}",(string id,string name,Store s,MediaService media,HttpContext ctx)=>
{
    ctx.Response.Headers.CacheControl="no-store";
    if(!s.Camera(id).Enabled)return Results.NotFound();var path=media.LiveFile(id,name,true);
    if(path is null||!File.Exists(path))return Results.NotFound();
    return Results.File(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete),name.EndsWith("m3u8")?"application/vnd.apple.mpegurl":"video/mp2t");
}).RequireAuthorization();
app.MapGet("/api/recording-range",(string? camera,DateTimeOffset? from,DateTimeOffset? to,int? offset,Store s)=>
{
    if(string.IsNullOrWhiteSpace(camera)||from is null||to is null||to<=from||offset is <0)return Results.BadRequest(new{error="เลือกกล้องและช่วงเวลาเริ่มก่อนเวลาสิ้นสุด"});
    s.Camera(camera);
    return Results.Ok(s.WithDetection(s.RecordingRange(camera,from.Value,to.Value,offset??0)).Select(r=>new{r.Id,r.CameraId,r.CameraName,r.Start,r.Duration,r.Bytes,r.Detection}));
}).RequireAuthorization();
app.MapGet("/api/recordings",(string? camera,DateTimeOffset? from,DateTimeOffset? to,int? limit,Store s)=>s.WithDetection(s.Recordings(camera,from,to,Math.Clamp(limit??500,1,2000))).Select(r=>new{r.Id,r.CameraId,r.CameraName,r.Start,r.Duration,r.Bytes,r.Detection})).RequireAuthorization();
app.MapGet("/api/recordings/{id}/download",(string id,Store s)=>{var r=s.Recording(id);return File.Exists(r.Path)?Results.File(r.Path,"video/mp4",fileDownloadName:$"recording-{r.Start:yyyyMMdd-HHmmss}.mp4",enableRangeProcessing:true):Results.NotFound();}).RequireAuthorization();
app.MapGet("/api/recordings/{id}/video",(string id,Store s)=>{var r=s.Recording(id);return File.Exists(r.Path)?Results.File(r.Path,"video/mp4",enableRangeProcessing:true):Results.NotFound();}).RequireAuthorization();
app.MapGet("/api/users",(Store s)=>s.Users().Select(u=>new{u.Id,u.Username,u.Role,u.Enabled})).RequireAuthorization("admin");
app.MapPost("/api/users",(UserInput input,Store s,HttpContext ctx)=>{var u=s.SaveUser(null,input);s.Audit(ctx.User.Identity!.Name!,"user.create",u.Username);return Results.Ok(new{u.Id});}).RequireAuthorization("admin");
app.MapPut("/api/users/{id}",(string id,UserInput input,Store s,HttpContext ctx)=>{if(!s.Users().Any(u=>u.Id==id))throw new KeyNotFoundException();var u=s.SaveUser(id,input);s.Audit(ctx.User.Identity!.Name!,"user.update",u.Username);return Results.Ok();}).RequireAuthorization("admin");
app.MapGet("/api/audit",(Store s)=>s.AuditRows()).RequireAuthorization("admin");
app.MapGet("/api/detection-settings",(Store s,IConfiguration config)=>s.DetectionSettings(config)).RequireAuthorization("admin");
app.MapPut("/api/detection-settings",(DetectionSettings value,Store s,HttpContext ctx)=>{try{s.SaveDetectionSettings(value);s.Audit(ctx.User.Identity!.Name!,"detection.settings",System.Text.Json.JsonSerializer.Serialize(value));return Results.Ok(value);}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization("admin");
app.MapGet("/api/stream-settings",(Store s)=>s.StreamSettings()).RequireAuthorization("admin");
app.MapPut("/api/stream-settings",(LiveStreamSettings value,Store s,HttpContext ctx)=>{try{s.SaveStreamSettings(value);s.Audit(ctx.User.Identity!.Name!,"stream.settings",System.Text.Json.JsonSerializer.Serialize(value));return Results.Ok(value);}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization("admin");
app.MapGet("/api/system",(WebRtcService rtc)=>new{webrtc=rtc.Status(),machine=Environment.MachineName,dataRoot=paths.Data,ffmpeg=File.Exists(paths.Ffmpeg),httpsRequiredForMicrophone=true}).RequireAuthorization("admin");
app.Run();
record PasswordChange(string Current,string NewPassword);







