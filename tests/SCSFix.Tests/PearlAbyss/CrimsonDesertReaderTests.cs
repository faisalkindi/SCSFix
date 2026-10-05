using System.Text;
using SCSFix.Core;
using SCSFix.Core.PearlAbyss;

namespace SCSFix.Tests.PearlAbyss;

public class CrimsonDesertReaderTests
{
    static PazEntry Entry(string path) => new(path, "0.paz", 0, 10, 10, 0);

    [Fact]
    public void ShaderFileNamesSplitIntoTheirSevenFields()
    {
        var f = CrimsonDesertReader.Fields(Entry("shadercache__/023cb92f_3f8f0b31_5_1eb5f012_3_deba1dcd_33e3354e.padxil"))!;
        Assert.Equal(["023cb92f", "3f8f0b31", "5", "1eb5f012", "3", "deba1dcd", "33e3354e"], f);
        Assert.Null(CrimsonDesertReader.Fields(Entry("shadercache__/arealightobject.pars")));
        Assert.Null(CrimsonDesertReader.Fields(Entry("shadercache__/a_b_c.padxil")));
    }

    [Theory]
    [InlineData("0", Stage.Vertex)]
    [InlineData("1", Stage.Hull)]
    [InlineData("2", Stage.Domain)]
    [InlineData("3", Stage.Geometry)]
    [InlineData("4", Stage.Pixel)]
    [InlineData("5", Stage.Compute)]
    public void ThirdFieldIsTheStage(string field, Stage stage) => Assert.Equal(stage, CrimsonDesertReader.StageOf(field));

    [Fact]
    public void RayTracingLibrariesHaveNoGraphicsStage() => Assert.All(new[] { "6", "7", "8", "x" }, f => Assert.Null(CrimsonDesertReader.StageOf(f)));

    /// <summary>Two names from the game's own files: f0 of a pass is the lookup3 hash (seed 0xC5EDE) of its PascalCase name, f1 of
    /// its source file name; the .pars files carry the lower-case name.</summary>
    [Fact]
    public void AFileNameHashesToItsFieldInPascalCase()
    {
        static uint H(string s) => PazArchive.HashLittle(Encoding.UTF8.GetBytes(s), 0x000C5EDE);
        Assert.Equal(0xb3e23caau, H("Sea"));               // an f0 of 20 shaders
        Assert.Equal(0x3f8f0b31u, H("Sea.hlsl"));          // the f1 of 023cb92f_3f8f0b31_...
        Assert.Contains("AreaLightObject", CrimsonDesertReader.Capitalisations("arealightobject", 6));
        Assert.Contains("CopyEffectMaterialID", CrimsonDesertReader.Capitalisations("copyeffectmaterialid", 6));
        Assert.Equal(1, CrimsonDesertReader.Capitalisations("sky", 1).Count());            // only the first letter
        Assert.All(CrimsonDesertReader.Capitalisations("abcdef", 3), c => Assert.True(c.Count(char.IsUpper) <= 3 && char.IsUpper(c[0])));
    }

    [Fact]
    public void ADirectoryWithoutTheArchivesIsNotCrimsonDesert()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try { Assert.Null(new CrimsonDesertReader().Detect(new Game("x:1", "x", Store.Other, dir, Path.Combine(dir, "x.exe")))); }
        finally { Directory.Delete(dir, true); }
    }
}
