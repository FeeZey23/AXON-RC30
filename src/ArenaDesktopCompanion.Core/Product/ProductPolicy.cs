using System.Globalization;
using System.Text.RegularExpressions;
namespace ArenaDesktopCompanion.Core.Product;

public static partial class ProductPolicy
{
    public const string Repository = "FeeZey23/AXON-RC30";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public static Version CurrentVersion => typeof(ProductPolicy).Assembly.GetName().Version!;
    public static bool IsArena(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https" && (u.Host.Equals("arena.ai", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("www.arena.ai", StringComparison.OrdinalIgnoreCase)) && u.IsDefaultPort && string.IsNullOrEmpty(u.UserInfo);
    public static string ParseRepository(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var u) || u.Scheme != "https" || !u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !u.IsDefaultPort || u.UserInfo.Length != 0 || u.Query.Length != 0 || u.Fragment.Length != 0) throw new ArgumentException("Use https://github.com/owner/repository, without branch, credentials or query.");
        var parts = u.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(p => !RepoPart().IsMatch(p))) throw new ArgumentException("Expected GitHub owner/repository.");
        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        if (name.Length == 0 || name is "." or "..") throw new ArgumentException("Invalid repository name.");
        return parts[0] + "/" + name;
    }
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")] private static partial Regex RepoPart();
    public static void ValidateRepositoryName(string name) { if (!RepoPart().IsMatch(name) || name.EndsWith(".git",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid repository name."); }
}

public static class AutomaticProjects
{
    public static string Create(string programDirectory, DateTime? now = null)
    {
        var root = Path.GetFullPath(Path.Combine(programDirectory, ".axon", "projects"));
        for (var p = new DirectoryInfo(programDirectory); p is not null; p = p.Parent) if (p.Exists && p.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Program directory must not traverse a reparse point.");
        Directory.CreateDirectory(root);
        foreach (var p in new[] {Directory.GetParent(root)!, new DirectoryInfo(root)}) if (p.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Project storage must not be a reparse point.");
        var counter = Path.Combine(root, ".sequence");
        using var stream = OpenCounter(counter);
        using var reader = new StreamReader(stream, leaveOpen:true);
        var text = reader.ReadToEnd();
        var last = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        foreach (var d in Directory.EnumerateDirectories(root)) { var part=Path.GetFileName(d).Split('.')[0]; if(int.TryParse(part,out var old)) last=Math.Max(last,old); }
        var next=checked(last+1);var path=Path.Combine(root,next.ToString("D4",CultureInfo.InvariantCulture)+".axon."+(now??DateTime.Now).ToString("dd-MM-yyyy-HH-mm",CultureInfo.InvariantCulture));
        if(Directory.Exists(path)||File.Exists(path)) throw new IOException("Project name collision; no overwrite.");
        stream.Position=0;stream.SetLength(0);using(var writer=new StreamWriter(stream,leaveOpen:true)){writer.Write(next.ToString(CultureInfo.InvariantCulture));writer.Flush();}stream.Flush(true);
        Directory.CreateDirectory(path);return path;
    }
    private static FileStream OpenCounter(string path)
    {
        if(File.Exists(path)&&File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Unsafe project counter.");
        var until=DateTime.UtcNow.AddSeconds(5);
        while(true){try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException) when(DateTime.UtcNow<until){Thread.Sleep(25);}}
    }
}

public static partial class PublicationPolicy
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase){".git",".axon",".axon-rc30",".vs",".idea","node_modules","bin","obj",".artifacts","releases","Profiles","Sessions","StateBackups","uploads",".arena-inbox",".arena-backup",".arena-staging",".arena-conflicts","__pycache__"};
    public static bool IsAllowed(string relative)
    {
        var parts=relative.Replace('\\','/').Split('/');
        if(Path.IsPathRooted(relative)||relative.Contains(':')||parts.Any(p=>p is "" or "." or ".."))return false;
        if(parts.Any(p=>Excluded.Contains(p)||p.StartsWith("_private",StringComparison.OrdinalIgnoreCase)))return false;
        var name=parts[^1];var ext=Path.GetExtension(name).ToLowerInvariant();
        return !name.StartsWith(".env",StringComparison.OrdinalIgnoreCase) && !new[]{"settings.json","settings.forward.json","session.json","token.txt","bin.txt","verbindung.txt","binding.json","peer-binding.json",".sequence"}.Contains(name,StringComparer.OrdinalIgnoreCase) && !name.StartsWith("settings.json.",StringComparison.OrdinalIgnoreCase) && !new[]{".pfx",".p12",".pem",".key",".msi",".exe",".dll",".zip",".7z",".cab",".log",".trx",".dmp"}.Contains(ext);
    }
    public static void EnsureScannerPassed(int exitCode){if(exitCode!=0)throw new InvalidDataException("Upload refused: maintained secret scanner found a possible credential or could not complete. Review project contents and the private redacted scanner report.");}
    public static bool ContainsSecret(string text) => Credentials().IsMatch(text);
    [GeneratedRegex(@"(?s)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----\s*[A-Za-z0-9+/=\s]{48,}-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\bgh[pousr]_[A-Za-z0-9]{30,}\b|\bgithub_pat_[A-Za-z0-9_]{40,}\b|\bAKIA[A-Z0-9]{16}\b|\bxox[baprs]-[A-Za-z0-9-]{24,}\b")]
    private static partial Regex Credentials();
}
