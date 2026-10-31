namespace ArenaDesktopCompanion.Core.Conflicts;

/// <summary>Kurze, ungefährliche Id für Anfrage-Dateien (keine Pfad-Rückkodierung nötig).</summary>
public static class RequestId
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
