using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
namespace GimDvr;
public sealed class AssetVersions(IWebHostEnvironment environment)
{
    readonly ConcurrentDictionary<string,(DateTime Modified,long Length,string Hash)> cache=new();
    public string Url(string url)
    {
        if(string.IsNullOrEmpty(url)||url.StartsWith('/')||url.StartsWith('#')||url.Contains(':'))return url;
        var fragment=url.IndexOf('#');var suffix=fragment<0?"":url[fragment..];
        var clean=fragment<0?url:url[..fragment];var query=clean.IndexOf('?');var name=query<0?clean:clean[..query];
        if(name.Split('/').Any(x=>x==".."))return url;
        var file=environment.WebRootFileProvider.GetFileInfo(name);
        if(!file.Exists||file.IsDirectory)return url;
        var key=file.LastModified.UtcDateTime;
        if(!cache.TryGetValue(name,out var entry)||entry.Modified!=key||entry.Length!=file.Length)
        {
            using var stream=file.CreateReadStream();
            entry=(key,file.Length,Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());cache[name]=entry;
        }
        var values=QueryHelpers.ParseQuery(query<0?"":clean[query..]).ToDictionary(x=>x.Key,x=>(string?)x.Value.ToString());
        values["v"]=entry.Hash;
        return QueryHelpers.AddQueryString(name,values)+suffix;
    }
    public async Task<string> Index(CancellationToken ct)
    {
        using var stream=environment.WebRootFileProvider.GetFileInfo("index.html").CreateReadStream();
        using var reader=new StreamReader(stream);var html=await reader.ReadToEndAsync(ct);
        html=Regex.Replace(html,"(?<attr>\\b(?:src|href|poster))=\"(?<url>[^\"]+)\"",m=>$"{m.Groups["attr"].Value}=\"{System.Net.WebUtility.HtmlEncode(Url(System.Net.WebUtility.HtmlDecode(m.Groups["url"].Value)))}\"");
        var dynamicAssets=JsonSerializer.Serialize(new Dictionary<string,string>{{"talk-worklet.js",Url("talk-worklet.js")}});
        return html.Replace("<head>","<head><script>window.assetUrl=u=>("+dynamicAssets+")[u]||u;</script>");
    }
}
