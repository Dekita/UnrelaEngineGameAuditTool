using DekUnrealGameAudit.Gui;

namespace DekUnrealGameAudit.Tests;

public class AppSettingsTests {
    [Fact]
    public void Snapshot_CopiesEveryEditableSettingAndIsIndependent() {
        var settings = new AppSettings {
            UeVersion = "GAME_UE5_3",
            AesKey = "secret",
            PaksFolder = "paks",
            QuickActionsOutputPath = "runs"
        };

        var snapshot = settings.Snapshot();

        Assert.True(settings.ContentEquals(snapshot));
        snapshot.PaksFolder = "other";
        Assert.False(settings.ContentEquals(snapshot));
        Assert.Equal("paks", settings.PaksFolder);
    }

    [Fact]
    public void CopyFrom_CopiesAesKeyWithoutSerializingIt() {
        var target = new AppSettings();

        target.CopyFrom(new AppSettings { AesKey = "secret", ModsFolder = "mods" });

        Assert.Equal("secret", target.AesKey);
        Assert.Equal("mods", target.ModsFolder);
    }
}
