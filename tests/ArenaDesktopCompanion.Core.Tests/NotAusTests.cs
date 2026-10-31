using System.Text.Json;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Bridge;
namespace ArenaDesktopCompanion.Core.Tests;
public class NotAusTests
{
    [Fact] public void Rc30_has_no_emergency_stop_setting() => Assert.Null(typeof(AppSettings).GetProperty("NotAus"));
    [Fact] public void Rc30_has_no_emergency_stop_service_api() => Assert.Null(typeof(BridgeService).GetMethod("NotAusSetzen"));
    [Fact] public void Old_stop_flag_does_not_suspend_rc30() => Assert.DoesNotContain("notAus",JsonSerializer.Serialize(JsonSerializer.Deserialize<AppSettings>("{\"notAus\":true}")));
    [Fact] public void Rc30_uses_distinct_settings_root() => Assert.EndsWith(Path.Combine("AXON","RC30"),AppSettingsStore.DefaultDirectory);
}
