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

}
