using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GothicSaveEditor.Models;

public class GothicVariable : IComparable<GothicVariable>, IComparable, INotifyPropertyChanged
{
    public string VariableName { get; }
    private int _value;

    public int Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                if (!Modified)
                {
                    Modified = true;
                    OnPropertyChanged(nameof(Modified));
                }
                OnPropertyChanged();
            }
        }
    }

    public bool Modified { get; private set; }

    public int Position { get; }

    private readonly int? _arrayIndex;

    public string FullName { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GothicVariable(string varName, int pos, int value, int? arrayIndex = null)
    {
        VariableName = varName;
        Position = pos;
        _value = value;
        _arrayIndex = arrayIndex;
        FullName = arrayIndex == null ? varName : $"{varName}[{arrayIndex}]";
    }

    public void SetUnModified()
    {
        if (Modified)
        {
            Modified = false;
            OnPropertyChanged(nameof(Modified));
        }
    }

    public int CompareTo(GothicVariable? other)
    {
        if (other is null) return 1;
        return string.CompareOrdinal(FullName, other.FullName);
    }

    public int CompareTo(object? obj) => CompareTo(obj as GothicVariable);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
