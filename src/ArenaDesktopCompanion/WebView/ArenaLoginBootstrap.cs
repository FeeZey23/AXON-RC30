using Microsoft.Web.WebView2.Core;
namespace ArenaDesktopCompanion.WebView;

/// <summary>One-time, in-memory copy of same-site Arena cookies; never share profile directories.</summary>
public static class ArenaLoginBootstrap
{
    public static async Task<int> CopyAsync(CoreWebView2 source,CoreWebView2 target,bool replace=false)
    {
        if(ReferenceEquals(source,target))throw new InvalidOperationException("Quelle und Ziel sind identisch.");
        var cookies=(await source.CookieManager.GetCookiesAsync("https://arena.ai")).Concat(await source.CookieManager.GetCookiesAsync("https://www.arena.ai"))
            .Where(c=>c.Domain.TrimStart('.').Equals("arena.ai",StringComparison.OrdinalIgnoreCase)||c.Domain.TrimStart('.').Equals("www.arena.ai",StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c=>(c.Name,c.Domain,c.Path)).ToArray();
        if(cookies.Length==0)return 0;
        var existing=(await target.CookieManager.GetCookiesAsync("https://arena.ai")).Concat(await target.CookieManager.GetCookiesAsync("https://www.arena.ai")).ToArray();
        if(!replace&&existing.Length>0)return 0;
        if(replace)foreach(var cookie in existing.Where(c=>c.Domain.TrimStart('.').Equals("arena.ai",StringComparison.OrdinalIgnoreCase)||c.Domain.TrimStart('.').Equals("www.arena.ai",StringComparison.OrdinalIgnoreCase)))target.CookieManager.DeleteCookie(cookie);
        foreach(var c in cookies)
        {
            var copy=target.CookieManager.CreateCookie(c.Name,c.Value,c.Domain,c.Path);copy.IsHttpOnly=c.IsHttpOnly;copy.IsSecure=c.IsSecure;copy.SameSite=c.SameSite;
            if(!c.IsSession)copy.Expires=c.Expires;
            target.CookieManager.AddOrUpdateCookie(copy);
        }
        return cookies.Length;
    }
}
