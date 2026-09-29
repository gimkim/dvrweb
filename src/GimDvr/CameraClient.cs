using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace GimDvr;

public partial class CameraClient(Store store)
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> controls = new();
    readonly CameraHttpPorts httpPorts = new();
    public string Rtsp(Camera c) => $"rtsp://{Uri.EscapeDataString(c.Username)}:{Uri.EscapeDataString(store.Password(c))}@{c.Host}:{c.RtspPort}{c.RtspPath}";
    HttpClient Client(Camera c) => new(new HttpClientHandler { Credentials = new NetworkCredential(c.Username, store.Password(c)), PreAuthenticate = true, AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };
    public async Task<string> Get(Camera c,string path,CancellationToken ct)
    {
        if(c.Driver!="vstarcam")throw new NotSupportedException("กล้อง RTSP นี้รองรับดูภาพและบันทึก; การควบคุมต้องใช้ไดรเวอร์ที่รองรับ");
        var port=await httpPorts.Resolve(c,ct,refreshExpired:path=="get_camera_params.cgi");
        using var client=Client(c);
        // Older VStarcam CGI additionally requires these parameters after HTTP authentication.
        var uri=$"http://{c.Host}:{port}/{path}{(path.Contains('?')?'&':'?')}loginuse={Uri.EscapeDataString(c.Username)}&loginpas={Uri.EscapeDataString(store.Password(c))}";
        HttpResponseMessage response;
        try { response=await client.GetAsync(uri,ct); }
        catch(HttpRequestException) { httpPorts.Invalidate(c); throw; }
        catch(OperationCanceledException) { httpPorts.Invalidate(c); throw; }
        using var responseScope=response;
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"กล้องตอบ HTTP {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync(ct);
    }
    [GeneratedRegex("var\\s+(\\w+)\\s*=\\s*([^;]*);",RegexOptions.CultureInvariant)]
    private static partial Regex VariablesRegex();
    public static Dictionary<string,string> Variables(string text)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(Match m in VariablesRegex().Matches(text))result[m.Groups[1].Value]=m.Groups[2].Value.Trim().Trim('"','\'');
        return result;
    }
    public async Task<object> State(Camera c,CancellationToken ct)
    {
        if(c.Driver!="vstarcam")return new{supported=false,reason="กล้อง RTSP ทั่วไป: ดูสดและบันทึก"};
        var camera=Variables(await Get(c,"get_camera_params.cgi",ct));
        var status=Variables(await Get(c,"get_status.cgi",ct));
        var alarm=Variables(await Get(c,"get_params.cgi",ct));
        string? human=null;
        try { var h=await Get(c,"trans_cmd_string.cgi?cmd=2106&command=3",ct);human=Extract(h,"HumanoidDetection"); } catch(InvalidOperationException){}
        return new{supported=true,ptz=true,ir=camera.GetValueOrDefault("ircut"),microphone=camera.GetValueOrDefault("involume"),speaker=camera.GetValueOrDefault("outvolume"),motion=alarm.GetValueOrDefault("alarm_motion_armed"),motionSensitivity=alarm.GetValueOrDefault("alarm_motion_sensitivity"),human,humanSupported=human is not null,firmware=status.GetValueOrDefault("app_version"),talk="ตรวจช่องเสียงย้อนกลับเมื่อกดพูด"};
    }
    static string? Extract(string text,string name)
    {
        var vars=Variables(text);if(vars.TryGetValue(name,out var value))return value;
        var match=Regex.Match(text,$"[\"']?{Regex.Escape(name)}[\"']?\\s*[:=]\\s*[\"']?(\\d+)",RegexOptions.IgnoreCase);
        return match.Success?match.Groups[1].Value:null;
    }
    public async Task Control(Camera c,ControlInput input,CancellationToken ct)
    {
        var gate=controls.GetOrAdd(c.Id,_=>new(1,1));
        if(!await gate.WaitAsync(0,ct))throw new InvalidOperationException("กำลังส่งคำสั่งก่อนหน้า กรุณาลองใหม่");
        try
        {
            string command;
            if(input.Action is "up" or "down" or "left" or "right")
            {
                var code=input.Action switch{"up"=>0,"down"=>2,"left"=>4,_=>6};
                try
                {
                    Check(await Get(c,$"decoder_control.cgi?command={code}&onestep=0",ct));
                    await Task.Delay(80,ct);
                }
                finally
                {
                    using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Get(c,$"decoder_control.cgi?command={code+1}",stop.Token);
                }
                return;
            }
            command=input.Action switch
            {
                "stop"=>"decoder_control.cgi?command=1",
                "center"=>"decoder_control.cgi?command=25",
                "ir" when input.Value is 0 or 1 =>$"camera_control.cgi?param=14&value={input.Value}",
                "microphone" when input.Value is >=0 and <=31 =>$"camera_control.cgi?param=24&value={input.Value}",
                "speaker" when input.Value is >=0 and <=31 =>$"camera_control.cgi?param=25&value={input.Value}",
                "motion" when input.Value is 0 or 1 =>$"set_alarm.cgi?motion_armed={input.Value}",
                "human" when input.Value is 0 or 1 =>await HumanCommand(c,input.Value,ct),
                _=>throw new ArgumentException("คำสั่งหรือค่าไม่ถูกต้อง")
            };
            Check(await Get(c,command,ct));
        }
        finally{gate.Release();}
    }
    public virtual async Task PtzCommand(Camera c,int code,CancellationToken ct)=>Check(await Get(c,$"decoder_control.cgi?command={code}&onestep=0",ct));
    async Task<string> HumanCommand(Camera c,int value,CancellationToken ct)
    {
        var state=await Get(c,"trans_cmd_string.cgi?cmd=2106&command=3",ct);
        if(Extract(state,"HumanoidDetection") is null)throw new NotSupportedException("กล้องไม่รายงานการรองรับ Human detection");
        var pir=Extract(state,"humanDetection");var distance=Extract(state,"DistanceAdjust");
        if(pir is null||distance is null)throw new NotSupportedException("อ่านค่าตรวจจับเดิมไม่ครบ จึงยังไม่เปลี่ยนค่า");
        return $"trans_cmd_string.cgi?cmd=2106&command=4&humanDetection={pir}&DistanceAdjust={distance}&HumanoidDetection={value}";
    }
    static void Check(string response)
    {
        if(response.Contains("error",StringComparison.OrdinalIgnoreCase)||response.Contains("fail",StringComparison.OrdinalIgnoreCase)||response.Contains("not support",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(response,@"result\s*[:=]\s*-",RegexOptions.IgnoreCase))
            throw new InvalidOperationException("กล้องไม่ยอมรับคำสั่งนี้");
        if(!response.Contains("ok",StringComparison.OrdinalIgnoreCase)&&!response.Contains("result",StringComparison.OrdinalIgnoreCase)&&!response.Contains("success",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("กล้องไม่ได้ยืนยันผลคำสั่ง");
    }
}

