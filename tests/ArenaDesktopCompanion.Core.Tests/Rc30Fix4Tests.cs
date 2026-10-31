using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Tests.Support;
namespace ArenaDesktopCompanion.Core.Tests;
public class Rc30Fix4Tests
{
    [Theory][InlineData("{\"workspaces\":null}")][InlineData("{\"workspaces\":[null]}")][InlineData("{\"workspaces\":[{\"panels\":null}]}")][InlineData("{\"workspaces\":[{\"name\":null}]}")][InlineData("{\"closedWorkspaces\":[null]}")][InlineData("{\"closedWorkspaces\":[{\"tab\":null}]}")][InlineData("{\"pendingTabCleanup\":null}")][InlineData("[]")]
    [InlineData("{\"workspaces\":[{\"id\":\"../bad\"}]}")]
    [InlineData("{\"workspaces\":[{\"id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"},{\"id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]}")]
    public void Broken_structure_recovers_the_same_ids_from_last_good(string broken)
    {
        using var t=new TempTree();var store=new AppSettingsStore(t.Root);var s=new AppSettings();s.ArbeitsbereichAnlegen();var id=s.Arbeitsbereiche[0].Id;store.Save(s);File.WriteAllText(store.FilePath,broken);var restored=store.Load();Assert.True(store.LastLoadWasCorrupt);Assert.True(store.RestoredLastGood);Assert.Equal(id,restored.Arbeitsbereiche[0].Id);Assert.NotEmpty(Directory.GetFiles(t.Root,"*.corrupt-*"));
    }
    [Fact]public void Missing_file_recovers_same_identity_from_last_good(){using var t=new TempTree();var store=new AppSettingsStore(t.Root);var s=new AppSettings();s.ArbeitsbereichAnlegen();store.Save(s);File.Delete(store.FilePath);var restored=store.Load();Assert.True(store.RestoredLastGood);Assert.Equal(s.Arbeitsbereiche[0].Id,restored.Arbeitsbereiche[0].Id);Assert.NotNull(store.RecoveryNotice);}
    [Fact]public void No_backup_reports_recovery_loss_instead_of_claiming_original_identity(){using var t=new TempTree();var store=new AppSettingsStore(t.Root);File.WriteAllText(store.FilePath,"{\"workspaces\":null}");Assert.Empty(store.Load().Arbeitsbereiche);Assert.False(store.RestoredLastGood);Assert.NotNull(store.RecoveryNotice);}
    [Fact]public void Rollback_sidecar_restores_only_missing_fields_not_user_changes()
    {
        using var t=new TempTree();var store=new AppSettingsStore(t.Root);var s=new AppSettings();s.ArbeitsbereichAnlegen();TabHistory.Close(s,s.Arbeitsbereiche[0].Id,DateTimeOffset.UtcNow);store.Save(s);File.Copy(store.FilePath,store.ForwardPath);var old=JsonNode.Parse(File.ReadAllText(store.FilePath))!.AsObject();old.Remove("closedWorkspaces");old["workspaces"]![0]!["name"]="Changed in old version";old["workspaces"]![0]!.AsObject().Remove("autoChatName");File.WriteAllText(store.FilePath,old.ToJsonString());var restored=store.Load();Assert.Single(restored.ClosedWorkspaces);Assert.Equal("Changed in old version",restored.Arbeitsbereiche[0].Name);Assert.True(restored.Arbeitsbereiche[0].AutoChatName);
    }
    [Fact]public void Explicit_empty_history_is_not_resurrected_by_sidecar(){var now=SettingsSafety.Parse("{\"closedWorkspaces\":[]}");var prior=SettingsSafety.Parse("{\"closedWorkspaces\":[{\"tab\":{}}]}");SettingsSafety.MergeMissing(now,prior);Assert.Empty(now["closedWorkspaces"]!.AsArray());}
    [Theory][InlineData(-30000,-30000)][InlineData(30000,30000)][InlineData(800,900)]
    public void Removed_monitor_restores_fully_inside_work_area(double x,double y){var r=PanelPlacement.Clamp(x,y,680,400,[new(0,0,1920,1080)]);Assert.InRange(r.Left,0,1240);Assert.InRange(r.Top,0,680);}
    [Fact]public void Negative_secondary_coordinates_are_kept_if_real(){var r=PanelPlacement.Clamp(-1100,20,500,400,[new(0,0,1920,1080),new(-1280,0,1280,1024)]);Assert.Equal(-1100,r.Left);}
    [Fact]public void Gaps_between_monitors_are_not_valid_positions(){var r=PanelPlacement.Clamp(2050,20,500,400,[new(0,0,1920,1080),new(2200,0,1920,1080)]);Assert.True(r.Left<1920||r.Left>=2200);}
    [Theory][InlineData(1d)][InlineData(1.25d)][InlineData(1.5d)][InlineData(2d)]
    public void Dpi_scaled_work_areas_preserve_fully_visible_bounds(double scale){var w=1920/scale;var h=1080/scale;var r=PanelPlacement.Clamp(1800,900,680,400,[new(0,0,w,h)]);Assert.True(r.Left+r.Width<=w);Assert.True(r.Top+r.Height<=h);}
    [Fact]public void Unknown_future_fields_survive_save(){using var t=new TempTree();var store=new AppSettingsStore(t.Root);File.WriteAllText(store.FilePath,"{\"futureField\":{\"x\":5},\"workspaces\":[]}");store.Save(store.Load());Assert.Equal(5,JsonNode.Parse(File.ReadAllText(store.FilePath))!["futureField"]!["x"]!.GetValue<int>());}
}
