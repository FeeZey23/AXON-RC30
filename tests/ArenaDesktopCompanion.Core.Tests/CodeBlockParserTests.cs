using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;

namespace ArenaDesktopCompanion.Core.Tests;

public class CodeBlockParserTests
{
    [Theory]
    // Die drei im alten Projekt beworbenen Schreibweisen – sie müssen weiterhin funktionieren
    [InlineData("// path: script.py\nprint(1)", "script.py")]
    [InlineData("# filename: app.py\nx = 1", "app.py")]
    [InlineData("/* file: index.html */\n<h1/>", "index.html")]
    // Und die vier Fälle, in denen die alte Regex versagt hat (Audit P0-3)
    [InlineData("```python\n# path: app.py\nprint(1)\n```", "app.py")]
    [InlineData("// path: **app.py**\ncode()", "app.py")]
    [InlineData("src/app.py\nprint(1)", "src/app.py")]
    [InlineData("Datei: src/straße.py\nx = 1", "src/straße.py")]
    [InlineData("some prose\nmore prose\n\n# save to: notes/todo.md\ntext", "notes/todo.md")]
    [InlineData("main.py – Hauptdatei\nprint(1)", "main.py")]
    [InlineData("Speichere das als `src/util.py`.\nx = 1", "src/util.py")]
    public void Erkennt_Dateinamen(string text, string expectedPath)
    {
        Assert.True(CodeBlockParser.TryParse(text, out var file, out var error), error);
        Assert.Equal(expectedPath, file!.RelativePath);
    }

    [Theory]
    [InlineData("```python\n# path: app.py\nprint(1)\n```", "print(1)\n")]
    [InlineData("// path: app.py\nprint(1)", "print(1)\n")]
    [InlineData("# file: app.py\r\nprint(1)\r\n", "print(1)\n")]
    public void Kopf_und_Fences_sind_nicht_mehr_im_Inhalt(string text, string expectedContent)
    {
        // P0-Punkt „Header landet in der geretteten Datei“ – hier wird er entfernt.
        Assert.True(CodeBlockParser.TryParse(text, out var file));
        Assert.Equal(expectedContent, file!.Content);
        Assert.DoesNotContain("path:", file.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```", file.Content, StringComparison.Ordinal);
        Assert.True(file.HeaderLineRemoved);
    }

    [Theory]
    // Was ausdrücklich kein Dateiname sein darf
    [InlineData("print('hallo welt')")]
    [InlineData("")]
    [InlineData("   \n   ")]
    [InlineData("x = 1 (kommentar)")]
    [InlineData("TODO: aufraeumen")]
    public void Keine_Datei_erkannt(string text)
    {
        Assert.False(CodeBlockParser.TryParse(text, out var file, out var error));
        Assert.Null(file);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Prosa_mit_Erwahnung_ist_kein_Treffer()
    {
        // "siehe README.md" war im alten Stand ein Risiko: ganzer Block als README.md gerettet.
        Assert.False(CodeBlockParser.TryParse("siehe README.md", out _));
    }

    [Theory]
    [InlineData("# path: ../outside.py\ncode")]
    [InlineData("// file: C:\\proj\\app.py\ncode")]
    [InlineData("/* path: /etc/passwd */\ncode")]
    public void Gefaehrliche_Pfade_werden_nicht_silent_repariert(string text)
    {
        Assert.False(CodeBlockParser.TryParse(text, out _, out var error));
        Assert.Contains("blockiert", error);
    }

    [Fact]
    public void Datei_in_Tieferer_Verschachtelung()
    {
        var text = "// path: a/b/c/d/e/f.py\npayload\n";
        Assert.True(CodeBlockParser.TryParse(text, out var file));
        Assert.Equal("a/b/c/d/e/f.py", file!.RelativePath);
        Assert.Equal("payload\n", file.Content);
    }

    [Fact]
    public void Header_am_Ende_eines_Fences_mit_Mehrzeiler_inhalt()
    {
        const string text = "```js\n/* filename: app.js */\nconst a = 1;\nconst b = 2;\n```";
        Assert.True(CodeBlockParser.TryParse(text, out var file));
        Assert.Equal("app.js", file!.RelativePath);
        Assert.Equal("const a = 1;\nconst b = 2;\n", file.Content);
    }

    // --- 24.09.2026: Feldrunde des Codeblock-Fangers ----------------------------------------
    // Der Fanger legte im Projekt leere Dateien an und leerte docs\BRUECKE.md. Diese Fälle sind
    // die Namen, die dabei wirklich entstanden sind – sie dürfen nie wieder ein Ziel werden.

    [Theory]
    [InlineData("Dispatcher.Invoke")]
    [InlineData("BridgeHost.AufUi")]
    [InlineData("System.IO.File.ReadAllText")]
    [InlineData("node --check arena-bot.js")]
    [InlineData("cmd/c ping -n 121 127.0.0.1")]
    [InlineData("```\nDispatcher.Invoke\n```")]
    public void Befehle_und_Mitgliedszugriffe_sind_keine_Dateinamen(string text)
    {
        Assert.False(CodeBlockParser.TryParse(text, out var file, out var error));
        Assert.Null(file);
        Assert.Contains("Kein Dateiname", error);
    }

    [Theory]
    [InlineData("```\nSecretRedaction.cs\n```")]
    [InlineData("```\nlandschaft_alpen.png\n```")]
    [InlineData("landschaft_alpen.png - ein Bild")]
    [InlineData("// path: leer.py")]
    [InlineData("Speichere das als `src/util.py`.\n")]
    public void Block_ohne_Inhalt_wird_nicht_gespeichert(string text)
    {
        Assert.False(CodeBlockParser.TryParse(text, out var file, out var error));
        Assert.Null(file);
        Assert.Contains("keinen Dateiinhalt", error);
    }

    [Fact]
    public void Fence_mit_Sprachangabe_und_Pfad_im_Infostring()
    {
        // Bis rc20 wirkungslos: StripFence lieferte nur bei einer schließenden Fence etwas.
        const string text = "```python path: app.py\nprint(1)\n```";
        Assert.True(CodeBlockParser.TryParse(text, out var file), "Infostring ```python path: app.py");
        Assert.Equal("app.py", file!.RelativePath);
        Assert.Equal("print(1)\n", file.Content);
    }

    [Fact]
    public void Fence_mit_reinem_Dateinamen_als_Infostring()
    {
        const string text = "```src/util.py\nx = 1\n```";
        Assert.True(CodeBlockParser.TryParse(text, out var file));
        Assert.Equal("src/util.py", file!.RelativePath);
        Assert.Equal("x = 1\n", file.Content);
    }
}
