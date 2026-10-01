using GimDvr;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

var root=Path.GetFullPath("artifacts/network-checks-"+Guid.NewGuid().ToString("N"));
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DataRoot"]=root}).Build();
var store=new Store(new Paths(config,new Env(root)),new EphemeralDataProtectionProvider());int passed=0;
void Check(bool yes,string name){if(!yes)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
Camera Create()=>store.SaveCamera(new Camera{Id="camera",Host="192.168.1.36",Uid="TEST",MacAddress="D4:8A:3B:66:78:83",RecordingEnabled=true,RecordingRoot=Path.Combine(root,"recordings")},"private-fixture");
Check(CameraNetworkDiscovery.NormalizeMac("d4-8a-3b-66-78-83")=="D48A3B667883","MAC canonical comparison accepts colon/hyphen/case");
Check(CameraNetworkDiscovery.NormalizeMac("bad")==null,"invalid MAC rejected");
byte[] reply=new byte[124];reply[0]=0x44;reply[1]=0x48;reply[2]=1;reply[3]=8;Encoding.ASCII.GetBytes("192.168.1.20").CopyTo(reply,4);reply[90]=95;reply[91]=156;Encoding.ASCII.GetBytes("TEST").CopyTo(reply,92);
Check(CameraNetworkDiscovery.ParseCandidate(reply,IPAddress.Parse("192.168.1.20"),"TEST")==("192.168.1.20",40031),"UID and sender validate candidate independent of old IP");
Check(CameraNetworkDiscovery.ParseCandidate(reply,IPAddress.Parse("192.168.1.21"),"TEST")==null,"reject spoofed embedded address");
Check(CameraNetworkDiscovery.ParseCandidate(reply,IPAddress.Parse("192.168.1.20"),"OTHER")==null,"reject different UID");
reply[90]=reply[91]=0;Check(CameraNetworkDiscovery.ParseCandidate(reply,IPAddress.Parse("192.168.1.20"),"TEST")==null,"reject zero port");
var original=Create();var n=new Fake(store){Reach=true,OldMac=original.MacAddress};var result=await n.Resolve(original,default);
Check(result.Host==original.Host&&n.Searches==0,"reachable matching original MAC avoids discovery");
original=Create();n=new Fake(store);result=await n.Resolve(original,default);
Check(result.Host=="192.168.1.20"&&store.Camera("camera").Host==result.Host,"unreachable old IP recovers by matching MAC and persists locally");
Check(result.Secret==original.Secret&&result.RecordingRoot==original.RecordingRoot&&result.RecordingEnabled,"recovery preserves encrypted credentials and recording configuration");
await n.Resolve(original,default);Check(n.Searches==1,"recovered address cached to bound repeated probes");
original=Create();n=new Fake(store){Reach=true,OldMac="00:11:22:33:44:55"};result=await n.Resolve(original,default);Check(result.Host=="192.168.1.20","old IP reused by different MAC cannot impersonate camera");
original=Create();n=new Fake(store){NewMac="00:11:22:33:44:55"};result=await n.Resolve(original,default);Check(result.Host==original.Host,"wrong candidate MAC leaves configuration untouched");
original=Create();n=new Fake(store){OnSearch=()=>store.SaveCamera(store.Camera("camera") with{Name="Admin edit",Host="192.168.1.22"},null)};result=await n.Resolve(original,default);Check(result.Host=="192.168.1.22"&&result.Name=="Admin edit","concurrent administrator edits are preserved");
original=store.SaveCamera(Create() with{Enabled=false},null);n=new Fake(store);result=await n.Resolve(original,default);Check(n.Searches==0&&n.Probes==0,"disabled cameras are never probed");
original=store.SaveCamera(Create() with{MacAddress=null},null);n=new Fake(store);await n.Resolve(original,default);Check(n.Probes==0,"legacy cameras without MAC unchanged");
original=Create();n=new Fake(store);using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await n.Resolve(original,cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Check(store.Camera("camera").Host==original.Host,"cancellation leaves camera unchanged");}}
Console.WriteLine($"{passed} network checks passed");
sealed class Fake(Store store):CameraNetworkDiscovery(store,NullLogger<CameraNetworkDiscovery>.Instance)
{
 public bool Reach;public string? OldMac;public string? NewMac="D48A3B667883";public int Searches,Probes;public Action? OnSearch;
 protected override Task<bool> Reachable(Camera c,CancellationToken ct){Probes++;return Task.FromResult(c.Host=="192.168.1.20"||Reach);}
 protected override Task<string?> ReadMac(string host,CancellationToken ct)=>Task.FromResult(host=="192.168.1.20"?NewMac:OldMac);
 protected override Task<List<(string Host,int Port)>> Discover(Camera c,CancellationToken ct){Searches++;OnSearch?.Invoke();return Task.FromResult(new List<(string,int)>{("192.168.1.20",40031)});}
}
sealed class Env(string root):IWebHostEnvironment
{
 public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="Checks";public string WebRootPath{get;set;}=root;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
