using GimDvr;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

if(args.Length!=2)throw new ArgumentException("runtime directory and ffmpeg required; synthetic fixture only");
var root=Path.GetFullPath("artifacts/detection-integration-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(root);
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DataRoot"]=root,["Dvr:DetectionRuntime"]=Path.GetFullPath(args[0]),["Dvr:Ffmpeg"]=args[1]}).Build();
var paths=new Paths(config,new Env(root));var store=new Store(paths,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));
var clip=Path.Combine(root,"synthetic.mp4");
using(var ffmpeg=new Process{StartInfo=new(args[1]){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}})
{
 foreach(var a in new[]{"-hide_banner","-loglevel","error","-f","lavfi","-i","color=c=black:s=640x360:r=10","-t","6","-c:v","libx264","-pix_fmt","yuv420p",clip})ffmpeg.StartInfo.ArgumentList.Add(a);
 ffmpeg.Start();var stderr=ffmpeg.StandardError.ReadToEndAsync();await ffmpeg.WaitForExitAsync();if(ffmpeg.ExitCode!=0)throw new Exception(await stderr);
}
var recording=new Recording("synthetic","test","Synthetic",clip,DateTimeOffset.UtcNow,6,new FileInfo(clip).Length);store.AddRecording(recording);
using var service=new DetectionService(store,paths,config,NullLogger<DetectionService>.Instance);
await service.StartAsync(CancellationToken.None);
DetectionResult? result=null;
try
{
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
 while(true){result=store.WithDetection([recording]).Single().Detection;if(result?.State is "complete" or "error" or "partial")break;await Task.Delay(250,timeout.Token);}
}
finally{using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(15));await service.StopAsync(stop.Token);}
if(result is not {State:"complete",Motion:false,Human:false,Frames:12,HumanSamples:2})throw new Exception("Unexpected synthetic result: "+System.Text.Json.JsonSerializer.Serialize(result));
Console.WriteLine("PASS FFmpeg -> actual OpenVINO -> JSON worker -> SQLite -> recording projection: "+result.Device+" / "+result.Decoder);
using(var gate=File.Open(Path.Combine(paths.Runtime,"detection.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Console.WriteLine("PASS shutdown releases analysis owner lock");
Console.WriteLine("Evidence: "+root);

sealed class Env(string root):IWebHostEnvironment
{
 public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="DetectionChecks";
 public string WebRootPath{get;set;}=root;public string ContentRootPath{get;set;}=root;
 public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
