using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GothicSaveEditor.Core.Primitives;
using GothicSaveEditor.Core.Utils;
using GothicSaveEditor.Models;

namespace GothicSaveEditor.Core.Readers;

/// <summary>
/// Structured, bounds-checked, thread-safe parser for Gothic 1 / 2 SAVEDAT.SAV (ZenGin BIN_SAFE format).
/// </summary>
public sealed class BinSafeReader
{
    private const byte TypeString = 0x01;
    private const byte TypeInt = 0x02;
    private const byte TypeFloat = 0x03;
    private const byte TypeByte = 0x04;
    private const byte TypeWord = 0x05;
    private const byte TypeBool = 0x06;
    private const byte TypeVec3 = 0x07;
    private const byte TypeColor = 0x08;
    private const byte TypeRaw = 0x09;
    private const byte TypeRawFloat = 0x0A;
    private const byte TypeEnum10 = 0x10;
    private const byte TypeEnum11 = 0x11;
    private const byte TypeHash = 0x12;

    private enum ParseSection
    {
        HeaderScalars,
        Dialogs,
        Missions,
        Variables
    }

    public static SaveGame ReadSaveGame(
        string path,
        Encoding? encoding = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var reader = new BinSafeReader();
        return reader.Read(path, encoding, progress, cancellationToken);
    }

    public SaveGame Read(
        string path,
        Encoding? encoding = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        encoding ??= EncodingHelper.GetEncoding(1252);
        byte[] bytes = File.ReadAllBytes(path);
        byte[] sha256 = SHA256.HashData(bytes);

        var variables = ParseBytes(bytes, encoding, progress, cancellationToken);
        return new SaveGame(path, variables, bytes.LongLength, sha256);
    }

