using System.Buffers.Binary;
using System.Text;
using K4os.Compression.LZ4;
using SCSFix.Core.PearlAbyss;

namespace SCSFix.Tests.PearlAbyss;

public class PazArchiveTests
{
    // vectors from lazorr410's reference implementation (paz_crypto.py), computed with it
    [Theory]
    [InlineData("Sea.hlsl", 0x000C5EDEu, 1066339121u)]
    [InlineData("", 0x000C5EDEu, 3736739277u)]
    [InlineData("1234567890123", 5u, 2404554074u)]
    public void HashLittleMatchesLookup3(string text, uint init, uint expected) =>
        Assert.Equal(expected, PazArchive.HashLittle(Encoding.UTF8.GetBytes(text), init));

    [Theory]
    [InlineData("test.xml", "001298970a18929d0c1e949b06149e910e1c96990a18929d06149e9102109a95", "6370f9f76370f9f76370f9f76370f9f7")]
    [InlineData("arealightobject.pars", "a4fd2d2caef72726a8f12120a2fb2b2aaaf32322aef72726a2fb2b2aa6ff2f2e", "c79f4c4cc79f4c4cc79f4c4cc79f4c4c")]
    [InlineData("A", "d641d324dc4bd92eda4ddf28d047d522d84fdd2adc4bd92ed047d522d443d126", "b523b244b523b244b523b244b523b244")]
    public void KeyAndIvDeriveFromTheLowerCasedBaseName(string name, string key, string iv)
    {
        var (k, v) = PazArchive.DeriveKeyIv(name);
        Assert.Equal(key, Convert.ToHexStringLower(k));
        Assert.Equal(iv, Convert.ToHexStringLower(v));
        var (k2, v2) = PazArchive.DeriveKeyIv(@"some\Folder/" + name.ToUpperInvariant());   // the directory and the case don't count
        Assert.Equal(k, k2);
        Assert.Equal(v, v2);
    }

    [Fact]
    public void ChaCha20MatchesTheReferenceAndIsItsOwnInverse()
    {
        var data = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        PazArchive.ChaCha20(data, "x.bin");
        Assert.StartsWith("870969d28067818b9b49518474b3bcc1c6919db844d646b3fa18140999e7d71dc1ae8c0159dfa56a", Convert.ToHexStringLower(data));
        PazArchive.ChaCha20(data, "x.bin");
        Assert.Equal(Enumerable.Range(0, 200).Select(i => (byte)i), data);
    }

    /// <summary>A directory with one PAMT and one PAZ, built the way the game's are: a plain file, an LZ4 one, an encrypted one.</summary>
    [Fact]
    public void ReadsAPamtAndItsFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "paz-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var plain = Encoding.ASCII.GetBytes("plain file");
            var big = Enumerable.Range(0, 5000).Select(i => (byte)(i % 7)).ToArray();
            var lz = new byte[LZ4Codec.MaximumOutputSize(big.Length)];
            lz = lz[..LZ4Codec.Encode(big, lz)];
            var secret = Encoding.ASCII.GetBytes("secret root signature bytes");
            var enc = (byte[])secret.Clone();
            PazArchive.ChaCha20(enc, "locked.pars");
            var paz = new List<byte>();
            (uint At, uint Comp, uint Orig) Put(byte[] b, int orig) { var at = (uint)paz.Count; paz.AddRange(b); return (at, (uint)b.Length, (uint)orig); }
            var (a1, c1, o1) = Put(plain, plain.Length);
            var (a2, c2, o2) = Put(lz, big.Length);
            var (a3, c3, o3) = Put(enc, secret.Length);
            File.WriteAllBytes(Path.Combine(dir, "0.paz"), [.. paz]);

            var pamt = new List<byte>();
            void U32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); pamt.AddRange(s.ToArray()); }
            void Name(uint parent, string n) { U32(parent); pamt.Add((byte)n.Length); pamt.AddRange(Encoding.UTF8.GetBytes(n)); }
            U32(0xDEADBEEF);   // magic
            U32(1);            // one PAZ
            U32(0); U32(0);    // hash, zero
            U32(0); U32((uint)paz.Count);   // its hash and size
            var folder = new List<byte>();
            { var keep = pamt; pamt = folder; Name(0xFFFFFFFF, "shadercache__"); pamt = keep; }
            U32((uint)folder.Count); pamt.AddRange(folder);
            var nodes = new List<byte>();
            uint n1, n2, n3;
            { var keep = pamt; pamt = nodes; n1 = (uint)pamt.Count; Name(0xFFFFFFFF, "plain.txt"); n2 = (uint)pamt.Count; Name(0xFFFFFFFF, "big.padxil"); n3 = (uint)pamt.Count; Name(0xFFFFFFFF, "Locked.pars"); pamt = keep; }
            U32((uint)nodes.Count); pamt.AddRange(nodes);
            U32(0); U32(0);    // no folder records, their hash
            void Rec(uint node, uint at, uint comp, uint orig, uint flags) { U32(node); U32(at); U32(comp); U32(orig); U32(flags); }
            Rec(n1, a1, c1, o1, 0);
            Rec(n2, a2, c2, o2, 2u << 16);
            Rec(n3, a3, c3, o3, 0);
            File.WriteAllBytes(Path.Combine(dir, "0.pamt"), [.. pamt]);

            var entries = PazArchive.ReadPamt(Path.Combine(dir, "0.pamt"));
            Assert.Equal(["shadercache__/plain.txt", "shadercache__/big.padxil", "shadercache__/Locked.pars"], entries.Select(e => e.Path));
            Assert.All(entries, e => Assert.Equal(Path.Combine(dir, "0.paz"), e.PazFile));
            Assert.Equal(plain, PazArchive.Read(entries[0], encrypted: false));
            Assert.Equal(big, PazArchive.Read(entries[1], encrypted: false));
            Assert.Equal(secret, PazArchive.Read(entries[2], encrypted: true));   // by its lower-cased name
            Assert.NotEqual(secret, PazArchive.Read(entries[2], encrypted: false));
            Assert.Throws<InvalidDataException>(() => PazArchive.ReadPamt(Path.Combine(dir, "0.paz")));   // numbered, but no index inside
        }
        finally { Directory.Delete(dir, true); }
    }
}
