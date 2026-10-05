using SCSFix.Core;
using SCSFix.Core.App;
using SCSFix.Core.Games;

namespace SCSFix.Tests.Platform;

/// <summary>Fixes for what the original SCSKiller's users reported: the wrong exe picked, a scan that never ends, "the game is running"
/// with nothing running, a recorder that never loaded, a compile of 64 GB, a portable build that wrote to %LOCALAPPDATA%.</summary>
public partial class AppTests
{
    [Fact]
    public void An_Unreal_install_picks_the_Shipping_exe_not_the_largest_helper_beside_it()   // upstream 26: Returnal's online-services installer is 114 MB
    {
        var install = Path.Combine(_root, "Returnal");
        var bin = Path.Combine(install, "Returnal", "Binaries", "Win64");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "EpicOnlineServicesInstaller.exe"), new byte[200_000]);
        File.WriteAllBytes(Path.Combine(bin, "Launcher.exe"), new byte[50_000]);
        File.WriteAllBytes(Path.Combine(bin, "Returnal-Win64-Shipping.exe"), new byte[4096]);
        Assert.Equal(Path.Combine(bin, "Returnal-Win64-Shipping.exe"), GameFiles.FindExe(install));
    }

    [Fact]
    public void Without_a_Shipping_name_the_largest_exe_that_is_not_a_helper_is_the_game()
    {
        var install = Path.Combine(_root, "Odd");
        var bin = Path.Combine(install, "Odd", "Binaries", "Win64");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "OddSetup.exe"), new byte[200_000]);
        File.WriteAllBytes(Path.Combine(bin, "CrashReportClient.exe"), new byte[100_000]);
        File.WriteAllBytes(Path.Combine(bin, "OddGame.exe"), new byte[4096]);
        Assert.Equal(Path.Combine(bin, "OddGame.exe"), GameFiles.FindExe(install));
    }

    /// <summary>Detect never returns until released.</summary>
    sealed class HangingReader(ManualResetEventSlim release) : IEngineReader
    {
        public EngineInfo? Detect(Game game) { release.Wait(); return null; }
        public ShaderIndex Index(Game game, EngineInfo e, IProgress<string>? log, CancellationToken ct) => new("c", [], new Dictionary<string, ShaderInfo>(), []);
        public void ReadShaders(Game game, EngineInfo e, IReadOnlySet<string> sha1s, Action<string, byte[]> sink, CancellationToken ct) { }
    }

    [Fact]
    public async Task A_scan_goes_on_without_a_game_whose_files_never_answer_and_says_why()   // upstream 51: "scans for games forever"
    {
        using var release = new ManualResetEventSlim();
        var k = Killer(new HangingReader(release));
        k.EvaluateBudget = TimeSpan.FromMilliseconds(300);
        try
        {
            var done = k.ScanAsync(default);
            Assert.Same(done, await Task.WhenAny(done, Task.Delay(TimeSpan.FromSeconds(20))));
            var s = k.Games.Single();
            Assert.Equal(GameStatus.Unsupported, s.Status);
            Assert.Contains("reading its files took more than", s.StatusReason);
            Assert.Contains("is its drive reachable?", s.StatusReason);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void A_recorder_the_game_never_loaded_is_named_when_its_last_run_began_after_the_recorder_went_in()   // upstream 2, 17
    {
        var dll = Path.Combine(_exeDir, "d3d12.dll");
        File.WriteAllBytes(dll, [1]);
        var installed = File.GetCreationTimeUtc(dll);
        var rec = new GameRecord { LastPlay = new PlayWindow(new DateTimeOffset(installed.AddMinutes(5), TimeSpan.Zero), new DateTimeOffset(installed.AddMinutes(30), TimeSpan.Zero)) };
        Assert.Contains("didn't load the recorder", ScsFix.NotLoaded(rec, _exeDir));

        File.WriteAllText(Path.Combine(_exeDir, "scsfix.log"), "loaded into");   // it did: whatever it wrote, even nothing recorded
        Assert.Null(ScsFix.NotLoaded(rec, _exeDir));
        File.Delete(Path.Combine(_exeDir, "scsfix.log"));

        rec.LastPlay = new PlayWindow(new DateTimeOffset(installed.AddMinutes(-30), TimeSpan.Zero), new DateTimeOffset(installed.AddMinutes(-5), TimeSpan.Zero));
        Assert.Null(ScsFix.NotLoaded(rec, _exeDir));   // it ran before the recorder was there: nothing to say
        Assert.Null(ScsFix.NotLoaded(new GameRecord(), _exeDir));   // never watched
    }

    [Fact]
    public async Task The_stopped_note_names_the_process_that_holds_the_games_name_but_not_a_staged_warms_child()   // upstream 46, 14, 27, 53
    {
        var k = Killer(new FakeReader(Unreal));
        k.Processes = fresh => [(100, 1, "Fake-Win64-Shipping.exe"), (200, 1, "scsfix_warm.exe"), (201, 200, "Fake-Win64-Shipping.exe"), (300, 1, "other.exe")];
        k.RunningGameExes = () => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Fake-Win64-Shipping.exe" };
        await k.ScanAsync(default);
        k.Enqueue(_game.Id);
        k.StartQueue();
        await Until(() => k.Queue.Single().Stage == QueueStage.Paused);
        Assert.Equal("stopped while Fake Game is running: continues when it exits. Running: Fake-Win64-Shipping.exe (process 100)", k.Queue.Single().Note);
        k.StopQueue();
        await k.WhenQueueIdle().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_queue_that_adds_more_than_16_GB_to_the_cache_is_warned_about_and_a_smaller_one_is_not()   // upstream 25, 52: Borderlands 4, 64 GB
    {
        Assert.Null(ScsFix.LargeCompileWarning([("Small", 4L << 30), ("Edge", ScsFix.LargeCompileBytes)]));
        var w = ScsFix.LargeCompileWarning([("Small", 4L << 30), ("Borderlands 4", 64L << 30)])!;
        Assert.Contains("Borderlands 4 would add about", w);
        Assert.DoesNotContain("Small", w);
        Assert.Contains("recording", w);
    }

    [Fact]
    public void A_portable_marker_beside_the_exe_keeps_the_data_there_and_without_it_the_data_is_in_LocalAppData()   // upstream 50
    {
        var exeDir = Path.Combine(_root, "portable-app");
        Directory.CreateDirectory(exeDir);
        Assert.Equal(Path.Combine(_root, "local", "SCSFix"), AppStore.Resolve(exeDir, Path.Combine(_root, "local")));
        File.WriteAllText(Path.Combine(exeDir, "portable.txt"), "");
        Assert.Equal(Path.Combine(exeDir, "data"), AppStore.Resolve(exeDir, Path.Combine(_root, "local")));
    }
}
