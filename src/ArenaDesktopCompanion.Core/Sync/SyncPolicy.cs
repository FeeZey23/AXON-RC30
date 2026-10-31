using ArenaDesktopCompanion.Core.Conflicts;
using ArenaDesktopCompanion.Core.Diff;
using ArenaDesktopCompanion.Core.IO;

namespace ArenaDesktopCompanion.Core.Sync;

public enum SyncOutcome
{
    /// <summary>Datei neu geschrieben.</summary>
    Written,
    /// <summary>Inhalt identisch – kein Schreibvorgang (kein Log-Spam, kein Zeitstempel-Churn).</summary>
    Unchanged,
    /// <summary>Geschrieben, vorherige Version im Backup.</summary>
    UpdatedWithBackup,
    /// <summary>Lokal vom Nutzer veränderte Datei – neue Version liegt im Konfliktordner, Original heil.</summary>
    ConflictPreserved,
    /// <summary>Nur-Melden-Modus: Aktion würde stattfinden, wurde aber nicht ausgeführt.</summary>
    Planned,
    /// <summary>Aus Sicherheits- oder Größengründen abgelehnt.</summary>
    Blocked,
    /// <summary>Vom Filter übersprungen (Build-Ordner, Systemmüll …).</summary>
    Skipped,
    /// <summary>Meine Datei behalten, Importversion zusätzlich als <c>datei.arena.py</c>.</summary>
    KeptBoth,
    /// <summary>Warten auf eine Entscheidung (Vorlage in <c>.arena-conflicts/pending/decisions.json</c>).</summary>
    NeedsDecision,
    /// <summary>Nicht geschrieben, weil IO fehlgeschlagen ist.</summary>
    Failed
}

public sealed record SyncResult(
    SyncOutcome Outcome,
    string RelativePath,
    string Message,
    long Bytes = 0,
    string? BackupRelativePath = null,
    string? ConflictRelativePath = null,
    string? ArenaCopyRelativePath = null,
    string? PendingArenaCopyPath = null,
    string? PendingMineCopyPath = null)
{
    public bool WroteSomething => Outcome is SyncOutcome.Written or SyncOutcome.UpdatedWithBackup
        or SyncOutcome.ConflictPreserved or SyncOutcome.KeptBoth;
}

