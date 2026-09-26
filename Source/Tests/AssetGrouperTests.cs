using System.Security.Cryptography;
using System.Text;
using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

public class AssetGrouperTests {
    [Theory]
    [InlineData("GameName/Content/Weapons/Sword.uasset", "Content/Weapons/Sword.uasset")]
    [InlineData("GameName\\Content\\Weapons\\Sword.uasset", "Content/Weapons/Sword.uasset")]
    [InlineData("/GameName/Content/Sword.uasset", "Content/Sword.uasset")]
    [InlineData("NoSlash", "NoSlash")]
    public void NormalizePath_DropsOnlyTheMountRootSegment(string raw, string expected) {
        Assert.Equal(expected, AssetGrouper.NormalizePath(raw));
    }

    [Theory]
    [InlineData("Engine/Content/Localization/Foo.uasset", "Engine/Content/Localization/Foo.uasset")]
    [InlineData("engine/Content/Localization/Foo.uasset", "engine/Content/Localization/Foo.uasset")]
    [InlineData("ENGINE\\Content\\Localization\\Foo.uasset", "ENGINE/Content/Localization/Foo.uasset")]
    public void NormalizePath_NeverStripsTheEngineRoot(string raw, string expected) {
        Assert.Equal(expected, AssetGrouper.NormalizePath(raw));
    }

    // INV-02's actual regression case: before this fix, both of these normalized to the identical
    // "Content/Localization/Foo.uasset" key - two genuinely different assets silently merged into one
    // GroupByLogicalAsset entry, hashed together as if they were parts of the same logical asset.
    [Fact]
    public void NormalizePath_EngineAndGameRootsWithMatchingSubPaths_NoLongerCollide() {
        var engineKey = AssetGrouper.NormalizePath("Engine/Content/Localization/Foo.uasset");
        var gameKey = AssetGrouper.NormalizePath("SomeGameName/Content/Localization/Foo.uasset");
        Assert.NotEqual(engineKey, gameKey);
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void HashFramedParts_SameInput_ProducesSameHash() {
        var parts = new[] { ("Foo.uasset", Bytes("AAAA")), ("Foo.uexp", Bytes("BB")) };
        var (hash1, _) = AssetGrouper.HashFramedParts(parts);
        var (hash2, _) = AssetGrouper.HashFramedParts(parts);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashFramedParts_SizeIsSumOfPartLengths() {
        var parts = new[] { ("Foo.uasset", Bytes("AAAA")), ("Foo.uexp", Bytes("BB")) };
        var (_, size) = AssetGrouper.HashFramedParts(parts);
        Assert.Equal(6, size);
    }

    [Fact]
    public void HashFramedParts_IsIndependentOfInputListOrder() {
        var partsInOneOrder = new[] { ("Foo.uasset", Bytes("AAAA")), ("Foo.uexp", Bytes("BB")) };
        var partsInReverseOrder = new[] { ("Foo.uexp", Bytes("BB")), ("Foo.uasset", Bytes("AAAA")) };

        var (hash1, _) = AssetGrouper.HashFramedParts(partsInOneOrder);
        var (hash2, _) = AssetGrouper.HashFramedParts(partsInReverseOrder);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashFramedParts_DifferentContent_ProducesDifferentHash() {
        var (hash1, _) = AssetGrouper.HashFramedParts(new[] { ("Foo.uasset", Bytes("AAAA")) });
        var (hash2, _) = AssetGrouper.HashFramedParts(new[] { ("Foo.uasset", Bytes("ZZZZ")) });
        Assert.NotEqual(hash1, hash2);
    }

    // INV-04's actual regression case: two different splits of one logical asset's parts whose raw
    // concatenated bytes are identical ("123456" either way), only the part boundary moved. The old,
    // unframed scheme (reproduced below via a plain SHA256 over the concatenated bytes) can't tell these
    // apart - HashFramedParts must.
    [Fact]
    public void HashFramedParts_DifferentPartBoundaries_SameConcatenatedBytes_ProduceDifferentHashes() {
        var splitA = new[] { ("Foo.uexp", Bytes("1234")), ("Foo.ubulk", Bytes("56")) };
        var splitB = new[] { ("Foo.uexp", Bytes("123")), ("Foo.ubulk", Bytes("456")) };

        // Prove the concatenated bytes really are identical, and that naive unframed concatenation - the
        // bug this replaces - really does collide, before asserting the fix doesn't.
        var concatenatedA = splitA.SelectMany(p => p.Item2).ToArray();
        var concatenatedB = splitB.SelectMany(p => p.Item2).ToArray();
        Assert.Equal(concatenatedA, concatenatedB);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(concatenatedA)), Convert.ToHexString(SHA256.HashData(concatenatedB)));

        var (hashA, _) = AssetGrouper.HashFramedParts(splitA);
        var (hashB, _) = AssetGrouper.HashFramedParts(splitB);
        Assert.NotEqual(hashA, hashB);
    }
}
