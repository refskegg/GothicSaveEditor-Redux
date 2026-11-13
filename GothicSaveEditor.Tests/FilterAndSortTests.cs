using System.Collections.Generic;
using System.Linq;
using GothicSaveEditor.Core.Filtering;
using GothicSaveEditor.Models;

namespace GothicSaveEditor.Tests;

public class FilterAndSortTests
{
    private static List<GothicVariable> CreateSampleVariables()
    {
        var misArrest = new GothicVariable("MIS_ARREST", 100, 1);
        var misTamora2 = new GothicVariable("MIS_TAMORA2", 200, 0);
        var misTamora10 = new GothicVariable("mis_tamora10", 300, 2);
        var diaTest = new GothicVariable("DIA_LESTER_HELLO", 400, 1);
        var enemyDead = new GothicVariable("ORC_WARRIOR_ISDEAD", 500, 1);
        var counter = new GothicVariable("KILL_COUNTER", 600, 10);
        var negCounter = new GothicVariable("TEMP_OFFSET", 700, -5);

        misTamora10.Value = 3; // Modified = true

        return new List<GothicVariable>
        {
            misTamora10,
            counter,
            diaTest,
            misArrest,
            negCounter,
            enemyDead,
            misTamora2
        };
    }

    [Fact]
    public void SortByName_IsNaturalAndCaseInsensitive_AscendingAndDescending()
    {
        var items = new List<GothicVariable>
        {
            new("TAMORA10", 10, 0),
            new("tamora2", 20, 0),
            new("alpha", 30, 0),
            new("Beta", 40, 0)
        };

        items.Sort(new VariableGridComparer(VariableSortColumn.Name, descending: false));
        Assert.Equal(new[] { "alpha", "Beta", "tamora2", "TAMORA10" }, items.Select(v => v.FullName));

        items.Sort(new VariableGridComparer(VariableSortColumn.Name, descending: true));
        Assert.Equal(new[] { "TAMORA10", "tamora2", "Beta", "alpha" }, items.Select(v => v.FullName));
    }

    [Fact]
    public void SortByValue_IsNumericInt32_NotLexicographic_AscendingAndDescending()
    {
        var items = new List<GothicVariable>
        {
            new("V_10", 10, 10),
            new("V_2", 20, 2),
            new("V_1", 30, 1),
            new("V_NEG", 40, -5),
            new("V_100", 50, 100),
            new("V_0", 60, 0)
        };

        items.Sort(new VariableGridComparer(VariableSortColumn.Value, descending: false));
        Assert.Equal(new[] { -5, 0, 1, 2, 10, 100 }, items.Select(v => v.Value));

        items.Sort(new VariableGridComparer(VariableSortColumn.Value, descending: true));
        Assert.Equal(new[] { 100, 10, 2, 1, 0, -5 }, items.Select(v => v.Value));

        // Verify Positions are untouched
        Assert.Equal(50, items[0].Position);
        Assert.Equal(40, items[^1].Position);
    }

    [Theory]
    [InlineData(VariableCategoryFilter.All, VariableValueFilter.All, "", 7)]
    [InlineData(VariableCategoryFilter.Quests, VariableValueFilter.All, "", 3)]
    [InlineData(VariableCategoryFilter.Dialogs, VariableValueFilter.All, "", 1)]
    [InlineData(VariableCategoryFilter.DefeatedEnemies, VariableValueFilter.All, "", 1)]
    [InlineData(VariableCategoryFilter.ModifiedOnly, VariableValueFilter.All, "", 1)]
    [InlineData(VariableCategoryFilter.All, VariableValueFilter.Zero, "", 1)]
    [InlineData(VariableCategoryFilter.All, VariableValueFilter.One, "", 3)]
    [InlineData(VariableCategoryFilter.All, VariableValueFilter.GreaterThanZero, "", 5)]
    [InlineData(VariableCategoryFilter.Quests, VariableValueFilter.GreaterThanZero, "tamora", 1)]
    [InlineData(VariableCategoryFilter.Quests, VariableValueFilter.One, "arrest", 1)]
    public void CombinedFilters_ReturnExpectedMatches(
        VariableCategoryFilter category,
        VariableValueFilter valueFilter,
        string search,
        int expectedCount)
    {
        var vars = CreateSampleVariables();
        var criteria = new VariableFilterCriteria(search, category, valueFilter);

        var matched = vars.Where(v => VariableFilter.Matches(v, criteria)).ToList();
        Assert.Equal(expectedCount, matched.Count);
    }
}
