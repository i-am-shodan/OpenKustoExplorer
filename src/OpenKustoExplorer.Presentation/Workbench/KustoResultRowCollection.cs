using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Publishes complete visible-row replacements as one collection reset.
/// </summary>
internal sealed class KustoResultRowCollection : ObservableCollection<KustoResultRowViewModel>
{
    /// <summary>
    /// Replaces all visible rows without raising per-row collection changes.
    /// </summary>
    /// <param name="rows">The complete visible row projection.</param>
    internal void ReplaceWith(IReadOnlyList<KustoResultRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        CheckReentrancy();
        Items.Clear();

        foreach (KustoResultRowViewModel row in rows)
        {
            Items.Add(row);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
