using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GothicSaveEditor.Tests.Legacy;

internal sealed class LegacyVariable
{
    public string VariableName { get; }
    public int Value { get; }
    public int Position { get; }
    private readonly int? _arrayIndex;
    public string FullName => _arrayIndex == null ? VariableName : $"{VariableName}[{_arrayIndex}]";

    public LegacyVariable(string varName, int pos, int value, int? arrayIndex = null)
    {
        VariableName = varName;
        Position = pos;
        Value = value;
        _arrayIndex = arrayIndex;
    }
}

internal readonly struct LegacyGothicVar
{
    public int[] Positions { get; }
    public int[] Values { get; }

    public LegacyGothicVar(int[] positions, int[] values)
    {
        Positions = positions;
        Values = values;
    }
}

internal sealed class LegacyVariablesComparer : IComparer<LegacyVariable>
{
    private enum ChunkType { Alphanumeric, Numeric }

    private static bool InChunk(char ch, char otherCh)
    {
        var type = char.IsDigit(otherCh) ? ChunkType.Numeric : ChunkType.Alphanumeric;
        return (type != ChunkType.Alphanumeric || !char.IsDigit(ch)) &&
               (type != ChunkType.Numeric || char.IsDigit(ch));
    }

    public int Compare(LegacyVariable? x, LegacyVariable? y)
    {
        if (x == null || y == null) return 0;
        var s1 = x.FullName;
        var s2 = y.FullName;
        if (s1 == null || s2 == null) return 0;

        int thisMarker = 0;
        int thatMarker = 0;
        while ((thisMarker < s1.Length) || (thatMarker < s2.Length))
        {
            if (thisMarker >= s1.Length) return -1;
            if (thatMarker >= s2.Length) return 1;

            char thisCh = s1[thisMarker];
            char thatCh = s2[thatMarker];
            var thisChunk = new StringBuilder();
            var thatChunk = new StringBuilder();

            while ((thisMarker < s1.Length) && (thisChunk.Length == 0 || InChunk(thisCh, thisChunk[0])))
            {
                thisChunk.Append(thisCh);
                thisMarker++;
                if (thisMarker < s1.Length) thisCh = s1[thisMarker];
            }

            while ((thatMarker < s2.Length) && (thatChunk.Length == 0 || InChunk(thatCh, thatChunk[0])))
            {
                thatChunk.Append(thatCh);
                thatMarker++;
                if (thatMarker < s2.Length) thatCh = s2[thatMarker];
            }

            int result = 0;
            if (char.IsDigit(thisChunk[0]) && char.IsDigit(thatChunk[0]))
            {
                var thisNumericChunk = Convert.ToInt32(thisChunk.ToString());
                var thatNumericChunk = Convert.ToInt32(thatChunk.ToString());
                if (thisNumericChunk < thatNumericChunk) result = -1;
                if (thisNumericChunk > thatNumericChunk) result = 1;
            }
            else
            {
                result = string.Compare(thisChunk.ToString(), thatChunk.ToString(), StringComparison.Ordinal);
            }

            if (result != 0) return result;
        }
        return 0;
    }
}

/// <summary>
/// Frozen copy of the original SaveDatReader from Phase 1 for snapshot comparison.
/// </summary>
internal static class LegacyReferenceReader
{
    private const byte ValueMarker = 0x12;
    private const byte NameMarker = 0x01;
    private const byte DialogMarker = 0x06;
    private const byte NormalVarMarker = 0x02;

    private static bool _dialogBegin;
    private static bool _dialogEnd;
    private static bool _dialog;
    private static bool _mission;

    public static List<LegacyVariable> Read(string path, bool sort = true)
    {
        var byteArray = File.ReadAllBytes(path);
        var controlPoints = FindControlPoints(byteArray);

        _dialogBegin = false;
        _dialogEnd = false;
        _dialog = true;
        _mission = false;

        return ParseSaveGame(byteArray, controlPoints.Item1, controlPoints.Item2, sort);
    }

    private static Tuple<int, int> FindControlPoints(byte[] bytes)
    {
        var index = 0;
        for (; index < bytes.Length; index++)
        {
            if (bytes[index] == 0x02
                && bytes[index + 1] == 0x00
                && bytes[index + 2] == 0x00
                && bytes[index + 3] == 0x00
                && bytes[index + 4] == 0x01
                && bytes[index + 5] == 0x00
                && bytes[index + 6] == 0x00
                && bytes[index + 7] == 0x00)
            {
                index += 8;
                break;
            }
        }

        int maxByte = BitConverter.ToInt32(bytes, index);
        if (maxByte == 0)
            throw new Exception("SRMaxByteError");
        index += 4;

        return new Tuple<int, int>(index, maxByte);
    }

