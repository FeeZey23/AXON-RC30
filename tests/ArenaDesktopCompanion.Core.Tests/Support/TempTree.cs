namespace ArenaDesktopCompanion.Core.Tests.Support;

/// <summary>Ein eigener temporärer Projektordner pro Test, damit Tests nicht ins Haus schreiben.</summary>
public sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "arena-companion-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Full(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Write(string relative, string content)
    {
        var path = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void WriteBytes(string relative, byte[] bytes)
    {
        var path = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public bool Exists(string relative) => File.Exists(Full(relative));
    public string Read(string relative) => File.ReadAllText(Full(relative));
    public byte[] ReadAll(string relative) => File.ReadAllBytes(Full(relative));

    public IEnumerable<string> AllFiles()
        => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
