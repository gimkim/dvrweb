using GimDvr;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder=Host.CreateApplicationBuilder(new HostApplicationBuilderSettings{Args=args,ContentRootPath=AppContext.BaseDirectory});
builder.Configuration["Dvr:WorkerProcess"]="true";
builder.Services.AddWindowsService(o=>o.ServiceName="GimDvrRecorder");
var env=new WorkerEnvironment{ContentRootPath=AppContext.BaseDirectory};
var paths=new Paths(builder.Configuration,env);
builder.Services.AddSingleton(paths);
builder.Services.AddDataProtection().SetApplicationName("GimDvr").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Data,"keys")));
builder.Services.AddSingleton<Store>();builder.Services.AddSingleton<CameraClient>();builder.Services.AddHostedService<MediaService>();
builder.Services.AddHostedService<DetectionService>();
builder.Services.Configure<HostOptions>(o=>o.ShutdownTimeout=TimeSpan.FromSeconds(35));
await builder.Build().RunAsync();

sealed class WorkerEnvironment:IWebHostEnvironment
{
 public string ApplicationName{get;set;}="GimDvr";public string EnvironmentName{get;set;}="Production";
 public string ContentRootPath{get;set;}="";public string WebRootPath{get;set;}="";
 public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
