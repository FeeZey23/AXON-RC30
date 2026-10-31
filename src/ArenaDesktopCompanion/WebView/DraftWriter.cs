using System.Text.Json;
using AXON.Connector;
using Microsoft.Web.WebView2.Core;
namespace ArenaDesktopCompanion.WebView;
public sealed record DraftResult(bool Ok,string Code,int Length);
public static class DraftWriter
{
    public static async Task<DraftResult> InsertAsync(CoreWebView2 core,string text,Uri address,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var result=JsonSerializer.Deserialize<DraftResult>(await core.ExecuteScriptAsync(DraftComposer.Script(text,address)).WaitAsync(token));
        if(result?.Ok!=true)return result??new(false,"no_result",0);
        await Task.Delay(250,token);
        return JsonSerializer.Deserialize<DraftResult>(await core.ExecuteScriptAsync(DraftComposer.Script(text,address,true)).WaitAsync(token))??new(false,"no_result",0);
    }
}
