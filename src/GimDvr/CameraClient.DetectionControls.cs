namespace GimDvr;

public partial class CameraClient
{
    static string FeatureRead(string action)=>action switch {
        "motion"=>"get_params.cgi",
        "humanFrame"=>"trans_cmd_string.cgi?cmd=2126&command=1",
        "tracking"=>"trans_cmd_string.cgi?cmd=2127&command=1",
        _=>throw new ArgumentException("ไม่รองรับการตั้งค่านี้")
    };
    public static string? FeatureValue(string action,string response)
    {
        var v=Variables(response);
        if(action!="motion"&&(v.GetValueOrDefault("result")!="0"||v.GetValueOrDefault("cmd")!=(action=="humanFrame"?"2126":"2127")))return null;
        var value=v.GetValueOrDefault(action switch{"motion"=>"alarm_motion_armed","humanFrame"=>"bHumanoidFrame","tracking"=>"enable",_=>""});
        return value is "0" or "1"?value:null;
    }
    async Task<string?> OptionalFeature(Camera c,string action,CancellationToken ct)
    {
        try{return FeatureValue(action,await Get(c,FeatureRead(action),ct));}
        catch(Exception e) when(e is HttpRequestException or InvalidOperationException or OperationCanceledException && !ct.IsCancellationRequested){return null;}
    }
    public static string FeatureWrite(string action,int value,string previous)
    {
        if(value is not (0 or 1))throw new ArgumentException("ค่าต้องเป็น 0 หรือ 1");
        if(FeatureValue(action,previous) is null)throw new NotSupportedException("กล้องไม่ส่งค่าที่รองรับ จึงยังไม่เปลี่ยนการตั้งค่า");
        if(action=="motion")return $"set_alarm.cgi?motion_armed={value}";
        if(action=="tracking")return $"trans_cmd_string.cgi?cmd=2127&command=0&enable={value}";
        var old=Variables(previous);
        if(!int.TryParse(old.GetValueOrDefault("sensitive"),out var sensitivity)||sensitivity<0)throw new NotSupportedException("อ่านความไวเดิมไม่ได้ จึงยังไม่เปลี่ยนกรอบคน");
        return $"trans_cmd_string.cgi?cmd=2126&command=0&sensitive={sensitivity}&bHumanoidFrame={value}";
    }
    async Task SetDetectionFeature(Camera c,ControlInput input,CancellationToken ct)
    {
        var read=FeatureRead(input.Action);
        var previous=await Get(c,read,ct);
        var command=FeatureWrite(input.Action,input.Value,previous);
        Check(await Get(c,command,ct));
        if(FeatureValue(input.Action,await Get(c,read,ct))!=input.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("อ่านกลับแล้วค่าไม่ตรงกับที่สั่ง กรุณาตรวจสถานะกล้องอีกครั้ง");
    }
}
