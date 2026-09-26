namespace DekUnrealGameAudit.Gui;

/// <summary>Canonical names for the settings fields that appear on more than one tab, used as the vocabulary for
/// <see cref="ISharedFieldView"/> so every view and MainWindow agree on the same strings without typos.</summary>
public static class SharedField {
    public const string PaksFolder = nameof(PaksFolder);
    public const string ModsFolder = nameof(ModsFolder);
    public const string UeVersion = nameof(UeVersion);
    public const string AesKey = nameof(AesKey);
    public const string ScopeRulesPath = nameof(ScopeRulesPath);
    public const string HeaderDumpFolder = nameof(HeaderDumpFolder);
}

public record SharedFieldChangedEventArgs(string FieldName, string Value);

/// <summary>Implemented by every view that shows a field also shown elsewhere (Hash Game Files, Hash Mod Files,
/// Hash Header Files, Scope Rules, Quick Actions). Raising <see cref="SharedFieldChanged"/> with a
/// <see cref="SharedField"/> name lets MainWindow push the new value to every other view that cares, without a
/// bespoke sync method per field-group - see MainWindow.xaml.cs's OnSharedFieldChanged. A view only needs to
/// recognize the field names it actually displays; <see cref="SetSharedField"/> silently ignores the rest.</summary>
public interface ISharedFieldView {
    event EventHandler<SharedFieldChangedEventArgs>? SharedFieldChanged;
    void SetSharedField(string fieldName, string value);
}
