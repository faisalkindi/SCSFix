using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using System.Text;
using K4os.Compression.LZ4;

namespace SCSFix.Core.PearlAbyss;

/// <summary>One file of a Pearl Abyss archive directory: where its bytes are (<see cref="PazFile"/>, <see cref="Offset"/>,
/// <see cref="CompSize"/> stored, <see cref="OrigSize"/> once unpacked) and the raw PAMT <see cref="Flags"/>.</summary>
public sealed record PazEntry(string Path, string PazFile, uint Offset, uint CompSize, uint OrigSize, uint Flags)
{
    /// <summary>0 stored, 1 DDS (128-byte header + LZ4 body), 2 LZ4 block.</summary>
    public int Compression => (int)((Flags >> 16) & 0xF);
    public bool Compressed => CompSize != OrigSize;
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

/// <summary>Crimson Desert's archives: a directory (0017, ...) holds <c>0.pamt</c>, the index, and <c>N.paz</c>, the data. The
/// PAMT is a prefix tree of names and 20-byte file records; a file is stored LZ4-compressed or not, and some kinds (XML,
/// the shader root signatures) are also ChaCha20-encrypted with a key derived from the lower-cased file name alone. The
/// format is the community's (lazorr410's crimson-desert-unpacker documents it); this is an independent reader, read-only.</summary>
public static class PazArchive
{
    const uint HashInit = 0x000C5EDE, IvXor = 0x60616263;
    static readonly uint[] XorDeltas = [0x00000000, 0x0A0A0A0A, 0x0C0C0C0C, 0x06060606, 0x0E0E0E0E, 0x0A0A0A0A, 0x06060606, 0x02020202];