    private static LegacyGothicVar ReadVariable(ref byte[] bytes, ref int rIndex)
    {
        var values = new int[1];
        var positions = new int[1];

        var arraySize = int.MaxValue;
        var arrIteration = -1;
        while (bytes[rIndex] == ValueMarker)
        {
            rIndex += 5;
            if (bytes[rIndex] == NameMarker)
            {
                break;
            }

            rIndex++;
            if (bytes[rIndex - 1] == NormalVarMarker)
            {
                if (_dialogBegin)
                {
                    _mission = true;
                    _dialog = false;
                    _dialogEnd = true;
                    _dialogBegin = false;
                }

                positions[0] = rIndex;
                values[0] = BitConverter.ToInt32(bytes, rIndex);
                if (arrIteration == -1)
                {
                    arraySize = values[0];
                }

                if (arrIteration >= 0 && arrIteration < arraySize)
                {
                    if (arrIteration == 0)
                    {
                        positions = new int[arraySize];
                        values = new int[arraySize];
                    }

                    positions[arrIteration] = rIndex;
                    values[arrIteration] = BitConverter.ToInt32(bytes, rIndex);
                }
                arrIteration++;
            }
            else if (bytes[rIndex - 1] == DialogMarker)
            {
                values[0] = BitConverter.ToInt32(bytes, rIndex);
                positions[0] = rIndex;
            }
            rIndex += 4;

            if (_dialog && bytes[rIndex - 5] == DialogMarker)
            {
                _dialogBegin = true;
                return new LegacyGothicVar(positions, values);
            }
        }
        return new LegacyGothicVar(positions, values);
    }

    private static string ReadName(ref byte[] bytes, ref int index)
    {
        int length = BitConverter.ToInt16(bytes, ++index);
        index += 2;
        if (length <= 0)
            return "";

        var sb = new StringBuilder(2048);
        var readTo = index + length;
        for (; index < readTo; index++)
        {
            sb.Append((char)bytes[index]);
        }
        index--;

        var str = sb.ToString();
        if (_mission == false)
        {
            return str;
        }

        if (str == "[]")
            _mission = false;

        return "";
    }

    private static List<LegacyVariable> ParseSaveGame(byte[] byteArray, int startIndex, int lastIndex, bool sort)
    {
        var needToReadValue = false;
        var varname = "";
        var gothicVar = new LegacyGothicVar();
        var variablesList = new List<LegacyVariable>();

        for (var index = startIndex; index < lastIndex; index++)
        {
            if (byteArray[index] == ValueMarker)
            {
                if (_dialog)
                {
                    gothicVar = ReadVariable(ref byteArray, ref index);
                }
                else
                {
                    if (needToReadValue && varname.Trim().Length > 0)
                    {
                        gothicVar = ReadVariable(ref byteArray, ref index);
                        if (_dialogEnd)
                        {
                            _dialogEnd = false;
                        }
                        else
                        {
                            variablesList.AddRange(gothicVar.Values.Select((t, i) => gothicVar.Values.Length > 1
                                ? new LegacyVariable(varname, gothicVar.Positions[i], gothicVar.Values[0], i)
                                : new LegacyVariable(varname, gothicVar.Positions[i], gothicVar.Values[0])));
                        }
                        index--;
                        needToReadValue = false;
                    }
                }
            }
            else if (byteArray[index] == NameMarker)
            {
                varname = ReadName(ref byteArray, ref index);
                if (varname.Trim().Length > 0)
                {
                    if (_dialog && _dialogBegin)
                    {
                        variablesList.AddRange(gothicVar.Values.Select((t, i) => gothicVar.Values.Length > 1
                            ? new LegacyVariable(varname, gothicVar.Positions[i], gothicVar.Values[0], i)
                            : new LegacyVariable(varname, gothicVar.Positions[i], gothicVar.Values[0])));
                    }
                    else
                    {
                        needToReadValue = true;
                    }
                }
            }
        }

        if (variablesList.Count == 0)
            throw new Exception("SREmptyVariablesList");

        if (sort)
            variablesList.Sort(new LegacyVariablesComparer());
        return variablesList;
    }
}
