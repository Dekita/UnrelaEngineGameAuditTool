using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DekUnrealGameAudit.Gui;

/// <summary>Wires a ListView + search TextBox + empty-state hint into one reusable "structured result list"
/// panel, shared by the three Compare views instead of each re-implementing filtering/empty-state toggling.
/// The list is additive to each view's existing plain-text log (log/export/Copy Log/Markdown are untouched) -
/// this is a "browse" pane for counts-first, searchable, virtualized results (a plain WPF ListView already
/// virtualizes, unlike the capped TextBox log).</summary>
public sealed class CompareResultsPanel {
    private readonly TextBox _searchBox;
    private readonly TextBlock _emptyHint;
    private readonly List<CompareResultRow> _rows = new();
    private readonly ListCollectionView _view;

    public CompareResultsPanel(ListView listView, TextBox searchBox, TextBlock emptyHint, string emptyHintText) {
        _searchBox = searchBox;
        _emptyHint = emptyHint;
        _emptyHint.Text = emptyHintText;
        _view = new ListCollectionView(_rows) { Filter = FilterRow };
        listView.ItemsSource = _view;
        _searchBox.TextChanged += (_, _) => _view.Refresh();
        UpdateEmptyHintVisibility();
    }

    /// <summary>Replaces every row (a fresh run's results, not an incremental update). Mutates the same
    /// backing list the ListCollectionView was constructed over, then Refresh()es it, rather than swapping in
    /// a new list - a ListCollectionView's SourceCollection can't be reassigned after construction.</summary>
    public void SetRows(List<CompareResultRow> rows) {
        _rows.Clear();
        _rows.AddRange(rows);
        _view.Refresh();
        UpdateEmptyHintVisibility();
    }

    public void Clear() => SetRows(new List<CompareResultRow>());

    private bool FilterRow(object obj) {
        var row = (CompareResultRow)obj;
        var filter = _searchBox.Text;
        return string.IsNullOrWhiteSpace(filter) || row.Path.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateEmptyHintVisibility() {
        _emptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
