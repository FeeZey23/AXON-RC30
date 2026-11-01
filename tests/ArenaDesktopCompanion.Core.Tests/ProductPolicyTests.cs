using ArenaDesktopCompanion.Core.Product;
using ArenaDesktopCompanion.Core.Settings;
using System.Text.Json;
namespace ArenaDesktopCompanion.Core.Tests;
public sealed class ProductPolicyTests
{
    [Theory][InlineData("https://arena.ai/agent",true)][InlineData("https://www.arena.ai/login",true)][InlineData("https://arena.ai.evil.test",false)][InlineData("https://evil.test/arena.ai",false)][InlineData("http://arena.ai",false)][InlineData("https://arena.ai:8443",false)][InlineData("https://user@arena.ai",false)]
    public void Main_navigation_only_allows_real_https_arena(string uri,bool allowed)=>Assert.Equal(allowed,ProductPolicy.IsArena(uri));
    [Theory][InlineData("https://github.com/owner/project")][InlineData("https://github.com/owner/project.git")]
    public void Repository_url_is_canonical(string uri)=>Assert.Equal("owner/project",ProductPolicy.ParseRepository(uri));
    [Theory][InlineData("https://github.com.evil.test/a/b")][InlineData("https://github.com/a/b/tree/main")][InlineData("file:///C:/a/b")][InlineData("https://user@github.com/a/b")][InlineData("https://github.com/a/b?token=private")][InlineData("https://github.com/a/b#branch")]
    public void Repository_input_cannot_smuggle_commands_or_credentials(string uri)=>Assert.Throws<ArgumentException>(()=>ProductPolicy.ParseRepository(uri));
    [Fact]public void Project_names_are_monotonic_and_keep_required_format(){using var tree=new Support.TempTree();var now=new DateTime(2026,10,31,22,27,0);var first=AutomaticProjects.Create(tree.Root,now);var second=AutomaticProjects.Create(tree.Root,now);Assert.Equal("0001.axon.31-10-2026-22-27",Path.GetFileName(first));Assert.Equal("0002.axon.31-10-2026-22-27",Path.GetFileName(second));Assert.True(Directory.Exists(first));}
    [Fact]public async Task Parallel_project_creation_does_not_reuse_numbers(){using var tree=new Support.TempTree();var paths=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Task.Run(()=>AutomaticProjects.Create(tree.Root))));Assert.Equal(12,paths.Distinct().Count());}
    [Fact]public void Existing_numbers_survive_missing_counter(){using var tree=new Support.TempTree();Directory.CreateDirectory(Path.Combine(tree.Root,".axon","projects","0010.axon.old"));Assert.StartsWith("0011.axon.",Path.GetFileName(AutomaticProjects.Create(tree.Root)));}
    [Theory][InlineData(".axon/projects/0001/a.txt")][InlineData(".git/config")][InlineData("Profiles/Default/Cookies")][InlineData(".env.production")][InlineData("token.txt")][InlineData("_private-archive/a.txt")][InlineData("output.log")]
    public void Publication_never_selects_private_runtime_files(string name)=>Assert.False(PublicationPolicy.IsAllowed(name));
    [Fact]public void Secret_scan_recognizes_pat_without_committing_one(){Assert.True(PublicationPolicy.ContainsSecret("gh"+"p_"+new string('a',36)));Assert.False(PublicationPolicy.ContainsSecret("ordinary project source"));}
    [Fact]public void Language_and_login_choices_persist_without_replacing_identity(){var s=new AppSettings{UiLanguage="en",Arbeitsbereiche=[new Arbeitsbereich{RememberLogin=false,AccessProfile="read",RepositoryUrl="https://github.com/a/b"}]};var copy=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;Assert.Equal("en",copy.UiLanguage);Assert.False(copy.Arbeitsbereiche[0].RememberLogin);Assert.Equal(s.Arbeitsbereiche[0].Id,copy.Arbeitsbereiche[0].Id);Assert.Equal("read",copy.Arbeitsbereiche[0].AccessProfile);}
    [Theory][InlineData("AXON.RC30","0.2.306","ISAS-AXON-RC30-0.2.306-win-x64.msi",true)][InlineData("other","0.2.306","ISAS-AXON-RC30-0.2.306-win-x64.msi",false)][InlineData("AXON.RC30","0.2.306.1","ISAS-AXON-RC30-0.2.306.1-win-x64.msi",false)][InlineData("AXON.RC30","0.2.306","../payload.msi",false)]
    public void Update_manifest_rejects_other_product_unsafe_name_and_revision(string product,string version,string file,bool valid)
    {
        var hash=new string('a',64);var json=JsonSerializer.Serialize(new{product,version,file,sha256=hash});
        if(valid)Assert.Equal(version,UpdateManifest.Parse(json,"sha256:"+hash).Version);else Assert.Throws<InvalidDataException>(()=>UpdateManifest.Parse(json,"sha256:"+hash));
    }
    [Fact]public void Update_manifest_requires_independent_github_digest(){var json=JsonSerializer.Serialize(new{product="AXON.RC30",version="0.2.306",file="ISAS-AXON-RC30-0.2.306-win-x64.msi",sha256=new string('a',64)});Assert.Throws<InvalidDataException>(()=>UpdateManifest.Parse(json,"sha256:"+new string('b',64)));}
    [Theory][InlineData("AXON RC30 Installer.exe",true)][InlineData("../AXON RC30 Installer.exe",false)][InlineData("payload.exe",false)]public void Bootstrapper_update_is_fixed_and_digest_bound(string name,bool valid){var hash=new string('a',64);if(valid)UpdateManifest.ValidateBootstrapper(name,hash,"sha256:"+hash);else Assert.Throws<InvalidDataException>(()=>UpdateManifest.ValidateBootstrapper(name,hash,"sha256:"+hash));Assert.Throws<InvalidDataException>(()=>UpdateManifest.ValidateBootstrapper("AXON RC30 Installer.exe",hash,"sha256:"+new string('b',64)));}
    [Theory][InlineData("../secret.txt")][InlineData("a/../secret.txt")][InlineData("C:\\private.txt")][InlineData("file.txt:secret")]public void Publication_rejects_traversal_absolute_and_stream_paths(string path)=>Assert.False(PublicationPolicy.IsAllowed(path));
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]public void Maintained_scanner_fails_closed_for_leaks_and_tool_errors(int exitCode){if(exitCode==0)PublicationPolicy.EnsureScannerPassed(exitCode);else Assert.Throws<InvalidDataException>(()=>PublicationPolicy.EnsureScannerPassed(exitCode));}
    [Fact]public void Git_policy_uses_regular_cross_platform_file_not_windows_device(){Assert.Equal("Empty.gitconfig",Path.GetFileName(ProductTools.GitPolicyFile));Assert.True(Path.IsPathFullyQualified(ProductTools.GitPolicyFile));Assert.DoesNotContain("NUL",ProductTools.GitPolicyFile,StringComparison.OrdinalIgnoreCase);}
    [Theory][InlineData("AXON RC30 Installer.exe",true)][InlineData("../AXON RC30 Installer.exe",false)][InlineData("other.exe",false)]public void GitHub_normalized_asset_maps_to_exact_local_installer(string local,bool valid){if(valid)Assert.Equal("AXON.RC30.Installer.exe",UpdateManifest.GitHubBootstrapperAsset(local));else Assert.Throws<InvalidDataException>(()=>UpdateManifest.GitHubBootstrapperAsset(local));}
}
