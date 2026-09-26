namespace DekUnrealGameAudit.Gui;

/// <summary>One row in a Compare view's structured, searchable result list - shared shape across
/// CompareGameFilesView/CompareModFilesView/CompareHeaderFilesView so the same ListView style, filter box
/// wiring and empty-state pattern works identically on all three, even though what each field means differs
/// per view (e.g. Path is an asset path for Compare Game Files, a mod name for Compare Mod Files).</summary>
public record CompareResultRow(string Category, string Path, string Detail);