public sealed record SyncPolicyOptions
{
    /// <summary>„Nur melden“: alles planen, nichts schreiben (2.12).</summary>
    public bool ReportOnly { get; init; }
    /// <summary>Vor dem Überschreiben in Backup\&lt;zeitstempel&gt; sichern (ADR 002 Punkt 3).</summary>
    public bool KeepBackups { get; init; } = true;
    public int BackupRunsToKeep { get; init; } = 20;
    public long MaxFileBytes { get; init; } = 64L * 1024 * 1024;
    /// <summary>Maximales Verhältnis entpackt/gepackt als Zip-Bomben-Schutz.</summary>
    public long MaxCompressionRatio { get; init; } = 200;
    /// <summary>Gemeinsamen Top-Ordner im Zip entfernen (Arena-zips enthalten fast immer einen).</summary>
    public bool StripCommonArchiveRoot { get; init; } = true;
    /// <summary>Was passieren soll, wenn eine lokale Datei sich vom Import unterscheidet (Standard: nachfragen).</summary>
    public ConflictResolution ConflictResolution { get; init; } = ConflictResolution.Ask;
    /// <summary>Pfade, die selbst im Nur-Lesen-Fall nie angefasst werden (relativ, '/'-Trenner).</summary>
    public IReadOnlyList<string> AdditionalDenyPaths { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Der einzelne Entscheidungspunkt für jede Datei, die von außen hereinkommt.
/// Bewusst ohne jede UI- und Webabhängigkeit – hier wird das Verhalten getestet.
/// </summary>
public sealed class SyncPolicy
{
    /// <summary>
    /// Der Sicherungsordner im Projektordner. Bis rc23 hieß er <c>.arena-backup</c> und war damit
    /// versteckt; Wunsch vom 26.09.2026: sichtbar als <c>Backup</c>, direkt neben den Dateien.
    /// </summary>
    public const string BackupDirName = "Backup";

    /// <summary>Der alte, versteckte Name – wird nur noch übersprungen, nicht mehr benutzt.</summary>
    public const string BackupDirNameAlt = ".arena-backup";
    public const string ConflictDirName = ".arena-conflicts";
    public const string InboxDirName = ".arena-inbox";

    private readonly string _root;
    private readonly SyncPolicyOptions _options;
    private readonly string _indexFilePath;

    public SyncPolicy(string projectRoot, SyncPolicyOptions? options = null)
    {
        _root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _options = options ?? new SyncPolicyOptions();
        _indexFilePath = Path.Combine(_root, SyncIndex.FileName);
        Index = SyncIndex.Load(_indexFilePath);
    }

    public SyncIndex Index { get; }
    public string Root => _root;
    public long MaxFileBytes => _options.MaxFileBytes;
    public long MaxCompressionRatio => _options.MaxCompressionRatio;
    public bool ReportOnly => _options.ReportOnly;

    /// <summary>
    /// Setzt sich, wenn der Sync-Index nicht geschrieben werden konnte (schreibgeschützter Ordner).
    /// Die Datei selbst ist dann geschrieben – nur die Gedächtnisfunktion leidet.
    /// </summary>
    public string? IndexWarning => Index.LastError is null
        ? null
        : "Sync-Index nicht schreibbar (" + Index.LastError + ") – Konflikt-Erkennung ist eingeschränkt";
    public bool StripCommonArchiveRoot => _options.StripCommonArchiveRoot;
    public string IndexFilePath => _indexFilePath;

    /// <summary>Schreibt (oder plant) genau eine Datei. Nebenläufigkeitssicher über <see cref="_gate"/>.</summary>
    private readonly object _gate = new();

    public SyncResult Apply(string relativePathInput, string content, string source)
        => ApplyCore(relativePathInput, FileHash.ToBytes(content), source, null);

    /// <summary>Dieselbe Zeile Regelwerk, aber mit erzwungener Konfliktauflösung – genutzt von
    /// <see cref="DecisionApplier"/>, wenn der Nutzer in <c>decisions.json</c> geantwortet hat.</summary>
    public SyncResult Apply(string relativePathInput, string content, string source, ConflictResolution forceResolution)
        => ApplyCore(relativePathInput, FileHash.ToBytes(content), source, forceResolution);

    /// <summary>Schreibt einen Ableger (z. B. <c>.arena-conflicts/datei.arena.py</c>) – ohne Verzeichnisfilter,
    /// aber weiterhin mit Pfadschutz.</summary>
    public SyncResult ApplyUnfiltered(string relativePathInput, string content, string source)
    {
        if (!PathSafety.TryResolveInside(_root, relativePathInput, out var relative, out var full, out var error))
        {
            return new SyncResult(SyncOutcome.Blocked, relativePathInput, $"blockiert: {error}");
        }
        return SafeWrite(full, FileHash.ToBytes(content), out var writeError)
            ? new SyncResult(SyncOutcome.Written, relative, "zusätzliche Version geschrieben", content.Length)
            : new SyncResult(SyncOutcome.Failed, relative, writeError ?? "Schreiben fehlgeschlagen");
    }

    public SyncResult Apply(string relativePathInput, ReadOnlySpan<byte> bytes, string source)
        => ApplyCore(relativePathInput, bytes.ToArray(), source);

    private SyncResult ApplyCore(string relativePathInput, byte[] bytes, string source, ConflictResolution? forceResolution = null)
    {
        if (WorkspaceFileFilter.ShouldSkip(relativePathInput))
        {
            return new SyncResult(SyncOutcome.Skipped, relativePathInput, "übersprungen (Filter)");
        }

        if (!PathSafety.TryResolveInside(_root, relativePathInput, out var relative, out var full, out var error))
        {
            return new SyncResult(SyncOutcome.Blocked, relativePathInput, $"blockiert: {error}");
        }

        foreach (var deny in _options.AdditionalDenyPaths)
        {
            if (string.Equals(deny, relative, StringComparison.OrdinalIgnoreCase))
            {
                return new SyncResult(SyncOutcome.Blocked, relative, $"blockiert (gesperrt): {relative}");
            }
        }

        if (Directory.Exists(full))
        {
            return new SyncResult(SyncOutcome.Blocked, relative,
                "blockiert: im Projekt existiert ein Ordner gleichen Namens");
        }

        if (bytes.LongLength > _options.MaxFileBytes)
        {
            return new SyncResult(SyncOutcome.Blocked, relative,
                $"blockiert: {bytes.LongLength} Bytes über Limit {_options.MaxFileBytes}");
        }

        var newHash = FileHash.OfBytes(bytes);

        lock (_gate)
        {
            string? writeError;
            var exists = File.Exists(full);

            // Zweite Verteidigungslinie gegen Datenverlust: Ein (praktisch) leerer Inhalt darf eine
            // gefüllte Datei nicht leeren. Am 24.09.2026 hat der experimentelle Codeblock-Fanger
            // genau das getan – docs\BRUECKE.md ging von 19.672 auf 0 Bytes (siehe CHANGELOG 0.2.0-rc21).
            if (exists && !_options.ReportOnly && IstPraktischLeer(bytes))
            {
                var vorhanden = new FileInfo(full).Length;
                if (vorhanden > 0)
                {
                    return new SyncResult(SyncOutcome.Blocked, relative,
                        $"blockiert: leerer Inhalt – die vorhandene Datei ({vorhanden} Bytes) bleibt unangetastet");
                }
            }

            if (!exists)
            {
                if (_options.ReportOnly)
                {
                    return new SyncResult(SyncOutcome.Planned, relative, "würde neu angelegt", bytes.LongLength);
                }
                if (!SafeWrite(full, bytes, out writeError))
                {
                    return new SyncResult(SyncOutcome.Failed, relative, writeError ?? "Schreiben fehlgeschlagen");
                }
                Index.Set(relative, newHash, bytes.LongLength, source);
                Index.Save(_indexFilePath);
                return new SyncResult(SyncOutcome.Written, relative, "neu geschrieben", bytes.LongLength);
            }

            var diskHash = FileHash.OfFile(full);
            if (diskHash is null)
            {
                return new SyncResult(SyncOutcome.Failed, relative, "bestehende Datei nicht lesbar (gesperrt?)");
            }

            if (string.Equals(diskHash, newHash, StringComparison.OrdinalIgnoreCase))
            {
                // Inhaltlich gleich: nichts tun – aber Index nachziehen, falls die Datei extern entstanden ist.
                if (!Index.TryGet(relative, out _))
                {
                    Index.Set(relative, newHash, bytes.LongLength, source);
                    if (!_options.ReportOnly)
                    {
                        Index.Save(_indexFilePath);
                    }
                }
                return new SyncResult(SyncOutcome.Unchanged, relative, "unverändert", bytes.LongLength);
            }

            var wasSyncedByUs = Index.TryGet(relative, out var known)
                               && string.Equals(known!.Sha256, diskHash, StringComparison.OrdinalIgnoreCase);

            var diff = DiffSummary.Build(SafeReadText(full), SafeReadText(bytes), maxLines: 40);
            var diffNote = diff.Identical ? string.Empty : $" · {diff.Label}";

            if (wasSyncedByUs)
            {
                // Von uns geschrieben und wirklich neu: sichern, dann überschreiben.
                if (_options.ReportOnly)
                {
                    return new SyncResult(SyncOutcome.Planned, relative, "würde aktualisiert" + diffNote, bytes.LongLength);
                }
                string? backupRel = _options.KeepBackups
                    ? BackupRotator.Create(_root, relative, _options.BackupRunsToKeep)
                    : null;
                if (!SafeWrite(full, bytes, out writeError))
                {
                    return new SyncResult(SyncOutcome.Failed, relative, writeError ?? "Schreiben fehlgeschlagen");
                }
                Index.Set(relative, newHash, bytes.LongLength, source);
                Index.Save(_indexFilePath);
                return new SyncResult(
                    SyncOutcome.UpdatedWithBackup,
                    relative,
                    (backupRel is null ? "aktualisiert" : "aktualisiert, Vorversion gesichert") + diffNote,
                    bytes.LongLength,
                    BackupRelativePath: backupRel);
            }

            // Fremde oder veränderte Datei -> die Richtlinie entscheidet, nicht der Zufall.
            var resolution = forceResolution ?? _options.ConflictResolution;
            var decision = new ConflictPolicy(resolution).Decide(_root, relative, SafeReadText(bytes) ?? string.Empty);

            switch (decision.Kind)
            {
                case ConflictDecisionKind.Written:
                {
                    if (_options.ReportOnly)
                    {
                        return new SyncResult(SyncOutcome.Planned, relative,
                            "Konflikt: würde übernommen (mit Sicherung deiner Version)" + diffNote, bytes.LongLength);
                    }
                    var backupRel = BackupRotator.Create(_root, relative, _options.BackupRunsToKeep);
                    if (!SafeWrite(full, bytes, out writeError))
                    {
                        return new SyncResult(SyncOutcome.Failed, relative, writeError ?? "Schreiben fehlgeschlagen");
                    }
                    Index.Set(relative, newHash, bytes.LongLength, source);
                    Index.Save(_indexFilePath);
                    return new SyncResult(SyncOutcome.UpdatedWithBackup, relative,
                        "Konflikt gelöst: Import übernommen, deine Version gesichert" + diffNote,
                        bytes.LongLength, BackupRelativePath: backupRel);
                }

                case ConflictDecisionKind.KeptBoth:
                {
                    var arenaRelative = decision.ArenaCopyRelative ?? ConflictPolicy.ArenaCopyName(relative);
                    if (_options.ReportOnly)
                    {
                        return new SyncResult(SyncOutcome.Planned, relative,
                            $"Konflikt: würde als {arenaRelative} abgelegt" + diffNote, bytes.LongLength);
                    }
                    if (!SafeWrite(Path.Combine(_root, arenaRelative.Replace('/', Path.DirectorySeparatorChar)), bytes, out writeError))
                    {
                        return new SyncResult(SyncOutcome.Failed, relative, writeError ?? "Schreiben fehlgeschlagen");
                    }
                    return new SyncResult(SyncOutcome.KeptBoth, relative,
                        $"deine Version behalten, Import als {arenaRelative} abgelegt" + diffNote,
                        bytes.LongLength, ArenaCopyRelativePath: arenaRelative);
                }

                case ConflictDecisionKind.NeedsUser:
                {
                    if (_options.ReportOnly)
                    {
                        return new SyncResult(SyncOutcome.Planned, relative,
                            "Konflikt: Entscheidung würde angefragt" + diffNote, bytes.LongLength);
                    }
                    var mineRel = decision.MineBackupRelative!;
                    var arenaRel = (decision.ArenaCopyPath ?? string.Empty)
                        .Replace(_root + Path.DirectorySeparatorChar, string.Empty).Replace('\\', '/');
                    SafeWrite(Path.Combine(_root, mineRel.Replace('/', Path.DirectorySeparatorChar)),
                        SafeReadBytes(full) ?? Array.Empty<byte>(), out _);
                    SafeWrite(Path.Combine(_root, arenaRel.Replace('/', Path.DirectorySeparatorChar)), bytes, out _);
                    DecisionApplier.AddRequest(_root, relative, SafeReadText(full) ?? string.Empty,
                        SafeReadText(bytes) ?? string.Empty, mineRel, arenaRel);
                    return new SyncResult(SyncOutcome.NeedsDecision, relative,
                        "Konflikt: wartet auf Entscheidung in .arena-conflicts\\pending\\decisions.json" + diffNote,
                        bytes.LongLength,
                        PendingMineCopyPath: Path.Combine(_root, mineRel.Replace('/', Path.DirectorySeparatorChar)),
                        PendingArenaCopyPath: Path.Combine(_root, arenaRel.Replace('/', Path.DirectorySeparatorChar)));
                }

                default:
                {
                    var conflictRelative = decision.ArenaCopyRelative ?? $"{ConflictDirName}/{DateTimeOffset.Now:yyyyMMdd-HHmmss}/{relative}";
                    if (_options.ReportOnly)
                    {
                        return new SyncResult(SyncOutcome.Planned, relative,
                            $"Konflikt: Importversion würde als {conflictRelative} abgelegt" + diffNote, bytes.LongLength);
                    }
                    if (!SafeWrite(Path.Combine(_root, conflictRelative.Replace('/', Path.DirectorySeparatorChar)), bytes, out writeError))
                    {
                        return new SyncResult(SyncOutcome.Failed, relative, writeError ?? "Schreiben fehlgeschlagen");
                    }
                    return new SyncResult(SyncOutcome.ConflictPreserved, relative,
                        "Konflikt: deine Datei behalten, Importversion abgelegt" + diffNote,
                        bytes.LongLength, ConflictRelativePath: conflictRelative);
                }
            }
        }
    }

    private static string? SafeReadText(string path)
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

    private static byte[]? SafeReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? SafeReadText(byte[] bytes)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Index-Eintrag entfernen (nachdem die Datei gelöscht wurde) – sonst würde der
    /// nächste Import einen Konflikt melden, obwohl nichts mehr da ist.</summary>
    public void Forget(string relativePath)
    {
        lock (_gate)
        {
            if (Index.TryGet(relativePath, out _))
            {
                Index.Remove(relativePath);
                Index.Save(_indexFilePath);
            }
        }
    }

    /// <summary>Derselbe Ablauf für eine fertige Datei auf der Platte (Import aus der Inbox).</summary>
    /// <summary>
    /// Leer oder nur aus Leerzeichen/Zeilenumbrüchen (max. 4 KB): kein Inhalt, sondern ein
    /// verstümmelter Block. Schützt davor, dass eine gefüllte Datei geleert wird.
    /// </summary>
    private static bool IstPraktischLeer(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return true;
        }
        if (bytes.Length > 4096)
        {
            return false;
        }

        foreach (var b in bytes)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }

        return true;
    }

    public SyncResult ApplyFromFile(string relativePathInput, string sourceFilePath, string source)
    {
        try
        {
            var info = new FileInfo(sourceFilePath);
            if (info.Length > _options.MaxFileBytes)
            {
                return new SyncResult(SyncOutcome.Blocked, relativePathInput,
                    $"blockiert: Datei {info.Length} Bytes über Limit {_options.MaxFileBytes}");
            }
            return ApplyCore(relativePathInput, File.ReadAllBytes(sourceFilePath), source, null);
        }
        catch (Exception ex)
        {
            return new SyncResult(SyncOutcome.Failed, relativePathInput, $"Quelle nicht lesbar: {ex.Message}");
        }
    }

    private bool SafeWrite(string fullPath, byte[] bytes, out string? error)
    {
        error = null;
        var dir = Path.GetDirectoryName(fullPath)!;
        var temp = Path.Combine(dir, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, fullPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Schreiben fehlgeschlagen: {ex.Message}";
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException) { /* nur Aufräumen */ }
            return false;
        }
    }
}
