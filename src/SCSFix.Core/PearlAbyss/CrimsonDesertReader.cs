using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SCSFix.Core.Carved;
using SCSFix.Core.Planning;
using SCSFix.Core.Unreal;

namespace SCSFix.Core.PearlAbyss;

/// <summary><see cref="IEngineReader"/> for Crimson Desert. The game ships its compiled shaders and its root signatures in
/// archive directory 0017, <c>shadercache__/</c>:
/// <list type="bullet">
/// <item><c>f0_f1_stage_f3_n_f5_f6.padxil</c>: a 36-byte header ("PASC") and a DXIL container without its root signature.
/// The stage is the third field (0 VS, 1 HS, 2 DS, 3 GS, 4 PS, 5 CS, 6+ ray tracing). The files of one pipeline share f0, f1, f5
/// and f6; f3 is the shader's own. f0 is the hash (lookup3, seed 0xC5EDE) of the pipeline's pass name in PascalCase, f1 that of its
/// source file name (<c>Name.hlsl</c>); the vertex and pixel shaders of a pass come from different source files, so the pairs
/// aren't in the names: the planner pairs them by what a vertex shader feeds.</item>
/// <item><c>name.pars</c>: that pass's root signature, a root-signature-only DXIL container, ChaCha20-encrypted with a key from
/// the file name. The .pars names are lower-case: the pass name comes back by trying their capitalisations against the f0
/// values (<see cref="Pairing"/>); a pass it can't give back is paired by what its shaders declare, when only one root
/// signature covers them all.</item>
/// </list>
/// A shader used by several passes is several entries of the index (one per root signature), as the planner takes one root
/// signature per shader. Ray tracing libraries aren't read (a recording's). Read-only; never launches or attaches to the game.</summary>
public sealed class CrimsonDesertReader : IEngineReader
{
    public const string Family = "CrimsonDesert";
    const string Dir = "0017", Cache = "shadercache__/";
    const int HeaderSize = 36, MaxCapitals = 6, MaxCandidates = 4;
    const uint NameSeed = 0x000C5EDE;
    /// <summary>1: one map per pipeline by name. 2: one map per pass, the planner pairing its shaders.</summary>
    const int ReaderVersion = 2;

    /// <summary>Where an indexed blob is: its entry, whether it's encrypted, the bytes to skip, and the SHA-1 of the bytes after
    /// that (an entry the index made for another root signature has an id of its own, the same bytes).</summary>
    public sealed record Loc(PazEntry Entry, bool Encrypted, int Skip, string Sha);

    readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, Loc>> located = new();

    static string Pamt(Game g) => Path.Combine(g.InstallDir, Dir, "0.pamt");

    public EngineInfo? Detect(Game game)
    {
        if (!File.Exists(Pamt(game)) || !File.Exists(Path.Combine(game.InstallDir, "meta", "0.papgt"))) return null;
        if (!Entries(game).Any(e => e.Path.EndsWith(".padxil", StringComparison.OrdinalIgnoreCase))) return null;
        return new EngineInfo(Family, "DXIL" + CarvedReader.EmbeddedRootSignatures, null, "D3D12", false, null);
    }

    static List<PazEntry> Entries(Game g) =>
        [.. PazArchive.ReadPamt(Pamt(g)).Where(e => e.Path.StartsWith(Cache, StringComparison.OrdinalIgnoreCase) && (e.Path.EndsWith(".padxil", StringComparison.OrdinalIgnoreCase) || e.Path.EndsWith(".pars", StringComparison.OrdinalIgnoreCase)))];

    /// <summary>A shader file's name, split: <c>f0_f1_stage_f3_n_f5_f6</c>; null when it isn't one.</summary>
    public static string[]? Fields(PazEntry e)
    {
        var f = e.Name[..^(e.Name.EndsWith(".padxil", StringComparison.OrdinalIgnoreCase) ? 7 : 5)].Split('_');
        return f.Length == 7 && f.All(x => x.Length > 0) ? f : null;
    }

    /// <summary>The pipeline stage of a name's third field; null = a ray tracing library or one it doesn't know.</summary>
    public static Stage? StageOf(string f2) => f2 switch { "0" => Stage.Vertex, "1" => Stage.Hull, "2" => Stage.Domain, "3" => Stage.Geometry, "4" => Stage.Pixel, "5" => Stage.Compute, _ => null };

