using System.Text.Json;
namespace GimDvr;
public sealed record LiveStreamSettings(int SegmentMs=150,int StartupMs=300,int RebufferMs=300,int LiveTargetMs=300)
{
    public void Validate()
    {
        if(SegmentMs<50||SegmentMs>1000||new[]{StartupMs,RebufferMs,LiveTargetMs}.Any(v=>v<=SegmentMs||v>5000))
            throw new ArgumentException("ชิ้นสตรีมต้องอยู่ระหว่าง 50–1000 ms และ buffer แต่ละค่าต้องมากกว่าชิ้นสตรีมและไม่เกิน 5000 ms");
    }
}
public sealed partial class Store
{
    public LiveStreamSettings StreamSettings()
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT json FROM settings WHERE key='live-stream'";
        return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<LiveStreamSettings>(json)!:new();
    }
    public void SaveStreamSettings(LiveStreamSettings value)
    {
        value.Validate();using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO settings VALUES('live-stream',$json) ON CONFLICT(key) DO UPDATE SET json=$json";
        Param(cmd,"$json",JsonSerializer.Serialize(value));cmd.ExecuteNonQuery();
    }
}
