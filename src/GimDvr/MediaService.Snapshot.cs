using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GimDvr;

public sealed partial class MediaService
{
    readonly SemaphoreSlim snapshotSlots=new(2,2);
    public async Task<byte[]> Snapshot(Camera camera,CancellationToken ct)
    {
        if(!camera.Enabled)throw new InvalidOperationException("กล้องนี้ถูกปิดใช้งาน");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token=timeout.Token;
        await snapshotSlots.WaitAsync(token);
        try
        {
            byte[]? segment=null;
            while(segment is null)
            {
                token.ThrowIfCancellationRequested();
                var playlist=LiveFile(camera.Id,"index.m3u8");
                if(playlist is not null)
                {
                    try
                    {
                        var lines=await File.ReadAllLinesAsync(playlist,token);
                        var name=lines.LastOrDefault(x=>Regex.IsMatch(x,@"^seg\d+\.ts$"));
                        if(name is not null)
                        {
                            var path=Path.Combine(Path.GetDirectoryName(playlist)!,name);
                            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                            if(file.Length>16*1024*1024)throw new InvalidOperationException("เฟรมภาพมีขนาดเกินที่รองรับ");
                            using var buffer=new MemoryStream();await file.CopyToAsync(buffer,token);segment=buffer.ToArray();
                        }
                    }
                    catch(IOException){ /* A segment may rotate while the playlist is read. */ }
                }
                if(segment is null)await Task.Delay(200,token);
            }
            var info=new ProcessStartInfo(paths.Ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-hide_banner","-loglevel","error","-f","mpegts","-i","pipe:0","-frames:v","1","-an","-c:v","mjpeg","-q:v","2","-f","image2pipe","pipe:1"})info.ArgumentList.Add(arg);
            using var process=new Process{StartInfo=info};
            if(!process.Start())throw new InvalidOperationException("เริ่มตัวสร้างภาพนิ่งไม่สำเร็จ");
            try
            {
                using var image=new MemoryStream();
                var output=process.StandardOutput.BaseStream.CopyToAsync(image,token);
                var errors=process.StandardError.ReadToEndAsync(token);
                async Task Feed(){try{await process.StandardInput.BaseStream.WriteAsync(segment,token);}catch(IOException){}finally{process.StandardInput.Close();}}
                await Task.WhenAll(Feed(),output,errors,process.WaitForExitAsync(token));
                var bytes=image.ToArray();
                if(process.ExitCode!=0||bytes.Length<2||bytes[0]!=255||bytes[1]!=216)throw new InvalidOperationException("สร้างภาพนิ่งไม่ได้ กรุณารอภาพสดแล้วลองอีกครั้ง");
                return bytes;
            }
            finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
        }
        finally{snapshotSlots.Release();}
    }
}
