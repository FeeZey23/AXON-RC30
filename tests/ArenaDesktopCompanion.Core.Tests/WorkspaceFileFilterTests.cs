using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Import;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;
using ArenaDesktopCompanion.Core.Tests.Support;
using System.Reflection;

namespace ArenaDesktopCompanion.Core.Tests;

public class WorkspaceFileFilterTests
{
    [Theory]
    [InlineData(".git/config")]
    [InlineData("src/.git/config")]
    [InlineData("node_modules/pkg/index.js")]
    [InlineData("__pycache__/mod.pyc")]
    [InlineData("__MACOSX/._datei")]
    [InlineData(".DS_Store")]
    [InlineData("Thumbs.db")]
    [InlineData("bin/Debug/app.dll")]
    [InlineData(".arena-backup/2020/a.py")]
    [InlineData("Backup/2020/a.py")]
    [InlineData("")]
    [InlineData("ordner/")]
    public void Wird_uebersprungen(string path) => Assert.True(WorkspaceFileFilter.ShouldSkip(path));

    [Theory]
    [InlineData("src/app.py")]
    [InlineData("README.md")]
    [InlineData(".env")]
    [InlineData("arena-notes.txt")]
    [InlineData("src/bin.py")]
    public void Wird_nicht_uebersprungen(string path) => Assert.False(WorkspaceFileFilter.ShouldSkip(path));
}

/// <summary>ADR 004: die riskante Logik lebt im Core und muss ohne Windows ausführbar bleiben.</summary>
