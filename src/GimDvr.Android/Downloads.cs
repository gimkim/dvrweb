using Android.App;
using Android.Content;
using Android.Webkit;
using Android.Widget;
using System.Net;

namespace GimDvr.AndroidApp;

public sealed partial class MainActivity
{
    const int SaveRequest = 1042;
    sealed record PendingSave(string Name, byte[]? Image, string? Url, string? Cookie);
    PendingSave? pendingSave;
    bool saving;
    void Notice(string text)=>Toast.MakeText(this,text,ToastLength.Long)?.Show();
    void ChooseSave(PendingSave pending,string mime)
    {
        if(!Allowed(web.Url))return;
        if(pendingSave is not null||saving){Notice("กำลังบันทึกไฟล์ก่อนหน้า");return;}
        pendingSave=pending;
        try{
            var intent=new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);intent.SetType(mime);intent.PutExtra(Intent.ExtraTitle,pending.Name);
            StartActivityForResult(intent,SaveRequest);
        }catch(Exception){pendingSave=null;Notice("เปิดหน้าบันทึกไฟล์ไม่ได้");}
    }
    void SaveImage(string data,string name)
    {
        if(!Allowed(web.Url))return;
        try{
            const string prefix="data:image/jpeg;base64,";
            if(data.Length>24*1024*1024||!data.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidDataException();
            var bytes=Convert.FromBase64String(data[prefix.Length..]);
            if(bytes.Length<3||bytes[0]!=255||bytes[1]!=216||bytes[2]!=255)throw new InvalidDataException();
            ChooseSave(new(SafeName(name,"jpg"),bytes,null,null),"image/jpeg");
        }catch(Exception){Notice("ข้อมูลภาพไม่ถูกต้องหรือใหญ่เกินไป");}
    }
    static string SafeName(string name,string extension)
    {
        var clean=System.Text.RegularExpressions.Regex.Replace(name,@"[^a-zA-Z0-9_.-]","_");
        if(clean.Length>120)clean=clean[..120];
        return clean.EndsWith("."+extension,StringComparison.OrdinalIgnoreCase)?clean:$"GimDVR-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";
    }
    void Download(string? url,string? disposition)
    {
        if(!Allowed(web.Url)||!Allowed(url)){Notice("ไม่อนุญาตลิงก์ดาวน์โหลดนี้");return;}
        var uri=new Uri(url!);
        if(!System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,@"^/gimdvr/api/recordings/[a-zA-Z0-9_-]+/download$")){Notice("ไม่รองรับไฟล์ดาวน์โหลดนี้");return;}
        var name=URLUtil.GuessFileName(url,disposition,"video/mp4")??"GimDVR.mp4";
        ChooseSave(new(SafeName(name,"mp4"),null,url,CookieManager.Instance?.GetCookie(url)),"video/mp4");
    }
    protected override async void OnActivityResult(int requestCode,Result resultCode,Intent? data)
    {
        base.OnActivityResult(requestCode,resultCode,data);
        if(requestCode!=SaveRequest)return;
        var pending=pendingSave;pendingSave=null;
        if(resultCode!=Result.Ok||data?.Data is null||pending is null)return;
        var destination=data.Data;saving=true;Notice("กำลังบันทึกไฟล์…");
        try{
            await Task.Run(async()=>{
                using var output=ContentResolver!.OpenOutputStream(destination,"w")??throw new IOException();
                if(pending.Image is not null)await output.WriteAsync(pending.Image);
                else{
                    using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false};
                    using var client=new HttpClient(handler){Timeout=TimeSpan.FromMinutes(30)};
                    using var request=new HttpRequestMessage(HttpMethod.Get,pending.Url);
                    if(!string.IsNullOrEmpty(pending.Cookie))request.Headers.TryAddWithoutValidation("Cookie",pending.Cookie);
                    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);
                    if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentType?.MediaType!="video/mp4")throw new IOException();
                    await response.Content.CopyToAsync(output);
                }
                await output.FlushAsync();
            });
            Notice("บันทึกไฟล์แล้ว");
        }catch(Exception){
            try{global::Android.Provider.DocumentsContract.DeleteDocument(ContentResolver!,destination);}catch(Exception){}
            Notice("บันทึกไม่สำเร็จ ตรวจสอบเครือข่าย พื้นที่ว่าง และเข้าสู่ระบบอีกครั้ง");
        }finally{saving=false;}
    }
    sealed class Downloads(MainActivity activity):Java.Lang.Object,IDownloadListener
    {
        public void OnDownloadStart(string? url,string? userAgent,string? contentDisposition,string? mimetype,long contentLength)
            =>activity.RunOnUiThread(()=>activity.Download(url,contentDisposition));
    }
}