    public List<GothicVariable> ParseBytes(
        ReadOnlySpan<byte> data,
        Encoding? encoding = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        encoding ??= EncodingHelper.GetEncoding(1252);

        int bodyStart = ParseAsciiHeader(data);
        if (bodyStart + 12 > data.Length)
            throw new InvalidDataException("SRHeaderError: BinSafe binary header is truncated.");

        int version = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(bodyStart, 4));
        int objectCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(bodyStart + 4, 4));
        int hashTableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(bodyStart + 8, 4));

        int pos = bodyStart + 12;
        if (hashTableOffset < pos || hashTableOffset > data.Length)
            throw new InvalidDataException(
                $"SRMaxByteError: Invalid hashTableOffset {hashTableOffset} (bodyStart={pos}, fileLength={data.Length}).");

        _ = version;
        _ = objectCount;

        var variables = new List<GothicVariable>(16384);
        var section = ParseSection.HeaderScalars;

        // Dialog state
        bool hasPendingDialogBool = false;
        int pendingDialogPos = 0;
        int pendingDialogVal = 0;

        // Mission (oCLogManager) state
        int remainingTopics = 0;
        int topicFieldsLeft = 0;
        int remainingEntries = 0;
        int chunkDepth = 0;

        // Variable (zCParser) state
        bool hasSymbolCount = false;
        string? currentVarName = null;
        int expectedArrayElements = -1;
        int currentArrayIndex = 0;
        bool isArray = false;

        int nextProgressPos = pos + Math.Max(4096, (hashTableOffset - pos) / 50);
        int totalSpan = Math.Max(1, hashTableOffset - pos);

        while (pos < hashTableOffset)
        {
            if (pos >= nextProgressPos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((double)(pos - bodyStart) / totalSpan * 0.85);
                nextProgressPos = pos + Math.Max(4096, totalSpan / 50);
            }

            byte tag = data[pos++];

            if (tag == TypeString)
            {
                string chunkStr = ReadLengthPrefixedString(data, ref pos, encoding);
                if (chunkStr == "[]")
                {
                    if (chunkDepth > 0) chunkDepth--;
                    if ((section == ParseSection.Dialogs || section == ParseSection.Missions) && chunkDepth == 0)
                    {
                        section = ParseSection.Variables;
                    }
                }
                else if (chunkStr.StartsWith("[%", StringComparison.Ordinal))
                {
                    chunkDepth++;
                }
                continue;
            }

            if (tag != TypeHash)
            {
                throw new InvalidDataException(
                    $"Unexpected BinSafe tag 0x{tag:X2} at offset 0x{pos - 1:X} ({pos - 1}).");
            }

            if (pos + 5 > hashTableOffset)
                throw new InvalidDataException($"Truncated BinSafe entry header at offset 0x{pos - 1:X} ({pos - 1}).");

            // Skip 4-byte hash index
            pos += 4;
            byte valType = data[pos++];

            switch (valType)
            {
                case TypeString:
                {
                    string str = ReadLengthPrefixedString(data, ref pos, encoding);

                    if (section == ParseSection.Dialogs)
                    {
                        if (hasPendingDialogBool)
                        {
                            if (!string.IsNullOrWhiteSpace(str))
                                variables.Add(new GothicVariable(str, pendingDialogPos, pendingDialogVal));
                            hasPendingDialogBool = false;
                        }
                    }
                    else if (section == ParseSection.Missions)
                    {
                        if (chunkDepth == 0)
                        {
                            if (topicFieldsLeft == 3)
                            {
                                // Topic title
                                topicFieldsLeft--;
                            }
                            else if (remainingEntries > 0)
                            {
                                remainingEntries--;
                                if (remainingEntries == 0 && remainingTopics == 0)
                                    section = ParseSection.Variables;
                            }
                        }
                    }
                    else if (section == ParseSection.Variables)
                    {
                        if (chunkDepth == 0)
                        {
                            currentVarName = string.IsNullOrWhiteSpace(str) ? null : str;
                            expectedArrayElements = -1;
                            currentArrayIndex = 0;
                            isArray = false;
                        }
                    }
                    break;
                }

                case TypeBool:
                {
                    EnsureBytes(data, pos, 4, hashTableOffset, "Bool");
                    int valPos = pos;
                    int val = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
                    pos += 4;

                    if (section == ParseSection.HeaderScalars)
                        section = ParseSection.Dialogs;

                    if (section == ParseSection.Dialogs)
                    {
                        pendingDialogPos = valPos;
                        pendingDialogVal = val;
                        hasPendingDialogBool = true;
                    }
                    break;
                }

                case TypeInt:
                {
                    EnsureBytes(data, pos, 4, hashTableOffset, "Int");
                    int valPos = pos;
                    int val = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
                    pos += 4;

                    if (section == ParseSection.HeaderScalars)
                    {
                        // day, hour, min, numInfos
                    }
                    else if (section == ParseSection.Dialogs)
                    {
                        // Transition from Dialogs -> Missions when numTopics > 0
                        hasPendingDialogBool = false;
                        if (val > 0)
                        {
                            remainingTopics = val;
                            topicFieldsLeft = 3;
                            remainingEntries = 0;
                            section = ParseSection.Missions;
                        }
                    }
                    else if (section == ParseSection.Missions)
                    {
                        if (chunkDepth == 0)
                        {
                            if (topicFieldsLeft == 0 && remainingEntries == 0 && remainingTopics == 0)
                            {
                                // Already finished topics -> this int is numSymbols
                                section = ParseSection.Variables;
                                hasSymbolCount = true;
                            }
                            else if (remainingEntries == 0)
                            {
                                // numEntries for current topic
                                remainingEntries = val;
                                remainingTopics--;
                                topicFieldsLeft = remainingTopics > 0 ? 3 : 0;
                                if (remainingEntries == 0 && remainingTopics == 0)
                                    section = ParseSection.Variables;
                            }
                        }
                    }
                    else // ParseSection.Variables
                    {
                        if (chunkDepth == 0)
                        {
                            if (!hasSymbolCount && currentVarName == null)
                            {
                                // numSymbols
                                hasSymbolCount = true;
                            }
                            else if (currentVarName != null)
                            {
                                if (expectedArrayElements == -1)
                                {
                                    expectedArrayElements = val;
                                    isArray = val > 1;
                                    currentArrayIndex = 0;
                                    if (expectedArrayElements <= 0)
                                    {
                                        currentVarName = null;
                                        expectedArrayElements = -1;
                                    }
                                }
                                else
                                {
                                    variables.Add(isArray
                                        ? new GothicVariable(currentVarName, valPos, val, currentArrayIndex)
                                        : new GothicVariable(currentVarName, valPos, val));

                                    currentArrayIndex++;
                                    if (currentArrayIndex >= expectedArrayElements)
                                    {
                                        currentVarName = null;
                                        expectedArrayElements = -1;
                                    }
                                }
                            }
                        }
                    }
                    break;
                }

                case TypeEnum10:
                case TypeEnum11:
                {
                    EnsureBytes(data, pos, 4, hashTableOffset, "Enum");
                    pos += 4;
                    if (section == ParseSection.Missions && chunkDepth == 0 && topicFieldsLeft > 0)
                    {
                        topicFieldsLeft--;
                    }
                    break;
                }

                case TypeFloat:
                case TypeColor:
                    EnsureBytes(data, pos, 4, hashTableOffset, "Float/Color");
                    pos += 4;
                    break;

                case TypeByte:
                    EnsureBytes(data, pos, 1, hashTableOffset, "Byte");
                    pos += 1;
                    break;

                case TypeWord:
                    EnsureBytes(data, pos, 2, hashTableOffset, "Word");
                    pos += 2;
                    break;

                case TypeVec3:
                    EnsureBytes(data, pos, 12, hashTableOffset, "Vec3");
                    pos += 12;
                    break;

                case TypeRaw:
                case TypeRawFloat:
                    EnsureBytes(data, pos, 2, hashTableOffset, "RawLength");
                    ushort rawLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos, 2));
                    pos += 2;
                    EnsureBytes(data, pos, rawLen, hashTableOffset, "RawPayload");
                    pos += rawLen;
                    break;

                default:
                    throw new InvalidDataException(
                        $"Unknown BinSafe value type 0x{valType:X2} at offset 0x{pos - 1:X} ({pos - 1}).");
            }
        }

        if (variables.Count == 0)
            throw new InvalidDataException("SREmptyVariablesList: No variables found in SAVEDAT.SAV.");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0.88);

        variables.Sort(VariablesComparer.Instance);

        progress?.Report(1.0);
        return variables;
    }

    private static int ParseAsciiHeader(ReadOnlySpan<byte> data)
    {
        int scanLimit = Math.Min(data.Length, 4096);
        int endPos = -1;
        bool isBinSafe = false;

        int lineStart = 0;
        for (int i = 0; i < scanLimit; i++)
        {
            if (data[i] == (byte)'\n')
            {
                var line = data.Slice(lineStart, i - lineStart);
                if (line.Length > 0 && line[^1] == (byte)'\r')
                    line = line[..^1];

                if (line.SequenceEqual("BIN_SAFE"u8))
                    isBinSafe = true;

                if (line.SequenceEqual("END"u8))
                {
                    endPos = i + 1;
                    break;
                }

                lineStart = i + 1;
            }
        }

        if (endPos < 0 || !isBinSafe)
            throw new InvalidDataException("SRHeaderError: Missing ZenGin BIN_SAFE header or END marker.");

        return endPos;
    }

    private static string ReadLengthPrefixedString(ReadOnlySpan<byte> data, ref int pos, Encoding encoding)
    {
        if (pos + 2 > data.Length)
            throw new InvalidDataException($"Truncated string length at offset 0x{pos:X} ({pos}).");

        ushort len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos, 2));
        pos += 2;

        if (pos + len > data.Length)
            throw new InvalidDataException($"String length {len} exceeds file bounds at offset 0x{pos:X} ({pos}).");

        if (len == 0)
            return string.Empty;

        string result = encoding.GetString(data.Slice(pos, len));
        pos += len;
        return result;
    }

    private static void EnsureBytes(ReadOnlySpan<byte> data, int pos, int count, int limit, string typeName)
    {
        if (pos + count > limit || pos + count > data.Length)
            throw new InvalidDataException(
                $"Truncated {typeName} ({count} bytes) at offset 0x{pos:X} ({pos}).");
    }
}
