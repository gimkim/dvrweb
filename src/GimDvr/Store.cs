using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json;

namespace GimDvr;

public sealed partial class Store
{
    readonly string connection;
    readonly IDataProtector protector;
    readonly object gate = new();
    public Store(Paths paths, IDataProtectionProvider protection)
    {
        Directory.CreateDirectory(paths.Data);
        connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(paths.Data, "dvr.db"), Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        protector = protection.CreateProtector("GimDvr.CameraCredentials.v1");
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,json TEXT NOT NULL);
        INSERT OR IGNORE INTO settings VALUES('live-stream','{"SegmentMs":150,"StartupMs":300,"RebufferMs":300,"LiveTargetMs":300}');
        CREATE TABLE IF NOT EXISTS cameras(id TEXT PRIMARY KEY,json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY,username TEXT UNIQUE COLLATE NOCASE,hash TEXT,role TEXT,enabled INTEGER,stamp TEXT);
        CREATE TABLE IF NOT EXISTS recordings(id TEXT PRIMARY KEY,cameraId TEXT,cameraName TEXT,path TEXT UNIQUE,start TEXT,duration REAL,bytes INTEGER);
        CREATE INDEX IF NOT EXISTS ix_recordings_camera_start ON recordings(cameraId,start);
        CREATE TABLE IF NOT EXISTS detections(recordingId TEXT PRIMARY KEY,state TEXT NOT NULL,result TEXT,attempts INTEGER NOT NULL DEFAULT 0,updated TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_detections_state ON detections(state,updated);
        CREATE TABLE IF NOT EXISTS attempts(key TEXT PRIMARY KEY,failures INTEGER,until TEXT);
        CREATE TABLE IF NOT EXISTS audit(id INTEGER PRIMARY KEY AUTOINCREMENT,time TEXT,actor TEXT,action TEXT,detail TEXT);
        """;
        cmd.ExecuteNonQuery();
        if (Users().Count == 0)
        {
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
            SaveUser(null, new("admin", "admin", true, password));
            File.WriteAllText(Path.Combine(paths.Data, "bootstrap.txt"), $"GimDvr initial administrator\r\nUsername: admin\r\nPassword: {password}\r\nCreated UTC: {DateTimeOffset.UtcNow:O}\r\nChange this password in Users after first sign-in. This file is outside the web root.\r\n");
        }
        var seed = Path.Combine(paths.Data, "seed-cameras.json");
        if (File.Exists(seed) && Cameras().Count == 0)
        {
            var entries = JsonSerializer.Deserialize<List<CameraInput>>(File.ReadAllText(seed), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            foreach (var entry in entries)
            {
                entry.Camera.RecordingEnabled = false;
                entry.Camera.RecordingRoot = "";
                SaveCamera(entry.Camera, entry.Password);
            }
            File.Delete(seed);
        }
    }
    SqliteConnection Open() { var db = new SqliteConnection(connection); db.Open(); return db; }
    static void Param(SqliteCommand cmd, string key, object? value) => cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
    public string Password(Camera camera) => protector.Unprotect(camera.Secret);
    public List<Camera> Cameras()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT json FROM cameras ORDER BY rowid";
        using var r = cmd.ExecuteReader(); List<Camera> result = [];
        while (r.Read()) result.Add(JsonSerializer.Deserialize<Camera>(r.GetString(0))!);
        return result;
    }
    public Camera Camera(string id) => Cameras().Find(x => x.Id == id) ?? throw new KeyNotFoundException("ไม่พบกล้อง");
    public Camera UpdateCameraNetwork(Camera expected,string host,int port)
    {
        lock(gate)
        {
            var current=Camera(expected.Id);
            if(current.Revision!=expected.Revision||current.Host!=expected.Host||current.MacAddress!=expected.MacAddress||!current.Enabled)return current;
            if(current.Host==host&&current.HttpPort==port)return current;
            return SaveCamera(current with{Host=host,HttpPort=port},null);
        }
    }
    public Camera SaveCamera(Camera camera, string? password)
    {
        lock (gate)
        {
            camera.Validate(); var old = Cameras().Find(x => x.Id == camera.Id);
            if (old is null && string.IsNullOrEmpty(password)) throw new ArgumentException("กรุณาระบุรหัสกล้อง");
            camera.Secret = !string.IsNullOrEmpty(password) ? protector.Protect(password) : old!.Secret;
            camera.Revision = (old?.Revision ?? 0) + 1;
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO cameras VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
            Param(cmd,"$id",camera.Id); Param(cmd,"$json",JsonSerializer.Serialize(camera)); cmd.ExecuteNonQuery(); return camera;
        }
    }
    public void DeleteCamera(string id) { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText="DELETE FROM cameras WHERE id=$id";Param(cmd,"$id",id);cmd.ExecuteNonQuery(); }
    public List<UserRow> Users()
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT id,username,hash,role,enabled,stamp FROM users ORDER BY username";
        using var r=cmd.ExecuteReader();List<UserRow> rows=[];while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetBoolean(4),r.GetString(5)));return rows;
    }
    public static string Hash(string password)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(salt)+":"+Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32));
    }
    public static bool Verify(string password,string hash)
    {
        var parts=hash.Split(':'); if(parts.Length!=2)return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]),Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(parts[0]),210000,HashAlgorithmName.SHA256,32));
    }
    public UserRow SaveUser(string? id,UserInput input)
    {
        lock(gate)
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(input.Username,"^[a-zA-Z0-9_.@-]{1,64}$"))throw new ArgumentException("ชื่อผู้ใช้ใช้ a-z, 0-9, _ . @ - สูงสุด 64 ตัว");
            if(input.Role is not("admin" or "operator" or "viewer"))throw new ArgumentException("สิทธิ์ไม่ถูกต้อง");
            var users=Users();var old=users.Find(x=>x.Id==id);
            if(users.Any(x=>x.Id!=id&&x.Username.Equals(input.Username,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("ชื่อผู้ใช้นี้มีแล้ว");
            if(old?.Role=="admin"&&old.Enabled&&(!input.Enabled||input.Role!="admin")&&users.Count(x=>x.Role=="admin"&&x.Enabled)==1)throw new ArgumentException("ต้องเหลือผู้ดูแลที่เปิดใช้งานอย่างน้อยหนึ่งคน");
            if(old is null&&string.IsNullOrEmpty(input.Password))throw new ArgumentException("กรุณาระบุรหัสผ่าน");
            var row=new UserRow(id??Guid.NewGuid().ToString("N"),input.Username,string.IsNullOrEmpty(input.Password)?old!.Hash:Hash(input.Password),input.Role,input.Enabled,Guid.NewGuid().ToString("N"));
            using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO users VALUES($id,$name,$hash,$role,$enabled,$stamp) ON CONFLICT(id) DO UPDATE SET username=$name,hash=$hash,role=$role,enabled=$enabled,stamp=$stamp";
            Param(cmd,"$id",row.Id);Param(cmd,"$name",row.Username);Param(cmd,"$hash",row.Hash);Param(cmd,"$role",row.Role);Param(cmd,"$enabled",row.Enabled);Param(cmd,"$stamp",row.Stamp);cmd.ExecuteNonQuery();return row;
        }
    }
    public bool LoginAllowed(string key)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT until FROM attempts WHERE key=$key";Param(cmd,"$key",key);
        var value=cmd.ExecuteScalar() as string;return value is null||DateTimeOffset.Parse(value)<=DateTimeOffset.UtcNow;
    }
    public void LoginResult(string key,bool success)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText=success?"DELETE FROM attempts WHERE key=$key":"INSERT INTO attempts VALUES($key,1,$now) ON CONFLICT(key) DO UPDATE SET failures=CASE WHEN until < $reset THEN 1 ELSE failures+1 END,until=CASE WHEN failures>=4 AND until >= $reset THEN $until ELSE $now END";
        Param(cmd,"$key",key);Param(cmd,"$now",DateTimeOffset.UtcNow.ToString("O"));Param(cmd,"$reset",DateTimeOffset.UtcNow.AddMinutes(-15).ToString("O"));Param(cmd,"$until",DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));cmd.ExecuteNonQuery();
    }
    public void AddRecording(Recording r)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT OR IGNORE INTO recordings VALUES($id,$camera,$name,$path,$start,$duration,$bytes)";
        Param(cmd,"$id",r.Id);Param(cmd,"$camera",r.CameraId);Param(cmd,"$name",r.CameraName);Param(cmd,"$path",r.Path);Param(cmd,"$start",r.Start.ToUniversalTime().ToString("O"));Param(cmd,"$duration",r.Duration);Param(cmd,"$bytes",r.Bytes);cmd.ExecuteNonQuery();
    }
    public List<Recording> Recordings(string? camera=null,DateTimeOffset? from=null,DateTimeOffset? to=null,int limit=500)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM recordings WHERE ($camera IS NULL OR cameraId=$camera) AND ($from IS NULL OR start >= $from) AND ($to IS NULL OR start < $to) ORDER BY start DESC LIMIT $limit";
        Param(cmd,"$camera",camera);Param(cmd,"$from",from?.ToUniversalTime().ToString("O"));Param(cmd,"$to",to?.ToUniversalTime().ToString("O"));Param(cmd,"$limit",limit);
        using var r=cmd.ExecuteReader();List<Recording> rows=[];while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),DateTimeOffset.Parse(r.GetString(4)),r.GetDouble(5),r.GetInt64(6)));return rows;
    }
    public List<Recording> RecordingRange(string camera,DateTimeOffset from,DateTimeOffset to,int offset)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT * FROM recordings WHERE cameraId=$camera AND julianday(start)<julianday($to) AND julianday(start)+duration/86400.0>julianday($from) ORDER BY start,id LIMIT 1000 OFFSET $offset";
        Param(cmd,"$camera",camera);Param(cmd,"$from",from.ToUniversalTime().ToString("O"));Param(cmd,"$to",to.ToUniversalTime().ToString("O"));Param(cmd,"$offset",offset);
        using var r=cmd.ExecuteReader();List<Recording> rows=[];while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),DateTimeOffset.Parse(r.GetString(4)),r.GetDouble(5),r.GetInt64(6)));return rows;
    }
    public Recording Recording(string id)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM recordings WHERE id=$id";Param(cmd,"$id",id);using var r=cmd.ExecuteReader();
        if(!r.Read())throw new KeyNotFoundException("ไม่พบวิดีโอ");return new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),DateTimeOffset.Parse(r.GetString(4)),r.GetDouble(5),r.GetInt64(6));
    }
    public void ForgetRecording(string id){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="DELETE FROM detections WHERE recordingId=$id; DELETE FROM recordings WHERE id=$id";Param(cmd,"$id",id);cmd.ExecuteNonQuery();}
    public void Audit(string actor,string action,string detail)
    {using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO audit(time,actor,action,detail) VALUES($time,$actor,$action,$detail)";Param(cmd,"$time",DateTimeOffset.UtcNow.ToString("O"));Param(cmd,"$actor",actor);Param(cmd,"$action",action);Param(cmd,"$detail",detail);cmd.ExecuteNonQuery();}
    public object AuditRows(){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT time,actor,action,detail FROM audit ORDER BY id DESC LIMIT 200";using var r=cmd.ExecuteReader();List<object> rows=[];while(r.Read())rows.Add(new{time=r.GetString(0),actor=r.GetString(1),action=r.GetString(2),detail=r.GetString(3)});return rows;}
}

