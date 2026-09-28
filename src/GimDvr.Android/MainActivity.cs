using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Java.Interop;

namespace GimDvr.AndroidApp;

[Activity(Label="GimDVR",MainLauncher=true,Exported=true,ScreenOrientation=ScreenOrientation.Portrait,
    ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize|ConfigChanges.KeyboardHidden)]
public sealed class MainActivity:Activity
{
    const string Server="https://gimgim.ddns.net/gimdvr/";
    WebView web=null!; bool fullScreen; bool paused;
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        Window?.SetFlags(WindowManagerFlags.KeepScreenOn,WindowManagerFlags.KeepScreenOn);
        web=new WebView(this);var root=new FrameLayout(this);root.AddView(web,new FrameLayout.LayoutParams(-1,-1));root.SetOnApplyWindowInsetsListener(new InsetsListener(this));SetContentView(root);
        var s=web.Settings;s.JavaScriptEnabled=true;s.DomStorageEnabled=true;s.MediaPlaybackRequiresUserGesture=false;
        s.AllowFileAccess=false;s.AllowContentAccess=false;s.MixedContentMode=MixedContentHandling.NeverAllow;
        s.UserAgentString+=" GimDvrAndroid/1.0";s.SetSupportMultipleWindows(false);
        CookieManager.Instance?.SetAcceptCookie(true);CookieManager.Instance?.SetAcceptThirdPartyCookies(web,false);
        web.SetWebViewClient(new LockedClient(this));web.SetWebChromeClient(new WebChromeClient());
        web.AddJavascriptInterface(new Bridge(this),"GimDvrAndroid");
        web.LoadUrl(Server);
    }
    static bool Allowed(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.Host=="gimgim.ddns.net"&&u.Port==443&&(u.AbsolutePath=="/gimdvr"||u.AbsolutePath.StartsWith("/gimdvr/",StringComparison.Ordinal));
    void SetFullscreen(bool value)
    {
        fullScreen=value;RequestedOrientation=value?ScreenOrientation.SensorLandscape:ScreenOrientation.Portrait;
        if(OperatingSystem.IsAndroidVersionAtLeast(30)){
            var controller=Window?.InsetsController;
            if(controller is not null){controller.SystemBarsBehavior=(int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;if(value)controller.Hide(WindowInsets.Type.SystemBars());else controller.Show(WindowInsets.Type.SystemBars());}
        }else{if(value)Window?.SetFlags(WindowManagerFlags.Fullscreen,WindowManagerFlags.Fullscreen);else Window?.ClearFlags(WindowManagerFlags.Fullscreen);}
        Window?.DecorView.RequestApplyInsets();
    }
    protected override void OnPause()
    {
        web.EvaluateJavascript("window.gimDvrSuspend?.()",null);CookieManager.Instance?.Flush();paused=true;web.OnPause();base.OnPause();
    }
    protected override void OnResume()
    {
        base.OnResume();if(web is null)return;web.OnResume();
        if(paused){paused=false;web.EvaluateJavascript("window.gimDvrResume?.()",null);}
    }
    public override void OnBackPressed()
    {
        if(fullScreen){web.EvaluateJavascript("window.exitLiveFullscreen?.()",null);SetFullscreen(false);}
        else if(web.CanGoBack())web.GoBack();else MoveTaskToBack(true);
    }
    protected override void OnDestroy(){web?.Destroy();base.OnDestroy();}
    sealed class InsetsListener(MainActivity activity):Java.Lang.Object,View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view,WindowInsets insets)
        {
            if(OperatingSystem.IsAndroidVersionAtLeast(30)){
                var safe=insets.GetInsets(activity.fullScreen?WindowInsets.Type.DisplayCutout():WindowInsets.Type.SystemBars()|WindowInsets.Type.DisplayCutout());
                view.SetPadding(safe.Left,safe.Top,safe.Right,safe.Bottom);
            }
            return insets;
        }
    }
    sealed class Bridge(MainActivity activity):Java.Lang.Object
    {
        [JavascriptInterface,Export("setFullscreen")]
        public void SetFullscreen(bool value)=>activity.RunOnUiThread(()=>{if(Allowed(activity.web.Url))activity.SetFullscreen(value);});
        [JavascriptInterface,Export("flushCookies")]
        public void FlushCookies()=>activity.RunOnUiThread(()=>CookieManager.Instance?.Flush());
    }
    sealed class LockedClient(MainActivity activity):WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view,IWebResourceRequest? request)=>!Allowed(request?.Url?.ToString());
        public override void OnPageFinished(WebView? view,string? url){base.OnPageFinished(view,url);CookieManager.Instance?.Flush();}
        public override void OnReceivedSslError(WebView? view,SslErrorHandler? handler,global::Android.Net.Http.SslError? error){handler?.Cancel();Toast.MakeText(activity,"ตรวจสอบ HTTPS ของเซิร์ฟเวอร์ไม่ผ่าน",ToastLength.Long)?.Show();}
        public override void OnReceivedError(WebView? view,IWebResourceRequest? request,WebResourceError? error)
        {
            base.OnReceivedError(view,request,error);
            if(request?.IsForMainFrame==true){var alert=new AlertDialog.Builder(activity);alert.SetTitle("เชื่อมต่อ GimDVR ไม่สำเร็จ");alert.SetMessage("ตรวจสอบเครือข่ายแล้วลองอีกครั้ง");alert.SetPositiveButton("ลองใหม่",(_,_)=>activity.web.LoadUrl(Server));alert.SetNegativeButton("ปิด",(_,_)=>activity.Finish());alert.Show();}
        }
    }
}
