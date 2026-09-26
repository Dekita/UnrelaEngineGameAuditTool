using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for a real bug hit this session: CamelCasePropertyNamesContractResolver's
/// default ProcessDictionaryKeys=true camelCases Dictionary&lt;string,T&gt; keys on write (e.g. an asset path
/// or a mod/type name starting with an uppercase letter) without ReadFile ever reversing it on read, silently
/// mangling any dictionary key that isn't already camelCase relative to the value written under it.</summary>
public class JsonUtilTests {
    [Fact]
    public void WriteFile_ThenReadFile_PreservesDictionaryKeyCasingExactly() {
        var path = Path.Combine(Path.GetTempPath(), $"json-util-test-{Guid.NewGuid():N}.json");
        try {
            var original = new GameManifest();
            original.Assets["Content/AAAffectedReticle_C.uasset"] = new AssetEntry { Hash = "ABC", Size = 1 };

            JsonUtil.WriteFile(path, original);
            var roundTripped = JsonUtil.ReadFile<GameManifest>(path);

            Assert.True(roundTripped.Assets.ContainsKey("Content/AAAffectedReticle_C.uasset"));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteFile_StillCamelCasesRegularProperties() {
        var path = Path.Combine(Path.GetTempPath(), $"json-util-test-{Guid.NewGuid():N}.json");
        try {
            JsonUtil.WriteFile(path, new GameManifest { UeVersion = "GAME_UE5_3" });
            var json = File.ReadAllText(path);

            Assert.Contains("\"ueVersion\"", json);
        } finally {
            File.Delete(path);
        }
    }
}
