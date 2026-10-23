using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class ListReconciliationTests
{
    [Fact]
    public void Filtering_and_restoring_a_large_list_use_linear_identity_comparisons()
    {
        var comparisons = 0;
        var rows = Enumerable.Range(0, 5000).Select(i => new Row(i, () => comparisons++)).ToArray();
        var shown = new ObservableCollection<Row>(rows);
        Reconcile(rows, shown, "keep");
        Assert.Equal(rows.Where(row => row.Id % 2 == 0), shown);
        Assert.True(comparisons < rows.Length * 10, $"Filtering made {comparisons} identity comparisons.");
        comparisons = 0;
        Reconcile(rows, shown, "");
        Assert.Equal(rows, shown);
        Assert.True(comparisons < rows.Length * 10, $"Restoring made {comparisons} identity comparisons.");
        Assert.Same(rows[42], shown[42]);
    }

    [Fact]
    public void Reordering_and_emptying_keep_the_original_row_objects()
    {
        var rows = Enumerable.Range(0, 4).Select(i => new Row(i, () => { })).ToArray();
        var shown = new ObservableCollection<Row>(rows);
        var reversed = rows.Reverse().ToArray();
        Reconcile(reversed, shown, "");
        for (var i = 0; i < rows.Length; i++)
            Assert.Same(reversed[i], shown[i]);
        Reconcile([], shown, "");
        Assert.Empty(shown);
    }

    private static void Reconcile(IReadOnlyList<Row> rows, ObservableCollection<Row> shown, string filter) =>
        typeof(MainWindowViewModel).GetMethod("FilterInto", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(Row)).Invoke(null,
                [rows, shown, filter, (Func<Row, string>)(row => row.Id % 2 == 0 ? "keep" : "hide"), null]);

    private sealed class Row(int id, Action compared)
    {
        public int Id => id;
        public override int GetHashCode() => id;
        public override bool Equals(object? obj)
        {
            compared();
            return ReferenceEquals(this, obj);
        }
    }
}
