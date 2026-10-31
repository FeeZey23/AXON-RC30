using System.Text.Json;
using ArenaDesktopCompanion.Core.Settings;
namespace ArenaDesktopCompanion.WebView;
/// <summary>User-requested current chat label only. Never reads messages or enumerates history.</summary>
public static class ArenaChatMetadata
{
    public static string NameScript(string address)
    {
        var canonical=ChatIdentity.Address(address)??throw new ArgumentException("Kein Arena-Chat.",nameof(address));var path=new Uri(canonical).AbsolutePath;
        return "(()=>{if(!['https://arena.ai','https://www.arena.ai'].includes(location.origin)||location.pathname.replace(/\\/$/,'')!=="+JsonSerializer.Serialize(path)+")return '';const p="+JsonSerializer.Serialize(path)+";const a=document.querySelector('a[href=\"'+p+'\"],a[href=\"https://arena.ai'+p+'\"],a[href=\"https://www.arena.ai'+p+'\"]');if(!a)return '';const c=a.cloneNode(true);c.querySelectorAll('button,svg,[aria-hidden=\"true\"]').forEach(n=>n.remove());return(c.getAttribute('title')||c.textContent||'').trim().slice(0,240)})()";
    }
}