    public ShaderIndex Index(Game game, EngineInfo engine, IProgress<string>? log, CancellationToken ct) => Build(game, log, ct).Index;

    /// <summary>A shader file read: its name's fields, the SHA-1 of its container, and the entry.</summary>
    sealed record Item(string[] F, string Sha, PazEntry Entry);

    /// <summary>The index and how it was made.</summary>
    public (ShaderIndex Index, Pairing Pairing) Build(Game game, IProgress<string>? log, CancellationToken ct)
    {
        var entries = Entries(game);
        var rs = new ConcurrentDictionary<string, (string Name, byte[] Blob, PazEntry Entry)>();   // sha1 of the container -> the file
        var infos = new ConcurrentDictionary<string, ShaderInfo>();
        var items = new ConcurrentBag<Item>();
        var pars = entries.Where(e => e.Path.EndsWith(".pars", StringComparison.OrdinalIgnoreCase)).ToList();
        var padxil = entries.Where(e => e.Path.EndsWith(".padxil", StringComparison.OrdinalIgnoreCase)).ToList();
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = ct };
        int libs = 0, odd = 0, rsBad = 0, done = 0;
        Parallel.ForEach(pars, opts, e =>
        {
            var b = PazArchive.Read(e, encrypted: true);
            if (!Dxbc.Valid(b) || !Dxbc.IsRootSignatureOnly(b) || !Dxbc.RootSignatureValid(b)) { Interlocked.Increment(ref rsBad); return; }   // left out, never guessed
            rs[Convert.ToHexStringLower(SHA1.HashData(b))] = (e.Name[..^5], b, e);
        });
        Parallel.ForEach(padxil, opts, e =>
        {
            var f = Fields(e);
            if (f == null || StageOf(f[2]) is not { } stage) { Interlocked.Increment(ref libs); return; }
            var b = PazArchive.Read(e, encrypted: false);
            if (b.Length <= HeaderSize || !b.AsSpan(0, 4).SequenceEqual("PASC"u8) || !Dxbc.Valid(b.AsSpan(HeaderSize))) { Interlocked.Increment(ref odd); return; }
            var c = b.AsSpan(HeaderSize).ToArray();
            var sha = Convert.ToHexStringLower(SHA1.HashData(c));
            if (!infos.ContainsKey(sha))
                try
                {
                    if (ShaderContainer.Parse(c, sha, new(0, 0, 0, 0)) is not { } info || info.Stage != stage) { Interlocked.Increment(ref odd); return; }   // a name that doesn't match its program: not used
                    infos[sha] = info with { Counts = CarvedReader.Counts(info.Bindings) };
                }
                catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { Interlocked.Increment(ref odd); return; }   // a valid container, an odd program
            items.Add(new Item(f, sha, e));
            if (Interlocked.Increment(ref done) % 20000 == 0) log?.Report($"crimson desert: {done} of {padxil.Count} shader files read");
        });
        var pairing = Pair(infos, [.. items.Select(i => (i.F, i.Sha))], rs.ToDictionary(r => r.Key, r => (r.Value.Name, r.Value.Blob)));

        // one index entry per (shader, root signature): the first root signature (by hash) keeps the shader's own id
        var roots = pairing.Roots;
        var primary = items.Where(i => roots.ContainsKey(i.F[0])).GroupBy(i => i.Sha).ToDictionary(g => g.Key, g => g.SelectMany(i => roots[i.F[0]]).Order(StringComparer.Ordinal).First());
        string IdOf(string sha, string root) => primary[sha] == root ? sha : CarvedReader.Sha1Hex($"{sha}|{root}");
        var shaders = new Dictionary<string, ShaderInfo>();
        var locs = new Dictionary<string, Loc>();
        var firstItem = new Dictionary<string, Item>();
        foreach (var i in items) firstItem.TryAdd(i.Sha, i);
        foreach (var i in items.Where(i => roots.ContainsKey(i.F[0])))
            foreach (var root in roots[i.F[0]])
            {
                var id = IdOf(i.Sha, root);
                if (shaders.ContainsKey(id)) continue;
                shaders[id] = infos[i.Sha] with { Sha1 = id, RootSignature = root };
                locs[id] = new Loc(firstItem[i.Sha].Entry, false, HeaderSize, i.Sha);
            }
        foreach (var (sha, (_, _, e)) in rs) locs[sha] = new Loc(e, true, 0, sha);

