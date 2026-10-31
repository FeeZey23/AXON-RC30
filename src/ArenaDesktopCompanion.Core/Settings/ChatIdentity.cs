using System.Text.RegularExpressions;
namespace ArenaDesktopCompanion.Core.Settings;
public static class ChatIdentity
{
    public static string? Address(string? raw)
    {
        if(!Uri.TryCreate(raw,UriKind.Absolute,out var u)||u.Scheme!="https"||!u.IsDefaultPort||u.UserInfo.Length>0||u.Host is not ("arena.ai" or "www.arena.ai"))return null;
        var path=u.AbsolutePath.TrimEnd('/');
        if(path!="/agent"&&!(path.StartsWith("/agent/",StringComparison.Ordinal)&&Guid.TryParseExact(path[7..],"D",out _)))return null;
        return "https://arena.ai"+path;
    }
    public static bool IsChat(string? raw)=>Address(raw) is{} a&&a!="https://arena.ai/agent";
    public static string? Name(string? raw){if(string.IsNullOrWhiteSpace(raw))return null;var s=Regex.Replace(raw,@"\s+"," ").Trim();return s.Length>160?s[..160]:s;}
    public static string? Title(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return null;
        var s=Regex.Replace(raw,@"\s+"," ").Trim();
        if(Regex.IsMatch(s,@"^(?:Arena(?:\.ai)?|LMArena)\s*[|·–—-]\s*(?:Benchmark|Compare|AI|Free|Chat)",RegexOptions.IgnoreCase))return null;
        s=Regex.Replace(s,@"\s+[|·–—-]\s+(?:Arena(?:\.ai)?|LMArena)(?:\s.*)?$","",RegexOptions.IgnoreCase).Trim();
        if(Regex.IsMatch(s,@"^(?:Arena(?:\.ai)?|LMArena|New chat|Neuer Chat|Agent(?: Mode)?|Arena - AI.*)$",RegexOptions.IgnoreCase))return null;
        return s.Length>160?s[..160]:s;
    }
}
