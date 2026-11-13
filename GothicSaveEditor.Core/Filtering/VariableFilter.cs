using System;
using System.Collections;
using System.Collections.Generic;
using GothicSaveEditor.Core.Primitives;
using GothicSaveEditor.Models;

namespace GothicSaveEditor.Core.Filtering;

public enum VariableCategoryFilter
{
    All,
    Quests,
    Dialogs,
    DefeatedEnemies,
    ModifiedOnly
}

public enum VariableValueFilter
{
    All,
    Zero,
    One,
    GreaterThanZero
}

public enum VariableSortColumn
{
    Name,
    Value
}

public sealed record VariableFilterCriteria(
    string SearchText,
    VariableCategoryFilter Category,
    VariableValueFilter ValueFilter);

public static class VariableFilter
{
    public static bool Matches(GothicVariable variable, VariableFilterCriteria criteria)
    {
        switch (criteria.Category)
        {
            case VariableCategoryFilter.Quests:
                if (!variable.VariableName.StartsWith("MIS_", StringComparison.OrdinalIgnoreCase))
                    return false;
                break;

            case VariableCategoryFilter.Dialogs:
                if (!variable.VariableName.StartsWith("DIA_", StringComparison.OrdinalIgnoreCase))
                    return false;
                break;

            case VariableCategoryFilter.DefeatedEnemies:
                if (!variable.VariableName.Contains("_ISDEAD", StringComparison.OrdinalIgnoreCase))
                    return false;
                break;

            case VariableCategoryFilter.ModifiedOnly:
                if (!variable.Modified)
                    return false;
                break;
        }

        switch (criteria.ValueFilter)
        {
            case VariableValueFilter.Zero:
                if (variable.Value != 0)
                    return false;
                break;

            case VariableValueFilter.One:
                if (variable.Value != 1)
                    return false;
                break;

            case VariableValueFilter.GreaterThanZero:
                if (variable.Value <= 0)
                    return false;
                break;
        }

        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            if (!variable.FullName.Contains(criteria.SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Sorts <see cref="GothicVariable"/> by Name (case-insensitive natural sort) or Value (numeric int32),
/// ascending or descending.
/// </summary>
public sealed class VariableGridComparer : IComparer<GothicVariable>, IComparer
{
    public VariableSortColumn Column { get; }
    public bool Descending { get; }

    public VariableGridComparer(VariableSortColumn column, bool descending)
    {
        Column = column;
        Descending = descending;
    }

    public int Compare(GothicVariable? x, GothicVariable? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return Descending ? 1 : -1;
        if (y is null) return Descending ? -1 : 1;

        int result = Column switch
        {
            VariableSortColumn.Value => x.Value.CompareTo(y.Value),
            _ => CompareNaturalIgnoreCase(x.FullName.AsSpan(), y.FullName.AsSpan())
        };

        if (result == 0 && Column == VariableSortColumn.Value)
        {
            // Stable tie-breaker by natural name
            result = CompareNaturalIgnoreCase(x.FullName.AsSpan(), y.FullName.AsSpan());
        }

        return Descending ? -result : result;
    }

    int IComparer.Compare(object? x, object? y) => Compare(x as GothicVariable, y as GothicVariable);

    public static int CompareNaturalIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
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

                int cmp = CompareNumericSpans(a.Slice(iStart, i - iStart), b.Slice(jStart, j - jStart));
                if (cmp != 0) return cmp;
            }
            else if (!aDigit && !bDigit)
            {
                int iStart = i;
                while (i < a.Length && !char.IsDigit(a[i])) i++;

                int jStart = j;
                while (j < b.Length && !char.IsDigit(b[j])) j++;

                int cmp = a.Slice(iStart, i - iStart)
                    .CompareTo(b.Slice(jStart, j - jStart), StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            else
            {
                return char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
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
        return cmp != 0 ? cmp : a.Length.CompareTo(b.Length);
    }
}
