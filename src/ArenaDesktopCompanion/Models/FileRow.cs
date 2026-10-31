using ArenaDesktopCompanion.Core.Icons;

namespace ArenaDesktopCompanion.Models;

/// <summary>Ein Eintrag der Dateiliste. Der relative Pfad ist die Anzeigeeinheit –
/// nur so sieht man, dass <c>src/util.py</c> nicht <c>util.py</c> ist.
/// Das Symbol ist ein <see cref="IconKind"/> und keine Zeichenkette: eine Zeichenkette war
/// vorher ein Emoji (, ) und erschien auf Windows als leeres Kästchen.</summary>
public sealed record FileRow(string RelativePath, long SizeBytes, IconKind Icon)
{
 public string SizeText => SizeBytes < 1024
 ? $"{SizeBytes} B"
 : SizeBytes < 1024 * 1024
 ? $"{SizeBytes / 1024.0:0.#} KB"
 : $"{SizeBytes / (1024.0 * 1024.0):0.#} MB";
}