    /// <summary>The entries of <paramref name="pamt"/>, the PAZ files named after it in <paramref name="pazDir"/>. Throws
    /// <see cref="InvalidDataException"/> for an index that doesn't have the layout.</summary>
    public static List<PazEntry> ReadPamt(string pamt, string? pazDir = null)
    {
        var d = File.ReadAllBytes(pamt);
        pazDir ??= System.IO.Path.GetDirectoryName(pamt) ?? ".";
        if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(pamt), out var stem)) throw new InvalidDataException($"{pamt}: not a numbered index");
        try
        {
            var off = 4;   // the magic differs between game versions
            uint U(int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4));
            var pazCount = (int)U(off);
            if (pazCount is < 0 or > 4096) throw new InvalidDataException($"{pamt}: {pazCount} archive files");
            off += 4 + 8;
            for (var i = 0; i < pazCount; i++) off += 8 + (i < pazCount - 1 ? 4 : 0);   // hash, size, and a separator between them
            var folderEnd = off + 4 + (int)U(off);
            off += 4;
            var prefix = "";
            while (off < folderEnd)
            {
                var (parent, len) = (U(off), d[off + 4]);
                if (parent == 0xFFFFFFFF) prefix = Encoding.UTF8.GetString(d, off + 5, len);
                off += 5 + len;
            }
            var nodeSize = (int)U(off);
            off += 4;
            var (nodeStart, nodes) = (off, new Dictionary<int, (uint Parent, string Name)>());
            while (off < nodeStart + nodeSize)
            {
                var len = d[off + 4];
                nodes[off - nodeStart] = (U(off), Encoding.UTF8.GetString(d, off + 5, len));
                off += 5 + len;
            }
            var paths = new Dictionary<uint, string>();
            string PathOf(uint node)
            {
                if (paths.TryGetValue(node, out var known)) return known;
                var parts = new List<string>();
                for (var cur = node; cur != 0xFFFFFFFF && parts.Count < 64 && nodes.TryGetValue((int)cur, out var n); cur = n.Parent) parts.Add(n.Name);
                parts.Reverse();
                return paths[node] = string.Concat(parts);
            }
            var folders = (int)U(off);
            off += 4 + 4 + folders * 16;
            var entries = new List<PazEntry>();
            for (; off + 20 <= d.Length; off += 20)
            {
                var (node, at, comp, orig, flags) = (U(off), U(off + 4), U(off + 8), U(off + 12), U(off + 16));
                var path = PathOf(node);
                entries.Add(new PazEntry(prefix.Length > 0 ? $"{prefix}/{path}" : path, System.IO.Path.Combine(pazDir, $"{stem + (int)(flags & 0xFF)}.paz"), at, comp, orig, flags));
            }
            return entries;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or KeyNotFoundException)
        {
            throw new InvalidDataException($"{pamt}: not a PAMT index of this layout", e);
        }
    }

    /// <summary>The file's bytes: read from its PAZ, decrypted when <paramref name="encrypted"/>, then unpacked. A short read or
    /// an LZ4 stream that doesn't unpack to its size throws <see cref="InvalidDataException"/>.</summary>
    public static byte[] Read(PazEntry e, bool encrypted, SafeFileHandle? handle = null)
    {
        var data = new byte[e.Compressed ? e.CompSize : e.OrigSize];
        using var own = handle == null ? File.OpenHandle(e.PazFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete) : null;
        if (RandomAccess.Read(handle ?? own!, data, e.Offset) != data.Length) throw new InvalidDataException($"{e.Path}: {e.PazFile} ends before it");
        if (encrypted) ChaCha20(data, e.Name);
        if (!e.Compressed) return data;
        switch (e.Compression)
        {
            case 2: return Unpack(data, (int)e.OrigSize, e.Path);
            case 1 when data.Length > 128:   // a DDS: its 128-byte header is stored plain
                var body = Unpack(data.AsSpan(128), (int)e.OrigSize - 128, e.Path);
                return [.. data.AsSpan(0, 128), .. body];
            default: throw new InvalidDataException($"{e.Path}: compression type {e.Compression} isn't known");
        }
    }

    static byte[] Unpack(ReadOnlySpan<byte> packed, int size, string path)
    {
        var o = new byte[size];
        if (LZ4Codec.Decode(packed, o) != size) throw new InvalidDataException($"{path}: LZ4 data doesn't unpack to {size} bytes");
        return o;
    }

    /// <summary>Bob Jenkins' lookup3 hashlittle (the primary hash), the engine's hash for names.</summary>
    public static uint HashLittle(ReadOnlySpan<byte> data, uint init)
    {
        static uint Rot(uint v, int k) => (v << k) | (v >> (32 - k));
        uint a, b, c;
        a = b = c = 0xDEADBEEF + (uint)data.Length + init;
        var length = data.Length;
        var off = 0;
        while (length > 12)
        {
            a += BinaryPrimitives.ReadUInt32LittleEndian(data[off..]);
            b += BinaryPrimitives.ReadUInt32LittleEndian(data[(off + 4)..]);
            c += BinaryPrimitives.ReadUInt32LittleEndian(data[(off + 8)..]);
            a -= c; a ^= Rot(c, 4); c += b;
            b -= a; b ^= Rot(a, 6); a += c;
            c -= b; c ^= Rot(b, 8); b += a;
            a -= c; a ^= Rot(c, 16); c += b;
            b -= a; b ^= Rot(a, 19); a += c;
            c -= b; c ^= Rot(b, 4); b += a;
            off += 12;
            length -= 12;
        }
        Span<byte> tail = stackalloc byte[12];
        data[off..].CopyTo(tail);
        static uint T(ReadOnlySpan<byte> t, int o) => BinaryPrimitives.ReadUInt32LittleEndian(t[o..]);
        if (length >= 12) c += T(tail, 8);
        else if (length >= 9) c += T(tail, 8) & (0xFFFFFFFFu >> (8 * (12 - length)));
        if (length >= 8) b += T(tail, 4);
        else if (length >= 5) b += T(tail, 4) & (0xFFFFFFFFu >> (8 * (8 - length)));
        if (length >= 4) a += T(tail, 0);
        else if (length >= 1) a += T(tail, 0) & (0xFFFFFFFFu >> (8 * (4 - length)));
        else return c;
        c ^= b; c -= Rot(b, 14);
        a ^= c; a -= Rot(c, 11);
        b ^= a; b -= Rot(a, 25);
        c ^= b; c -= Rot(b, 16);
        a ^= c; a -= Rot(c, 4);
        b ^= a; b -= Rot(a, 14);
        c ^= b; c -= Rot(b, 24);
        return c;
    }

    /// <summary>The ChaCha20 key and 16-byte IV (a 32-bit counter, then a 96-bit nonce) of a file name: its lower-cased base name's
    /// hash, repeated four times for the IV, XORed into eight words for the key.</summary>
    public static (byte[] Key, byte[] Iv) DeriveKeyIv(string name)
    {
        var seed = HashLittle(Encoding.UTF8.GetBytes(name[(name.LastIndexOfAny(['/', '\\']) + 1)..].ToLowerInvariant()), HashInit);
        var iv = new byte[16];
        for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(iv.AsSpan(4 * i), seed);
        var key = new byte[32];
        for (var i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(4 * i), (seed ^ IvXor) ^ XorDeltas[i]);
        return (key, iv);
    }

    /// <summary>Decrypts (or encrypts: it is a stream cipher) <paramref name="data"/> in place with the key of <paramref name="name"/>.</summary>
    public static void ChaCha20(byte[] data, string name)
    {
        var (key, iv) = DeriveKeyIv(name);
        ChaCha20(data, key, iv);
    }

    public static void ChaCha20(byte[] data, byte[] key, byte[] iv)
    {
        static uint Rot(uint v, int k) => (v << k) | (v >> (32 - k));
        Span<uint> init = stackalloc uint[16], x = stackalloc uint[16];
        init[0] = 0x61707865; init[1] = 0x3320646e; init[2] = 0x79622d32; init[3] = 0x6b206574;
        for (var i = 0; i < 8; i++) init[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(4 * i));
        for (var i = 0; i < 4; i++) init[12 + i] = BinaryPrimitives.ReadUInt32LittleEndian(iv.AsSpan(4 * i));   // counter, nonce
        Span<byte> block = stackalloc byte[64];
        for (var pos = 0; pos < data.Length; pos += 64)
        {
            init.CopyTo(x);
            for (var r = 0; r < 10; r++)
            {
                Quarter(x, 0, 4, 8, 12); Quarter(x, 1, 5, 9, 13); Quarter(x, 2, 6, 10, 14); Quarter(x, 3, 7, 11, 15);
                Quarter(x, 0, 5, 10, 15); Quarter(x, 1, 6, 11, 12); Quarter(x, 2, 7, 8, 13); Quarter(x, 3, 4, 9, 14);
            }
            for (var i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(block[(4 * i)..], x[i] + init[i]);
            for (var i = 0; i < 64 && pos + i < data.Length; i++) data[pos + i] ^= block[i];
            init[12]++;   // the 32-bit block counter
        }
        static void Quarter(Span<uint> s, int a, int b, int c, int d)
        {
            s[a] += s[b]; s[d] = Rot(s[d] ^ s[a], 16);
            s[c] += s[d]; s[b] = Rot(s[b] ^ s[c], 12);
            s[a] += s[b]; s[d] = Rot(s[d] ^ s[a], 8);
            s[c] += s[d]; s[b] = Rot(s[b] ^ s[c], 7);
        }
    }
}