        // one map per pass and root signature, its graphics shaders together: the planner pairs a vertex shader with the pixel shaders
        // its outputs feed (the names don't say which pair a pipeline was: a pass's vertex and pixel shaders come from different
        // source files, and 7,000 pixel shaders had no vertex shader beside them). A compute shader is a pipeline of its own.
        var maps = new Dictionary<string, ShaderMap>();
        foreach (var g in items.Where(i => roots.ContainsKey(i.F[0])).GroupBy(i => i.F[0]))
            foreach (var root in roots[g.Key])
            {
                var graphics = g.Where(i => StageOf(i.F[2]) != Stage.Compute).Select(i => IdOf(i.Sha, root)).Distinct().Order(StringComparer.Ordinal).ToList();
                if (graphics.Count > 0)
                {
                    var hash = CarvedReader.Sha1Hex(g.Key + "|" + root + "|" + string.Join(',', graphics));
                    maps.TryAdd(hash, new ShaderMap(hash, "shadercache", CarvedReader.Platform, graphics));
                }
                foreach (var cs in g.Where(i => StageOf(i.F[2]) == Stage.Compute).Select(i => IdOf(i.Sha, root)).Distinct().Order(StringComparer.Ordinal))
                {
                    var hash = CarvedReader.Sha1Hex("cs|" + cs);
                    maps.TryAdd(hash, new ShaderMap(hash, "shadercache", CarvedReader.Platform, [cs], IsPipeline: true));
                }
            }
        using var content = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        content.AppendData(Encoding.UTF8.GetBytes($"reader {ReaderVersion};"));   // a change in how the files are read makes a new index, so a plan built before it isn't reused
        foreach (var e in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
            content.AppendData(Encoding.UTF8.GetBytes($"{e.Path}|{e.Offset}|{e.CompSize}|{e.OrigSize}\n"));
        located[game.Id] = locs;
        log?.Report($"crimson desert: {shaders.Count} shader entries ({infos.Count} distinct shaders), {maps.Count} pipelines, {rs.Count} root signatures; "
            + $"{pairing.Named} passes named by their root signature, {pairing.ByRanges} by what they declare, {pairing.Several} with up to {MaxCandidates} candidates compiled under each, {pairing.Left} left to a recording; "
            + $"{libs} ray tracing libraries and {odd + rsBad} unusable files left out");
        return (new ShaderIndex(Convert.ToHexStringLower(content.GetHashAndReset()), [CarvedReader.Platform], shaders, [.. maps.Values.OrderBy(m => m.Hash, StringComparer.Ordinal)]), pairing);
    }

    /// <summary>What the pairing made. <see cref="Roots"/>: a pass (f0) -> the root signature(s) its shaders are planned with.</summary>
    public sealed record Pairing(IReadOnlyDictionary<string, IReadOnlyList<string>> Roots, int Named, int ByRanges, int Several, int Left,
        IReadOnlyList<(string F0, string? Name, int Shaders, int Uncovered, string How)> Passes);

