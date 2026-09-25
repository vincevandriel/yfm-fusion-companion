using System.Collections.ObjectModel;
using System.Windows.Controls.Primitives;

namespace YfmCompanion.Desktop;

/// <summary>Reconcile live card rows without resetting their collection, selection or scroll viewport.</summary>
internal static class StableCardItems
{
    public static void Update<T, TKey>(Selector control, IReadOnlyList<T> rows, Func<T, TKey> key) where T : class
    {
        if (control.ItemsSource is not ObservableCollection<T> current)
        {
            control.ItemsSource = new ObservableCollection<T>(rows);
            return;
        }
        if (current.SequenceEqual(rows)) return;
        var selected = control.SelectedItem as T;
        var selectedKey = selected is null ? default : key(selected);
        for (var index = 0; index < rows.Count; index++)
        {
            var matching = -1;
            for (var candidate = index; candidate < current.Count; candidate++)
                if (EqualityComparer<TKey>.Default.Equals(key(current[candidate]), key(rows[index]))) { matching = candidate; break; }
            if (matching < 0) current.Insert(index, rows[index]);
            else
            {
                if (matching != index) current.Move(matching, index);
                if (!EqualityComparer<T>.Default.Equals(current[index], rows[index])) current[index] = rows[index];
            }
        }
        while (current.Count > rows.Count) current.RemoveAt(current.Count - 1);
        if (selected is not null)
            control.SelectedItem = current.FirstOrDefault(row => EqualityComparer<TKey>.Default.Equals(key(row), selectedKey));
    }
}
