namespace SCSFix.Core.Carved;

/// <summary>Several engine readers as one, tried in order. Detect: the first result that can be indexed, else the first
/// reader's that recognized the game (its reason is the most specific, e.g. Unreal's "encrypted" over the carver's "packed").
/// Index and ReadShaders go to the reader of <see cref="EngineInfo.Family"/>, so an EngineInfo cached from an earlier scan
/// still finds its reader.</summary>
public sealed class EngineReaders(params (string Family, IEngineReader Reader)[] readers) : IEngineReader
{
    public T? Get<T>() where T : class, IEngineReader => readers.Select(r => r.Reader).OfType<T>().FirstOrDefault();

    public EngineInfo? Detect(Game game)
    {
        EngineInfo? first = null;
        Exception? error = null;
        foreach (var (_, reader) in readers)
        {
            EngineInfo? e;
            try { e = reader.Detect(game); }
            catch (Exception ex) { error ??= ex; continue; } // one reader choking on the game's files doesn't stop the next
            if (e == null) continue;
            if (e.Unsupported == null && !e.Encrypted) return e;
            first ??= e;
        }
        if (first == null && error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        // Nobody could read the game's shaders: a recording still gives the plan (the recorder captures what the game creates,
        // with its bytes), so it isn't "unsupported" until the Planner finds no recorder for its graphics API or its anti-cheat blocks one.
        return first is { RecordOnly: false } f ? f with { RecordOnly = true } : first;
    }

    public string IndexStamp(Game game) => string.Concat(readers.Select(r => r.Reader.IndexStamp(game)));

    public string DetectStamp(Game game, EngineInfo? engine) => engine == null ? string.Concat(readers.Select(r => r.Reader.DetectStamp(game, null)))
        : readers.FirstOrDefault(r => r.Family == engine.Family).Reader?.DetectStamp(game, engine) ?? "";

    IEngineReader Of(EngineInfo e) => readers.FirstOrDefault(r => r.Family == e.Family).Reader ?? throw new NotSupportedException($"no reader for {e.Family}");

    public ShaderIndex Index(Game game, EngineInfo engine, IProgress<string>? log, CancellationToken ct) =>
        engine.RecordOnly ? RecordOnlyIndex(game) : Of(engine).Index(game, engine, log, ct);

    public void ReadShaders(Game game, EngineInfo engine, IReadOnlySet<string> sha1s, Action<string, byte[]> sink, CancellationToken ct)
    {
        if (!engine.RecordOnly) Of(engine).ReadShaders(game, engine, sha1s, sink, ct);   // else the files hold nothing readable: the recording has the bytes
    }

    /// <summary>No shaders, no maps: the plan comes from the recording alone. The content hash follows the game's exe, so a
    /// game update makes the plan stale like it does for an indexed game.</summary>
    internal static ShaderIndex RecordOnlyIndex(Game game)
    {
        var exe = new FileInfo(game.ExePath);
        var stamp = exe.Exists ? $"record-only|{exe.Length}|{exe.LastWriteTimeUtc.Ticks}" : "record-only";
        return new ShaderIndex(Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(stamp))),
            [], new Dictionary<string, ShaderInfo>(), []);
    }
}
