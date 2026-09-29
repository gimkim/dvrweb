using System.Net;
using System.Text.Json.Serialization;

namespace GimDvr;

public sealed record Camera
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Camera";
    public string Driver { get; set; } = "vstarcam";
    public string Host { get; set; } = "";
    public int HttpPort { get; set; } = 80;
    public int RtspPort { get; set; } = 554;
    public string RtspPath { get; set; } = "/tcp/av0_0";
    public string Username { get; set; } = "admin";
    public string Secret { get; set; } = "";
    public string? Uid { get; set; }
    public bool Enabled { get; set; } = true;
    public bool RecordingEnabled { get; set; }
    public string RecordingRoot { get; set; } = "";
    public int? RetentionDays { get; set; }
    public int Revision { get; set; } = 1;
    public string TalkMode { get; set; } = "auto";

    public void Validate()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(Id ?? "", "^[a-zA-Z0-9_-]{1,64}$")) throw new ArgumentException("รหัสกล้องไม่ถูกต้อง");
        if (Name.Length is < 1 or > 80) throw new ArgumentException("ชื่อกล้องต้องมี 1–80 ตัวอักษร");
        if (!IPAddress.TryParse(Host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("กรุณาระบุ IPv4 ของกล้องใน LAN");
        var b = ip.GetAddressBytes();
        if (!(b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31))
            throw new ArgumentException("รองรับ IP กล้องในเครือข่ายส่วนตัวเท่านั้น");
        if (HttpPort is < 1 or > 65535 || RtspPort is < 1 or > 65535) throw new ArgumentException("พอร์ตไม่ถูกต้อง");
        if (!RtspPath.StartsWith('/') || RtspPath.Contains('@') || RtspPath.Contains('#') || RtspPath.Any(char.IsControl))
            throw new ArgumentException("RTSP path ไม่ถูกต้อง");
        if (Driver is not ("vstarcam" or "rtsp")) throw new ArgumentException("ชนิดกล้องไม่ถูกต้อง");
        if (RetentionDays is < 1 or > 36500) throw new ArgumentException("จำนวนวันต้องมากกว่า 0 หรือเลือกไม่จำกัด");
        if (RecordingEnabled && string.IsNullOrWhiteSpace(RecordingRoot)) throw new ArgumentException("ตั้งโฟลเดอร์บันทึกก่อนเปิดการบันทึก");
        if (RecordingRoot.Length > 0 && !Path.IsPathFullyQualified(RecordingRoot)) throw new ArgumentException("ใช้ path เต็มบนเซิร์ฟเวอร์หรือ UNC");
        if (Username.Length > 100 || Uid?.Length > 100) throw new ArgumentException("ข้อมูลกล้องยาวเกินกำหนด");
    }
}
public sealed record CameraInput(Camera Camera, string? Password);
public sealed record LoginInput(string Username, string Password,bool RememberDevice=false);
public sealed record UserInput(string Username, string Role, bool Enabled, string? Password);
public sealed record UserRow(string Id, string Username, string Hash, string Role, bool Enabled, string Stamp);
public sealed record Recording(string Id, string CameraId, string CameraName, string Path, DateTimeOffset Start, double Duration, long Bytes)
{
    public DetectionResult? Detection { get; init; }
}
public sealed record ControlInput(string Action, int Value = 0);
public sealed class Paths(IConfiguration config, IWebHostEnvironment env)
{
    public string Data { get; } = Path.GetFullPath(config["Dvr:DataRoot"] ?? Path.Combine(env.ContentRootPath, "App_Data"));
    public string Ffmpeg { get; } = config["Dvr:Ffmpeg"] ?? "ffmpeg";
    public string Ffprobe { get; } = config["Dvr:Ffprobe"] ?? "ffprobe";
    public string Live => Path.Combine(Data, "live");
    public string Manifests => Path.Combine(Data, "manifests");
    public string Runtime => Path.Combine(Data,"runtime");
    public bool ExternalMedia => config["Dvr:MediaOwner"]=="worker"&&!config.GetValue<bool>("Dvr:WorkerProcess");
    public bool ArrivalClock(string id)=>(config["Dvr:ArrivalClockCameraIds"]??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Contains(id,StringComparer.Ordinal);
    public string LiveEncoder => config["Dvr:LiveEncoder"]??"auto";
    public int LiveHeight => Math.Clamp(config.GetValue<int?>("Dvr:LiveHeight")??1080,360,2160)/2*2;
}
