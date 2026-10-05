using System.Diagnostics;
using System.Text;
using SCSFix.Core;
using SCSFix.Core.Planning;
using SCSFix.Core.PearlAbyss;
using Xunit.Abstractions;

namespace SCSFix.Tests.PearlAbyss;

/// <summary>Reads the installed Crimson Desert (manual: SCSFIX_CD_SURVEY=1; SCSFIX_CD_DIR names it when it isn't on E:): what the
/// reader finds, how many root signatures cover each group of shaders.</summary>
[Trait("Needs", "Game")]
public class CrimsonDesertSurvey(ITestOutputHelper output)
{
    static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("SCSFIX_CD_DIR") ?? @"E:\SteamLibrary\steamapps\common\Crimson Desert";
        return File.Exists(Path.Combine(d, "0017", "0.pamt")) ? d : null;
    }

    [Fact]
    public void Survey()
    {
        if (Environment.GetEnvironmentVariable("SCSFIX_CD_SURVEY") == null || Dir() is not { } dir) return;
        var game = new Game("steam:3321460", "Crimson Desert", Store.Steam, dir, Path.Combine(dir, "bin64", "CrimsonDesert.exe"));
        var reader = new CrimsonDesertReader();
        Assert.NotNull(reader.Detect(game));
        var sw = Stopwatch.StartNew();
        var (index, pairing) = reader.Build(game, new Progress<string>(output.WriteLine), CancellationToken.None);
        output.WriteLine($"built in {sw.Elapsed.TotalSeconds:F1} s: {index.Shaders.Count} shaders, {index.Maps.Count} maps");
        output.WriteLine($"pairing: {pairing.Named} named, {pairing.ByRanges} by ranges, {pairing.Several} several candidates, {pairing.Left} left; passes {pairing.Passes.Count}");
        output.WriteLine("how: " + string.Join(", ", pairing.Passes.GroupBy(p => p.How.StartsWith("named, but") ? "named, not covered" : p.How.Contains("root signatures cover") ? "several unnamed cover" : p.How).Select(g => $"{g.Key}: {g.Count()} passes/{g.Sum(x => x.Shaders)} shaders")));
        foreach (var p in pairing.Passes.Where(p => p.How is not ("named" or "by ranges") && !p.How.EndsWith("all planned")).OrderByDescending(p => p.Shaders).Take(25)) output.WriteLine($"  left: f0={p.F0} name={p.Name} shaders={p.Shaders} uncovered={p.Uncovered} {p.How}");
        output.WriteLine($"entries per shader: {string.Join(", ", index.Shaders.Values.GroupBy(s2 => s2.Counts).Take(0).Select(g => g.Key))}");
        output.WriteLine("stages: " + string.Join(", ", index.Shaders.Values.GroupBy(s2 => s2.Stage).Select(g => $"{g.Key} {g.Count()}")));
        output.WriteLine("map shapes: " + string.Join(", ", index.Maps.GroupBy(m => string.Join('+', m.Shaders.Select(s2 => index.Shaders[s2].Stage).Order())).OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key} x{g.Count()}")));
        // every map's shaders share one root signature, and it covers them
        var bad = index.Maps.Count(m => m.Shaders.Select(s2 => index.Shaders[s2].RootSignature).Distinct().Count() != 1);
        output.WriteLine($"distinct shaders planned: {index.Shaders.Keys.Select(k => k).Count()} entries");
        output.WriteLine($"maps with more than one root signature: {bad}");
        Assert.Equal(0, bad);
        NameHashes(game);
    }

    /// <summary>Does f0 hash a name the .pars names give back (in some capitalisation)? Tries every capitalisation with up to six
    /// capitals, the first letter one.</summary>
    void NameHashes(Game game)
    {
        var entries = PazArchive.ReadPamt(Path.Combine(game.InstallDir, "0017", "0.pamt")).Where(e => e.Path.StartsWith("shadercache__/")).ToList();
        var f0s = entries.Where(e => e.Path.EndsWith(".padxil")).Select(CrimsonDesertReader.Fields).OfType<string[]>().Select(f => Convert.ToUInt32(f[0], 16)).ToHashSet();
        var f1s = entries.Where(e => e.Path.EndsWith(".padxil")).Select(CrimsonDesertReader.Fields).OfType<string[]>().Select(f => Convert.ToUInt32(f[1], 16)).ToHashSet();
        var names = entries.Where(e => e.Path.EndsWith(".pars")).Select(e => e.Name[..^5]).Distinct().ToList();
        output.WriteLine($"f0 values {f0s.Count}, f1 values {f1s.Count}, pars names {names.Count}");
        int found0 = 0, found1 = 0;
        var resolved = new List<string>();
        foreach (var name in names)
        {
            var hit = false;
            foreach (var cand in Capitalisations(name, 6))
            {
                var b = Encoding.UTF8.GetBytes(cand);
                if (f0s.Contains(PazArchive.HashLittle(b, 0x000C5EDE))) { found0++; hit = true; resolved.Add($"{name} -> {cand}"); break; }
            }
            if (!hit && Capitalisations(name, 6).Any(c => f1s.Contains(PazArchive.HashLittle(Encoding.UTF8.GetBytes(c + ".hlsl"), 0x000C5EDE)))) found1++;
        }
        output.WriteLine($"names whose capitalised form hashes to an f0: {found0} of {names.Count}; to an f1 (.hlsl): {found1}");
        foreach (var r in resolved.Take(40)) output.WriteLine("  " + r);
    }

    static IEnumerable<string> Capitalisations(string lower, int maxCapitals)
    {
        var n = lower.Length;
        var chars = lower.ToCharArray();
        chars[0] = char.ToUpperInvariant(chars[0]);
        // choose up to maxCapitals-1 further positions
        IEnumerable<string> Rec(int from, int left, char[] cur)
        {
            yield return new string(cur);
            if (left == 0) yield break;
            for (var i = from; i < n; i++)
            {
                if (!char.IsLetter(lower[i])) continue;
                var next = (char[])cur.Clone();
                next[i] = char.ToUpperInvariant(lower[i]);
                foreach (var s in Rec(i + 1, left - 1, next)) yield return s;
            }
        }
        return Rec(1, maxCapitals - 1, chars);
    }
}
