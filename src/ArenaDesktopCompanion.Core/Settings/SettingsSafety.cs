using System.Text.Json;
using System.Text.Json.Nodes;
namespace ArenaDesktopCompanion.Core.Settings;
public static class SettingsSafety
{
    public static JsonObject Parse(string json)
    {
        var root=JsonNode.Parse(json) as JsonObject??throw new InvalidDataException("Einstellungswurzel muss ein Objekt sein.");
        foreach(var key in new[]{"workspaces","closedWorkspaces","pendingTabCleanup"})
            if(root.TryGetPropertyValue(key,out var value)&&value is not JsonArray)throw new InvalidDataException("Ungültige Liste: "+key);
        if(root["workspaces"] is JsonArray tabs)
        {
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var t in tabs){ValidateTab(t);if(t?["id"] is{} id&&!ids.Add(id.ToString()))throw new InvalidDataException("Doppelte Tab-ID.");}
        }
        if(root["closedWorkspaces"] is JsonArray closed)foreach(var c in closed){if(c is not JsonObject o||o["Tab"] is null&&o["tab"] is null)throw new InvalidDataException("Ungültiger Verlaufseintrag.");ValidateTab(o["tab"]??o["Tab"]);}
        return root;
    }
    private static void ValidateTab(JsonNode? node)
    {
        if(node is not JsonObject t)throw new InvalidDataException("Ungültiger Tab.");
        if(t.TryGetPropertyValue("id",out var id)&&(id is not JsonValue value||!value.TryGetValue<string>(out var text)||!Guid.TryParseExact(text,"N",out _)))throw new InvalidDataException("Ungültige Tab-ID.");
        foreach(var key in new[]{"id","name","group","connectionId","connectedChatAddress","chatAddress"})if(t.TryGetPropertyValue(key,out var v)&&v is null)throw new InvalidDataException("Ungültiger Tabwert: "+key);
        if(t.TryGetPropertyValue("panels",out var panels)){if(panels is not JsonArray a)throw new InvalidDataException("Ungültige Panelliste.");foreach(var p in a)if(p is not JsonObject o||o.TryGetPropertyValue("kind",out var k)&&k is null)throw new InvalidDataException("Ungültiges Panel.");}
    }
    public static void MergeMissing(JsonObject current,JsonObject saved)
    {
        foreach(var p in saved)if(!current.ContainsKey(p.Key))current[p.Key]=p.Value?.DeepClone();
        if(current["workspaces"] is JsonArray now&&saved["workspaces"] is JsonArray old)
            foreach(var n in now.OfType<JsonObject>()){var id=n["id"]?.ToString();var previous=old.OfType<JsonObject>().FirstOrDefault(o=>o["id"]?.ToString()==id);if(previous is not null)foreach(var p in previous)if(!n.ContainsKey(p.Key))n[p.Key]=p.Value?.DeepClone();}
    }
}
