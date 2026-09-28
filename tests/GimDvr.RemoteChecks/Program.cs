using GimDvr;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

var key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dvr:DetectionRemoteUrl"]="https://fixture.invalid/MotionService/",["Dvr:DetectionRemoteKey"]=key}).Build();
var root=Path.GetFullPath("artifacts/remote-checks");Directory.CreateDirectory(root);var path=Path.Combine(root,"fixture.bin");await File.WriteAllBytesAsync(path,[1,2,3]);
var clip=new Recording("fixture","camera","Fixture",path,DateTimeOffset.UtcNow,6,3);
var events=new List<string>();var fake=new Fake(key);
using var remote=new RemoteDetection(config,(name,_)=>events.Add(name),fake);
Check(!remote.Available&&await remote.Analyze(clip,default) is null,"unavailable routes to local without uploading");
await remote.Probe(default);Check(remote.Available,"health accepts authenticated ready protocol1");
var result=await remote.Analyze(clip,default);Check(result is {State:"complete",Device:"CUDA"}&&fake.Bytes==3,"successful streamed upload preserves CUDA result");
fake.Mode="fail";Check(await remote.Analyze(clip,default) is null&&!remote.Available&&events.Contains("remote_fallback"),"remote failure returns same-job local fallback and marks offline");
fake.Mode="ok";await remote.Probe(default);Check(remote.Available,"later poll restores remote eligibility");
fake.Mode="partial";Check(await remote.Analyze(clip,default) is null,"partial remote output does not become negative or completed");
fake.Mode="ok";await remote.Probe(default);fake.Mode="cancel";using(var cancel=new CancellationTokenSource(30))
{try{await remote.Analyze(clip,cancel.Token);throw new Exception("Cancellation swallowed");}catch(OperationCanceledException){Console.WriteLine("PASS owner cancellation does not start local fallback");}}
fake.Mode="wrongProtocol";await remote.Probe(default);Check(!remote.Available,"wrong protocol rejected");
fake.Mode="unauthorized";await remote.Probe(default);Check(!remote.Available,"unauthorized service rejected");
using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=fixture",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddDays(1));var pin=Convert.ToHexString(SHA256.HashData(cert.RawData));
Check(RemoteDetection.TrustedCertificate(cert,SslPolicyErrors.RemoteCertificateChainErrors,pin)&&!RemoteDetection.TrustedCertificate(cert,SslPolicyErrors.None,new string('0',64)),"exact certificate pin required even when normal chain is valid");
Check(!RemoteDetection.TrustedCertificate(cert,SslPolicyErrors.RemoteCertificateChainErrors,null),"untrusted TLS is rejected without configured pin");
void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
sealed class Fake(string key):HttpMessageHandler
{
    public string Mode="ok";public int Bytes;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(request.Headers.Authorization?.ToString()!="Bearer "+key)throw new Exception("Missing key");
        if(request.RequestUri!.AbsolutePath.EndsWith("health"))return new(Mode=="unauthorized"?HttpStatusCode.Unauthorized:HttpStatusCode.OK){Content=new StringContent(Mode=="wrongProtocol"?"{\"ready\":true,\"protocol\":2}":"{\"ready\":true,\"protocol\":1}",Encoding.UTF8,"application/json")};
        if(request.RequestUri.AbsolutePath!="/MotionService/analyze"||request.RequestUri.Query!="?duration=6")throw new Exception("Wrong route");
        if(Mode=="cancel")await Task.Delay(Timeout.Infinite,ct);
        if(Mode=="fail")return new(HttpStatusCode.ServiceUnavailable);
        Bytes=(await request.Content!.ReadAsByteArrayAsync(ct)).Length;
        return new(HttpStatusCode.OK){Content=new StringContent(Mode=="partial"?"{\"state\":\"partial\"}":"{\"state\":\"complete\",\"motion\":false,\"human\":false,\"frames\":12,\"humanSamples\":2,\"device\":\"CUDA\"}",Encoding.UTF8,"application/json")};
    }
}