    /// <summary>The root signature of each pass (f0): its own, found by name, when that covers every shader of the pass; else, for a
    /// pass no name came back for, the one of the unnamed root signatures that covers them all. A pass neither settles is left
    /// out: its shaders aren't planned, a recording's.</summary>
    static Pairing Pair(IReadOnlyDictionary<string, ShaderInfo> infos, IReadOnlyList<(string[] F, string Sha)> files, IReadOnlyDictionary<string, (string Name, byte[] Blob)> rs)
    {
        var ranges = new Dictionary<string, RootSig.Ranges>();
        foreach (var (sha, (_, b)) in rs)
            try { ranges[sha] = RootSig.Parse(b); }
            catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException) { }
        var f0Values = files.Select(f => Convert.ToUInt32(f.F[0], 16)).ToHashSet();
        // the pass name behind each f0: a root signature's name, capitalised
        var byName = new ConcurrentDictionary<string, string>();   // f0 hex -> root signature sha
        Parallel.ForEach(rs, r =>
        {
            foreach (var cand in Capitalisations(r.Value.Name, MaxCapitals))
            {
                var h = PazArchive.HashLittle(Encoding.UTF8.GetBytes(cand), NameSeed);
                if (f0Values.Contains(h)) { byName.TryAdd(h.ToString("x8"), r.Key); break; }
            }
        });
        var of = new Dictionary<string, IReadOnlyList<string>>();
        var passes = new List<(string, string?, int, int, string)>();
        int nNamed = 0, nRanges = 0, nSeveral = 0, nLeft = 0;
        foreach (var g in files.GroupBy(f => f.F[0]))
        {
            var members = g.Select(f => f.Sha).Distinct().ToList();
            int Uncovered(string root) => members.Count(m => !ranges.TryGetValue(root, out var r) || RootSig.Uncovered(r, infos[m].Stage, infos[m]) is not null);
            if (byName.TryGetValue(g.Key.ToLowerInvariant(), out var own) && ranges.ContainsKey(own))
            {
                var bad = Uncovered(own);
                passes.Add((g.Key, rs[own].Name, members.Count, bad, bad == 0 ? "named" : $"named, but {bad} shaders aren't covered by it"));
                if (bad == 0) (of[g.Key], nNamed) = ([own], nNamed + 1); else nLeft++;
                continue;
            }
            // no name came back: the root signatures that cover every shader. One is the pass's; a few, one of them is, and every
            // one is planned (the wrong ones only cost a compile); many say too little, and the pass is left to a recording
            var ok = ranges.Keys.Where(k => Uncovered(k) == 0).Order(StringComparer.Ordinal).ToList();
            if (ok.Count == 1) { of[g.Key] = ok; nRanges++; }
            else if (ok.Count is > 1 and <= MaxCandidates) { of[g.Key] = ok; nSeveral++; }
            else nLeft++;
            passes.Add((g.Key, ok.Count == 1 ? rs[ok[0]].Name : null, members.Count, 0,
                ok.Count == 1 ? "by ranges" : ok.Count == 0 ? "no root signature covers it" : ok.Count <= MaxCandidates ? $"{ok.Count} candidates, all planned" : $"{ok.Count} root signatures cover it"));
        }
        return new Pairing(of, nNamed, nRanges, nSeveral, nLeft, passes);
    }

    /// <summary>The PascalCase spellings of a lower-case name with up to <paramref name="maxCapitals"/> capitals, the first letter one.</summary>
    internal static IEnumerable<string> Capitalisations(string lower, int maxCapitals)
    {
        var chars = lower.ToCharArray();
        chars[0] = char.ToUpperInvariant(chars[0]);
        return Spell(lower, 1, maxCapitals - 1, chars);
    }

    static IEnumerable<string> Spell(string lower, int from, int left, char[] cur)
    {
        yield return new string(cur);
        if (left == 0) yield break;
        for (var i = from; i < lower.Length; i++)
        {
            if (!char.IsLetter(lower[i])) continue;
            var next = (char[])cur.Clone();
            next[i] = char.ToUpperInvariant(lower[i]);
            foreach (var s in Spell(lower, i + 1, left - 1, next)) yield return s;
        }
    }

    public void ReadShaders(Game game, EngineInfo engine, IReadOnlySet<string> sha1s, Action<string, byte[]> sink, CancellationToken ct)
    {
        if (!located.TryGetValue(game.Id, out var locs))
        {
            Build(game, null, ct);
            locs = located[game.Id];
        }
        foreach (var file in sha1s.Where(locs.ContainsKey).Select(s => (Id: s, At: locs[s])).GroupBy(x => x.At.Entry.PazFile))
        {
            using var h = File.OpenHandle(file.Key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            foreach (var (id, at) in file.OrderBy(x => x.At.Entry.Offset))
            {
                ct.ThrowIfCancellationRequested();
                byte[] b;
                try { b = PazArchive.Read(at.Entry, at.Encrypted, h)[at.Skip..]; }
                catch (Exception e) when (e is InvalidDataException or IOException) { continue; }   // changed since the index: fails at replay and is counted there
                if (Convert.ToHexStringLower(SHA1.HashData(b)) == at.Sha) sink(id, b);
            }
        }
    }
}
