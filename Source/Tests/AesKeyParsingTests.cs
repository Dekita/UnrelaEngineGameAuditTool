using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

public class AesKeyParsingTests {
    [Fact]
    public void ParseAesKeys_BareHexImpliesMainKeyGuid() {
        var result = AesKeyParsing.ParseAesKeys(["DEADBEEF"]);

        var (guid, key) = Assert.Single(result);
        Assert.Equal(ProviderFactory.MainKeyGuid, guid);
        Assert.Equal("DEADBEEF", key);
    }

    [Fact]
    public void ParseAesKeys_GuidColonKeySplitsOnFirstColonOnly() {
        var result = AesKeyParsing.ParseAesKeys(["0000-guid:DEAD:BEEF"]);

        var (guid, key) = Assert.Single(result);
        Assert.Equal("0000-guid", guid);
        Assert.Equal("DEAD:BEEF", key);
    }

    [Fact]
    public void ParseAesKeysMultiline_SplitsOnNewlinesAndSkipsBlankLines() {
        var result = AesKeyParsing.ParseAesKeysMultiline("AAA:111\n\n  BBB:222  \n");

        Assert.Equal([("AAA", "111"), ("BBB", "222")], result);
    }

    [Fact]
    public void ParseAesKeysMultiline_NullOrEmptyProducesNoKeys() {
        Assert.Empty(AesKeyParsing.ParseAesKeysMultiline(null));
        Assert.Empty(AesKeyParsing.ParseAesKeysMultiline(""));
    }
}
