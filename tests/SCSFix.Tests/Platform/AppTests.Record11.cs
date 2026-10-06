using SCSFix.Core;
using SCSFix.Core.App;
using SCSFix.Core.Unreal;

namespace SCSFix.Tests.Platform;

/// <summary>The DirectX 11 recorder: the proxy under its other name, d3d11.dll, in a game none of the readers can read.</summary>
public partial class AppTests
{
    const string UnreadableFiles = "no raw DXBC/DXIL shaders in its files (0.0 GB sampled): shaders are compressed or packed: needs an engine reader";
    static readonly EngineInfo Packed11 = new("Carved", "-", null, "D3D11", false, UnreadableFiles, RecordOnly: true);

    string Dll12 => Path.Combine(_exeDir, "d3d12.dll");
    string Dll11 => Path.Combine(_exeDir, ScsFix.Proxy11);

    /// <summary>NVIDIA's profile, the one whose D3D11 cache is warmed (Planner.D3D11Cache); the app manages recorders.</summary>
    ScsFix Recording11(EngineInfo engine, string profile = "nvidia-1")
    {
        var k = Killer(new FakeReader(engine), vendor: new FakeVendor(Gpu, profile: profile));
        k.ProcessNames = () => new HashSet<string>();
        k.ManageRecorders = true;
        return k;
    }

    [Fact]
    public async Task A_record_only_game_on_DirectX_11_gets_d3d11_dll_as_its_only_recorder()
    {
        using var _ = new FreshLedger(_root);
        var k = Recording11(Packed11);
        await k.ScanAsync(default);
        Assert.True(k.Games.Single().Records11);
        Assert.True(ScsFix.IsOurProxy(Dll11));
        Assert.False(File.Exists(Dll12));   // nothing of D3D12 in a game that only runs on DirectX 11
        Assert.Equal(File.ReadAllBytes(_proxy), File.ReadAllBytes(Dll11));   // the same binary under the other name
        Assert.Equal(["d3d11.dll", "scsfix.ini"], k.Store.LoadGame(_game.Id).RecorderFiles.Keys.Order());
        await AssertStaysArmed(k, _game);   // the install watcher doesn't take the new d3d11.dll for a program it must disarm for
    }

    [Fact]
    public async Task A_game_that_may_run_on_either_gets_both_names()
    {
        using var _ = new FreshLedger(_root);
        var k = Recording11(Packed11 with { GraphicsApi = UnrealRhi.Ambiguous });
        await k.ScanAsync(default);
        Assert.True(ScsFix.IsOurProxy(Dll12) && ScsFix.IsOurProxy(Dll11));
        Assert.Equal(["d3d11.dll", "d3d12.dll", "scsfix.ini"], k.Store.LoadGame(_game.Id).RecorderFiles.Keys.Order());
        await AssertStaysArmed(k, _game);
    }

    [Fact]
    public async Task Turning_the_recorder_off_removes_both_files_and_nothing_else()
    {
        using var _ = new FreshLedger(_root);
        var k = Recording11(Packed11 with { GraphicsApi = UnrealRhi.Ambiguous });
        await k.ScanAsync(default);
        File.WriteAllText(Path.Combine(_exeDir, "game.cfg"), "settings");   // the game's own
        k.SetRecorderOverride(_game.Id, RecorderOverride.Off);
        Assert.False(File.Exists(Dll12) || File.Exists(Dll11) || File.Exists(Path.Combine(_exeDir, "scsfix.ini")));
        Assert.Empty(k.Store.LoadGame(_game.Id).RecorderFiles);
        Assert.True(File.Exists(Path.Combine(_exeDir, "game.cfg")));
    }

    [Fact]
    public async Task Another_d3d11_dll_is_never_replaced()
    {
        using var _ = new FreshLedger(_root);
        var foreign = "MZ somebody's d3d11.dll (ReShade?)"u8.ToArray();
        File.WriteAllBytes(Dll11, foreign);
        // only on DirectX 11: no recorder at all, and the reason says why
        var k = Recording11(Packed11);
        await k.ScanAsync(default);
        Assert.Equal(ScsFix.SkipForeignDll11, k.Games.Single().RecorderSkip);
        Assert.False(k.Games.Single().RecorderInstalled && ScsFix.IsOurProxy(Dll11));
        Assert.Equal(foreign, File.ReadAllBytes(Dll11));
        Assert.False(File.Exists(Dll12));
    }

    [Fact]
    public async Task Another_d3d11_dll_stays_when_the_DirectX_12_recorder_goes_in()
    {
        using var _ = new FreshLedger(_root);
        var foreign = "MZ somebody's d3d11.dll (ReShade?)"u8.ToArray();
        File.WriteAllBytes(Dll11, foreign);
        // on either API: the DirectX 12 recorder goes in, the other d3d11.dll stays
        var k2 = Recording11(Packed11 with { GraphicsApi = UnrealRhi.Ambiguous });
        await k2.ScanAsync(default);
        Assert.True(ScsFix.IsOurProxy(Dll12));
        Assert.Equal(foreign, File.ReadAllBytes(Dll11));
        Assert.DoesNotContain("d3d11.dll", k2.Store.LoadGame(_game.Id).RecorderFiles.Keys);   // never claimed, so never removed
        k2.SetRecorderOverride(_game.Id, RecorderOverride.Off);
        Assert.Equal(foreign, File.ReadAllBytes(Dll11));
    }

    [Fact]
    public async Task Only_a_record_only_game_on_a_GPU_whose_D3D11_cache_is_warmed_gets_it()
    {
        using var _ = new FreshLedger(_root);
        var amd = Recording11(Packed11, profile: "fake-1");   // the D3D11 warm is NVIDIA's (Planner.D3D11Cache)
        await amd.ScanAsync(default);
        Assert.Equal(ScsFix.SkipNotDx12, amd.Games.Single().RecorderSkip);
        Assert.False(File.Exists(Dll11));
        // a game the readers can read isn't recorded on DirectX 11: its files give the plan
        var readable = Recording11(new EngineInfo("Unity", "2022.3", null, "D3D11", false, null));
        await readable.ScanAsync(default);
        Assert.False(readable.Games.Single().Records11);
        Assert.False(File.Exists(Dll11));
    }

    [Fact]
    public async Task A_d3d11_recorder_the_game_calls_its_own_is_found_by_either_name()
    {
        using var _ = new FreshLedger(_root);
        var k = Recording11(Packed11);
        await k.ScanAsync(default);
        var s = k.Games.Single();
        Assert.True(s.RecorderInstalled);                    // "ours" looks under both names
        var fresh = Recording11(Packed11);                   // another run of the app finds it on disk, with no record of it
        fresh.Store.SaveGame(_game.Id, new GameRecord());
        await fresh.ScanAsync(default);
        Assert.True(fresh.Games.Single().RecorderInstalled);
    }
}
