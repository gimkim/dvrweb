using System.Text.Json;

namespace GimDvr;

public sealed partial class Store
{
    // Load all results for a returned page in one query, never expose filesystem paths.
    public List<Recording> WithDetection(List<Recording> recordings)
    {
        if(recordings.Count==0)return recordings;
        using var db=Open();using var cmd=db.CreateCommand();
        var keys=recordings.Select((r,i)=>{var key="$id"+i;Param(cmd,key,r.Id);return key;}).ToArray();
        cmd.CommandText=$"SELECT recordingId,state,result FROM detections WHERE recordingId IN ({string.Join(',',keys)})";
        using var reader=cmd.ExecuteReader();var results=new Dictionary<string,DetectionResult>();
        while(reader.Read())results[reader.GetString(0)]=reader.IsDBNull(2)?new(reader.GetString(1)):JsonSerializer.Deserialize<DetectionResult>(reader.GetString(2))!;
        return recordings.Select(r=>r with{Detection=results.GetValueOrDefault(r.Id)??new("pending")}).ToList();
    }
    public Recording? NextDetection(bool oldest=false)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
        SELECT r.* FROM recordings r LEFT JOIN detections d ON d.recordingId=r.id
        WHERE d.recordingId IS NULL OR d.state='pending' OR (d.state IN ('error','partial') AND d.attempts<3 AND d.updated<$retry)
        ORDER BY r.start
        """+(oldest?" ASC":" DESC")+",r.id LIMIT 1";
        Param(cmd,"$retry",DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
        using var r=cmd.ExecuteReader();return r.Read()?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),DateTimeOffset.Parse(r.GetString(4)),r.GetDouble(5),r.GetInt64(6)):null;
    }
    public void ResetInterruptedDetections()
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE detections SET state='pending',result=NULL WHERE state='processing'";cmd.ExecuteNonQuery();
    }
    public void SaveDetection(string id,DetectionResult result)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
        INSERT INTO detections(recordingId,state,result,attempts,updated)
        SELECT $id,$state,$result,CASE WHEN $state='processing' THEN 1 ELSE 0 END,$now WHERE EXISTS(SELECT 1 FROM recordings WHERE id=$id)
        ON CONFLICT(recordingId) DO UPDATE SET state=$state,result=$result,updated=$now,attempts=detections.attempts+CASE WHEN $state='processing' THEN 1 ELSE 0 END
        """;
        Param(cmd,"$id",id);Param(cmd,"$state",result.State);Param(cmd,"$result",JsonSerializer.Serialize(result));Param(cmd,"$now",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery();
    }
}
