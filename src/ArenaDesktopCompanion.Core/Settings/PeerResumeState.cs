using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ArenaDesktopCompanion.Core.Settings;
public sealed record PeerResumeState(string Binding,DateTimeOffset LastSeen,string Signature)
{
    private static string Scope(Arbeitsbereich tab)=>JsonSerializer.Serialize(new{tab.Id,Root=Path.GetFullPath(tab.ProjectFolder!).TrimEnd('\\','/').ToUpperInvariant(),tab.BridgeBin,tab.ConnectionId,Chat=ChatIdentity.Address(tab.ConnectedChatAddress)??tab.ConnectedChatAddress});
    private static byte[] Sign(Arbeitsbereich tab,string binding,DateTimeOffset at)=>HMACSHA256.HashData(Encoding.UTF8.GetBytes(tab.BridgeToken!),Encoding.UTF8.GetBytes(binding+"\n"+at.ToString("O")));
    public static PeerResumeState Create(Arbeitsbereich tab,DateTimeOffset lastSeen)
    {
        if(string.IsNullOrWhiteSpace(tab.ConnectionId)||string.IsNullOrWhiteSpace(tab.BridgeToken)||string.IsNullOrWhiteSpace(tab.ProjectFolder))throw new InvalidOperationException("Keine bestätigbare Sitzung.");
        var binding=Scope(tab);return new(binding,lastSeen,Convert.ToHexString(Sign(tab,binding,lastSeen)));
    }
    public bool Matches(Arbeitsbereich tab,DateTimeOffset now)
    {
        try{return !string.IsNullOrWhiteSpace(tab.ConnectionId)&&!string.IsNullOrWhiteSpace(tab.BridgeToken)&&LastSeen<=now.AddMinutes(5)&&Binding==Scope(tab)&&CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Signature),Sign(tab,Binding,LastSeen));}
        catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or FormatException){return false;}
    }
    public static PeerResumeState? Read(string path,Arbeitsbereich tab)
    {
        try{var p=JsonSerializer.Deserialize<PeerResumeState>(File.ReadAllText(path));return p?.Matches(tab,DateTimeOffset.UtcNow)==true?p:null;}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
}
