using System;
using System.Collections.Generic;
using GothicSaveEditor.Models;

namespace GothicSaveEditor.Core.Primitives;

/// <summary>
/// Allocation-free, overflow-safe natural comparer for <see cref="GothicVariable"/>.
/// </summary>
public sealed class VariablesComparer : IComparer<GothicVariable>
{
    public static readonly VariablesComparer Instance = new();

    public int Compare(GothicVariable? x, GothicVariable? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return CompareNatural(x.FullName.AsSpan(), y.FullName.AsSpan());
    }

    public static int CompareNatural(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        int i = 0;
        int j = 0;

        while (i < a.Length && j < b.Length)
        {
            bool aDigit = char.IsDigit(a[i]);
            bool bDigit = char.IsDigit(b[j]);

            if (aDigit && bDigit)
            {
                int iStart = i;
                while (i < a.Length && char.IsDigit(a[i])) i++;

                int jStart = j;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                var aNum = a.Slice(iStart, i - iStart);
                var bNum = b.Slice(jStart, j - jStart);

                int cmp = CompareNumericSpans(aNum, bNum);
                if (cmp != 0) return cmp;
            }
            else if (!aDigit && !bDigit)
            {
                int iStart = i;
                while (i < a.Length && !char.IsDigit(a[i])) i++;

                int jStart = j;
                while (j < b.Length && !char.IsDigit(b[j])) j++;

                int cmp = a.Slice(iStart, i - iStart).CompareTo(b.Slice(jStart, j - jStart), StringComparison.Ordinal);
                if (cmp != 0) return cmp;
            }
            else
            {
                return a[i].CompareTo(b[j]);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    private static int CompareNumericSpans(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var aTrimmed = a.TrimStart('0');
        var bTrimmed = b.TrimStart('0');

        if (aTrimmed.Length != bTrimmed.Length)
            return aTrimmed.Length.CompareTo(bTrimmed.Length);

        int cmp = aTrimmed.CompareTo(bTrimmed, StringComparison.Ordinal);
        if (cmp != 0) return cmp;

        // Equal numeric value: tie-break by original digit count (e.g. "01" vs "1")
        return a.Length.CompareTo(b.Length);
    }
}
