using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion.Core.Conflicts;

/// <summary>Was tun, wenn eine Datei im Projekt sich von der importierten unterscheidet?</summary>
public enum ConflictResolution
{
    /// <summary>Nichts überschreiben, beides liegen lassen und eine Entscheidung anfragen (Standard).</summary>
    Ask = 0,
    /// <summary>Arena-Version übernehmen, meine Version nach <c>Backup/</c>.</summary>
    ArenaWins = 1,
    /// <summary>Meine Version behalten, Arena-Version nach <c>.arena-conflicts</c> ablegen.</summary>
    KeepMine = 2,
    /// <summary>Meine Version behalten, Arena-Version zusätzlich als <c>datei.arena.py</c> ablegen.</summary>
    KeepBoth = 3
}

public enum ConflictDecisionKind
{
    /// <summary>Kein Konflikt (Ziel existiert nicht oder Inhalt ist gleich).</summary>
    NoConflict,
    /// <summary>Zieldatei darf geschrieben werden.</summary>
    Written,
    /// <summary>Eigene Datei behalten, Importversion im Konfliktordner.</summary>
    KeptMine,
    /// <summary>Eigene Datei behalten, Importversion zusätzlich mit <c>.arena</c>-Namenszusatz.</summary>
    KeptBoth,
    /// <summary>Nutzer muss entscheiden – Anfrage liegt in <c>decisions.json</c>.</summary>
    NeedsUser
}

public sealed record ConflictDecision(
    ConflictDecisionKind Kind,
    string RelativePath,
    string? ArenaCopyRelative = null,
    string? MineBackupRelative = null,
    string? RequestFile = null,
    string? MineCopyPath = null,
    string? ArenaCopyPath = null)
{
    public bool NeedsUser => Kind == ConflictDecisionKind.NeedsUser;

    /// <summary>Der Aufrufer darf die Zieldatei schreiben (und sichert sie ggf. über <see cref="MineBackupRelative"/>).</summary>
    public bool TargetMayBeWritten => Kind == ConflictDecisionKind.Written;
}

/// <summary>
/// Die Konfliktauswahl an einem Ort – vorher war sie hart in <see cref="SyncPolicy"/> verdrahtet,
/// wodurch es keine Wahl gab und die UI nichts zum Anzeigen hatte.
/// Diese Klasse schreibt nichts an der Zieldatei, sie entscheidet und benennt die Zielpfade.
/// </summary>
public sealed class ConflictPolicy
{
    public const string PendingDirName = "pending";
    public const string FilesDirName = "files";
    public const string TemplateFileName = "decisions.json";

    public ConflictPolicy(ConflictResolution resolution) => Resolution = resolution;

    public ConflictResolution Resolution { get; }

    public static string PendingDirectory(string root)
        => Path.Combine(root, SyncPolicy.ConflictDirName, PendingDirName);

    public static string FilesDirectory(string root)
        => Path.Combine(PendingDirectory(root), FilesDirName);

    public static string TemplatePath(string root)
        => Path.Combine(PendingDirectory(root), TemplateFileName);

    /// <summary>Grundname für die Zweitversion: <c>a/b/main.py</c> → <c>a/b/main.arena.py</c>.</summary>
    public static string ArenaCopyName(string relativePath)
        => WithSuffix(relativePath, ".arena");

    internal static string WithSuffix(string relativePath, string suffix)
    {
        var dir = relativePath.Contains('/') ? relativePath[..relativePath.LastIndexOf('/')] : string.Empty;
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var ext = Path.GetExtension(relativePath);
        return string.IsNullOrEmpty(dir) ? name + suffix + ext : $"{dir}/{name}{suffix}{ext}";
    }

    public ConflictDecision Decide(string root, string relativePath, string newContent)
    {
        var fullRoot = Path.GetFullPath(root);
        var target = Path.GetFullPath(Path.Combine(new[] { fullRoot }.Concat(relativePath.Split('/')).ToArray()));
        if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            // Dasselbe Netz wie PathSafety – hier nur als Doppelprüfung, der Aufrufer hat bereits validiert.
            return new ConflictDecision(ConflictDecisionKind.KeptMine, relativePath);
        }

        if (Directory.Exists(target))
        {
            // Ein Ordner an der Stelle ist kein Ziel für eine Datei.
            return new ConflictDecision(ConflictDecisionKind.KeptMine, relativePath);
        }

        if (!File.Exists(target))
        {
            return new ConflictDecision(ConflictDecisionKind.Written, relativePath);
        }

        var mine = SafeRead(target);
        if (mine is null)
        {
            return new ConflictDecision(ConflictDecisionKind.KeptMine, relativePath);
        }
        if (string.Equals(mine, newContent, StringComparison.Ordinal))
        {
            return new ConflictDecision(ConflictDecisionKind.NoConflict, relativePath);
        }

        switch (Resolution)
        {
            case ConflictResolution.ArenaWins:
                return new ConflictDecision(
                    ConflictDecisionKind.Written,
                    relativePath,
                    MineBackupRelative: BackupPath(relativePath));

            case ConflictResolution.KeepMine:
                return new ConflictDecision(
                    ConflictDecisionKind.KeptMine,
                    relativePath,
                    ArenaCopyRelative: $"{SyncPolicy.ConflictDirName}/{DateTimeOffset.Now:yyyyMMdd-HHmmss}/{relativePath}");

            case ConflictResolution.KeepBoth:
                return new ConflictDecision(
                    ConflictDecisionKind.KeptBoth,
                    relativePath,
                    ArenaCopyRelative: FreeArenaCopy(root, relativePath));

            default:
                var id = RequestId.New();
                var mineRel = $"{SyncPolicy.ConflictDirName}/{PendingDirName}/{FilesDirName}/{id}.mine";
                var arenaRel = $"{SyncPolicy.ConflictDirName}/{PendingDirName}/{FilesDirName}/{id}.arena";
                return new ConflictDecision(
                    ConflictDecisionKind.NeedsUser,
                    relativePath,
                    MineBackupRelative: mineRel,
                    RequestFile: TemplatePath(root),
                    MineCopyPath: Path.Combine(root, mineRel.Replace('/', Path.DirectorySeparatorChar)),
                    ArenaCopyPath: Path.Combine(root, arenaRel.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    private static string FreeArenaCopy(string root, string relativePath)
    {
        var candidate = ArenaCopyName(relativePath);
        for (var n = 2; Exists(root, candidate); n++)
        {
            candidate = WithSuffix(relativePath, $".arena-{n}");
        }
        return candidate;
    }

    private static bool Exists(string root, string relative)
        => File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string BackupPath(string relativePath)
        => $"{SyncPolicy.BackupDirName}/{DateTimeOffset.Now:yyyyMMdd-HHmmss}/{relativePath}";

    private static string? SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
