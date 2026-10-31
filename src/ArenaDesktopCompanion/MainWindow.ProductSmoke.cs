using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Product;
using ArenaDesktopCompanion.Workspace;
namespace ArenaDesktopCompanion;
public partial class MainWindow
{
    private async Task RunProductSmokeAsync(TabSession owner,List<string> checks)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add("Product: "+label);}
        var originalLanguage=_settings.UiLanguage;_settings.UiLanguage="en";Product.UiText.Apply(this);Check(ProjectFilesButton.Content.ToString()=="Project files","Language English applied to actual toolbar");Check(Product.UiText.Translate("Keine geschlossenen Tabs")=="No closed tabs","English history actions localized");var userLabel=new System.Windows.Controls.TextBlock{Text="Einstellungen",Tag="UserContent"};Product.UiText.Apply(userLabel);Check(userLabel.Text=="Einstellungen","Localization never renames user-owned labels");_settings.UiLanguage="de";Product.UiText.Apply(this);Check(ProjectFilesButton.Content.ToString()=="Projektdateien","German toolbar restored");_settings.UiLanguage=originalLanguage;
        var origin=Path.Combine(Rc30Paths.DataRoot,"test-origin");
        using(var ephemeral=new TabSession(new Arbeitsbereich{RememberLogin=false},App.Services))
        {
            ephemeral.BrowserCreated+=b=>BrowserHost.Children.Add(b);
            await ephemeral.EnsureBrowserAsync().WaitAsync(TimeSpan.FromSeconds(25));Check(ephemeral.Browser!.CoreWebView2.Profile.IsInPrivateModeEnabled,"Do-not-remember uses a real WebView2 InPrivate profile");
            BrowserHost.Children.Remove(ephemeral.Browser);
        }
        var navigation=owner.Navigation!;var core=owner.Browser!.CoreWebView2;var before=core.Source;
        var seen=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Map(CoreWebView2 popup){popup.SetVirtualHostNameToFolderMapping("external.axon.test",origin,CoreWebView2HostResourceAccessKind.DenyCors);popup.NavigationCompleted+=(_,e)=>{if(e.IsSuccess)seen.TrySetResult();};}
        navigation.PopupCreated+=Map;
        await navigation.OpenLinkAsync("https://external.axon.test/index.html");await seen.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Check(core.Source==before&&navigation.Windows.Count==1,"External link opens another native WebView without leaving Arena main view");
        foreach(var w in navigation.Windows.ToArray())w.Close();
        var opened=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void NativePopup(CoreWebView2 popup){popup.SetVirtualHostNameToFolderMapping("external.axon.test",origin,CoreWebView2HostResourceAccessKind.DenyCors);popup.NavigationCompleted+=async(_,e)=>{if(e.IsSuccess){var has=await popup.ExecuteScriptAsync("window.opener !== null");if(has=="true")opened.TrySetResult();else opened.TrySetException(new Exception("OAuth opener lost"));}};}
        navigation.PopupCreated-=Map;navigation.PopupCreated+=NativePopup;
        await core.ExecuteScriptAsync("window.open('https://external.axon.test/index.html','axon-oauth-test')");await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Check(core.Source==before,"Native NewWindowRequested keeps OAuth opener and original Arena navigation");
        foreach(var w in navigation.Windows.ToArray())w.Close();navigation.PopupCreated-=NativePopup;
        Check(Title.EndsWith(ProductPolicy.CurrentVersion.ToString(3),StringComparison.Ordinal),"Window title uses central assembly release version");
    }
}
