using System.Text.Json;
namespace GimDvr;
public sealed record DetectionSettings(int Workers=1,double ReadRate=4)
{
    public void Validate(){if(Workers<1||Workers>4||!double.IsFinite(ReadRate)||ReadRate<0||ReadRate>32)throw new ArgumentException("จำนวน worker ต้องเป็น 1–4 และความเร็วอ่านต้องเป็น 0–32 เท่า (0 = ไม่จำกัด)");}
}
public sealed partial class Store
{
    public DetectionSettings DetectionSettings(IConfiguration config)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT json FROM settings WHERE key='detection'";
        return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<DetectionSettings>(json)!:new(Math.Clamp(config.GetValue<int?>("Dvr:DetectionConcurrency")??1,1,4),Math.Clamp(config.GetValue<double?>("Dvr:DetectionReadRate")??4,0,32));
    }
    public void SaveDetectionSettings(DetectionSettings value)
    {
        value.Validate();using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO settings VALUES('detection',$json) ON CONFLICT(key) DO UPDATE SET json=$json";Param(cmd,"$json",JsonSerializer.Serialize(value));cmd.ExecuteNonQuery();
    }
}
