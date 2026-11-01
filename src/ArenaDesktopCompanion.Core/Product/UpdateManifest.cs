using System.Text.Json;
using System.Text.RegularExpressions;
namespace ArenaDesktopCompanion.Core.Product;
public sealed record UpdateManifest(string Version,string File,string Sha256)
{
    public static string GitHubBootstrapperAsset(string localName){if(localName!="AXON RC30 Installer.exe")throw new InvalidDataException("Unexpected local bootstrapper name.");return "AXON.RC30.Installer.exe";}
    public static void ValidateBootstrapper(string file,string sha256,string assetDigest)
    {if(file!="AXON RC30 Installer.exe"||!Regex.IsMatch(sha256,"^[a-f0-9]{64}$")||assetDigest!="sha256:"+sha256)throw new InvalidDataException("Unsafe bootstrapper or mismatched GitHub digest.");}
    public static UpdateManifest Parse(string json,string assetDigest)
    {
        using var document=JsonDocument.Parse(json);var m=document.RootElement;
        if(m.GetProperty("product").GetString()!="AXON.RC30")throw new InvalidDataException("Wrong update product.");
        var version=m.GetProperty("version").GetString()!;
        if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+$")||!System.Version.TryParse(version,out var parsed)||parsed.Major>255||parsed.Minor>255||parsed.Build>65535)throw new InvalidDataException("Invalid MSI version.");
        var file=m.GetProperty("file").GetString()!;
        if(file!="ISAS-AXON-RC30-"+version+"-win-x64.msi")throw new InvalidDataException("Unsafe/mismatched update filename.");
        var sha=m.GetProperty("sha256").GetString()!.ToLowerInvariant();
        if(!Regex.IsMatch(sha,"^[a-f0-9]{64}$")||assetDigest!="sha256:"+sha)throw new InvalidDataException("Update manifest and authenticated GitHub digest disagree.");
        return new(version,file,sha);
    }
}
