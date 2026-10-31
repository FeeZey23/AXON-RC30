namespace ArenaDesktopCompanion.Core.Settings;

/// <summary>RC30 owns its complete state. No migration or reuse of rc27 credentials/profiles.</summary>
public static class Rc30Paths
{
    public const string InstanceName = "AXON.RC30";
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AXON", Environment.GetEnvironmentVariable("AXON_RC30_SMOKE") == "1" ? "RC30.Smoke" : "RC30");
    public static string Profiles => Path.Combine(DataRoot, "Profiles");
    public static string Sessions => Path.Combine(DataRoot, "Sessions");
    public static string SessionFolder(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Ungültige Sitzungs-ID.", nameof(id));
        return Path.Combine(Sessions, id);
    }
    public static string ProfileFolder(string id) => Path.Combine(Profiles, Path.GetFileName(SessionFolder(id)));
}
