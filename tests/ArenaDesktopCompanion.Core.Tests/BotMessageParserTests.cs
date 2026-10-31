using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;
using System.Reflection;

namespace ArenaDesktopCompanion.Core.Tests;

public class BotMessageParserTests
{
    // Klassische escapte Strings statt Raw-Literals: JSON steckt voller aufeinanderfolgender Anführungszeichen.
    private const string Valid = "{\"action\":\"SYNC_FILE\",\"fileName\":\"src/a.py\",\"content\":\"print(1)\\n\"}";

    [Fact]
    public void Gueltige_Nachricht_wird_gelesen()
    {
        Assert.True(BotMessageParser.TryParse(Valid, out var message, out var error), error);
        Assert.Equal("src/a.py", message!.FileName);
        Assert.Equal("print(1)\n", message.Content);
        Assert.Equal(BotMessage.SyncFileAction, message.Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"action\":\"OTHER\",\"fileName\":\"a.py\",\"content\":\"x\"}")]
    [InlineData("{\"fileName\":\"a.py\",\"content\":\"x\"}")]

    [InlineData("{\"action\":\"SYNC_FILE\",\"fileName\":\"a.py\"}")]
    [InlineData("{\"action\":\"SYNC_FILE\",\"fileName\":42,\"content\":\"x\"}")]
    [InlineData("{\"action\":\"SYNC_FILE\",\"fileName\":\"a.py\",\"content\":{}}")]

    public void Alles_Unerwartete_wird_abgewehrt(string? json)
    {
        Assert.False(BotMessageParser.TryParse(json, out var message, out var error));
        Assert.Null(message);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Leerer_FileName_wird_als_kein_Name_gelesen()
    {
        const string json = "{\"action\":\"SYNC_FILE\",\"fileName\":\"  \",\"content\":\"x\"}";
        Assert.True(BotMessageParser.TryParse(json, out var message, out _));
        Assert.Equal("  ", message!.FileName);   // weitergereicht; CleanCandidate macht daraus nichts
    }

    [Fact]
    public void Ohne_FileName_ist_erlaubt_der_Pfad_kommt_aus_dem_Inhalt()
    {
        const string json = "{\"action\":\"SYNC_FILE\",\"content\":\"# path: a.py\\nprint(1)\"}";
        Assert.True(BotMessageParser.TryParse(json, out var message, out var error), error);
        Assert.Equal(string.Empty, message!.FileName);
        Assert.Contains("# path: a.py", message.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ubergrosser_Inhalt_wird_abgewiesen()
    {
        var json = "{\"action\":\"SYNC_FILE\",\"fileName\":\"a.py\",\"content\":\"" + new string('x', 5000) + "\"}";
        Assert.False(BotMessageParser.TryParse(json, out _, out var error, maxContentBytes: 1000));
        Assert.Contains("Limit", error);
    }
}
