using System.Buffers.Binary;
using System.IO.Compression;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Rimtera's compact cell encoding for saves: row-major runs of identical cells, each run written
/// as a 7-bit varint length followed by the cell, then Deflate and base64. A version byte comes first.
/// Version 2 (current) writes the packed 8-byte cell: tile u16, wall u16, liquid units u8 (0–240),
/// liquid kind + flags u8, damage u8, frame mask u8 (little-endian). Version 1 (older saves) wrote
/// 15 bytes: tile u16, wall u16, liquid depth f32 bits, liquid kind u8, damage u8, frame mask u8,
/// frame x u16, frame y u16; it still decodes (depth rounds to liquid units, frame x/y come from the mask).
/// </summary>
public static class CellCodec
{
    private const byte Version = 2;
    private const byte LegacyVersion = 1;
    private const int CellBytes = 8;
    private const int LegacyCellBytes = 15;

    public static string Encode(ReadOnlySpan<WorldCell> cells)
    {
        using var raw = EncodeToStream(cells);
        return Convert.ToBase64String(raw.GetBuffer(), 0, (int)raw.Length);
    }

    /// <summary>The deflated cell stream as raw bytes (binary sidecar saves).</summary>
    public static byte[] EncodeBytes(ReadOnlySpan<WorldCell> cells)
    {
        using var raw = EncodeToStream(cells);
        return raw.ToArray();
    }

    /// <summary>
    /// The run stream before compression (PU-12): a quick pass a save can take on the game thread, leaving
    /// <see cref="DeflateRuns"/> (the slow part) to a worker. <c>DeflateRuns(EncodeRuns(cells))</c> decodes as <see cref="EncodeBytes"/>.
    /// </summary>
    public static byte[] EncodeRuns(ReadOnlySpan<WorldCell> cells)
    {
        using var runs = new MemoryStream();
        WriteRuns(runs, cells);
        return runs.ToArray();
    }

    /// <summary>Compresses a run stream from <see cref="EncodeRuns"/> into the sidecar format.</summary>
    public static byte[] DeflateRuns(byte[] runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        using var raw = new MemoryStream();
        using (var deflate = new DeflateStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(runs, 0, runs.Length);
        return raw.ToArray();
    }

    private static MemoryStream EncodeToStream(ReadOnlySpan<WorldCell> cells)
    {
        var raw = new MemoryStream();
        using (var deflate = new DeflateStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            WriteRuns(deflate, cells);
        return raw;
    }

    private static void WriteRuns(Stream deflate, ReadOnlySpan<WorldCell> cells)
    {
        {
            deflate.WriteByte(Version);
            // Runs are buffered and written in blocks: far fewer stream calls than one per run.
            var block = new byte[64 * 1024];
            var used = 0;
            var index = 0;
            while (index < cells.Length)
            {
                var cell = cells[index];
                var packed = cell.Packed;
                var run = 1;
                while (index + run < cells.Length && cells[index + run].Packed == packed)
                    run++;
                if (used + 5 + CellBytes > block.Length)
                {
                    deflate.Write(block, 0, used);
                    used = 0;
                }
                used += WriteVarint(block.AsSpan(used), (uint)run);
                WriteCell(block.AsSpan(used), cell);
                used += CellBytes;
                index += run;
            }
            deflate.Write(block, 0, used);
        }
    }

    public static WorldCell[] Decode(string encoded, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (expectedCount < 0)
            throw new InvalidDataException("Invalid cell count.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The compressed cell data is not valid base64.", ex);
        }

        return DecodeBytes(bytes, expectedCount);
    }

    public static WorldCell[] DecodeBytes(byte[] bytes, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (expectedCount < 0)
            throw new InvalidDataException("Invalid cell count.");
        var cells = new WorldCell[expectedCount];
        using var deflate = new BufferedStream(new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress), 64 * 1024);
        var version = deflate.ReadByte();
        if (version is not (Version or LegacyVersion))
            throw new InvalidDataException($"Unsupported compressed cell version {version}.");
        var legacy = version == LegacyVersion;
        Span<byte> cellBuffer = stackalloc byte[legacy ? LegacyCellBytes : CellBytes];
        var index = 0;
        while (index < expectedCount)
        {
            var run = ReadVarint(deflate);
            if (run == 0 || run > (uint)(expectedCount - index))
                throw new InvalidDataException("The compressed cell data has an invalid run.");
            deflate.ReadExactly(cellBuffer);
            var cell = legacy ? ReadLegacyCell(cellBuffer) : ReadCell(cellBuffer);
            cells.AsSpan(index, (int)run).Fill(cell);
            index += (int)run;
        }
        if (deflate.ReadByte() != -1)
            throw new InvalidDataException("The compressed cell data has trailing bytes.");
        return cells;
    }

    private static void WriteCell(Span<byte> span, in WorldCell cell)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)cell.TileId);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], cell.WallId);
        span[4] = cell.LiquidUnits;
        span[5] = (byte)((byte)cell.LiquidKind | (cell.Flags << 3));
        span[6] = cell.Damage;
        span[7] = cell.FrameMask;
    }

    private static WorldCell ReadCell(ReadOnlySpan<byte> span)
    {
        if (span[4] > WorldCell.LiquidUnitsMax)
            throw new InvalidDataException("The compressed cell data has an invalid liquid amount.");
        return new WorldCell
        {
            TileId = (TileId)BinaryPrimitives.ReadUInt16LittleEndian(span),
            WallId = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]),
            LiquidUnits = span[4],
            LiquidKind = (LiquidKind)(span[5] & 0x07),
            Flags = (byte)(span[5] >> 3),
            Damage = span[6],
            FrameMask = span[7]
        };
    }

    /// <summary>Version 1 cell: float depth becomes liquid units; empty cells lose their kind and vice versa.</summary>
    private static WorldCell ReadLegacyCell(ReadOnlySpan<byte> span)
    {
        var depth = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span[4..]));
        if (!float.IsFinite(depth) || depth < 0f || depth > TileGrid.MaximumLiquidDepth)
            throw new InvalidDataException("The compressed cell data has an invalid liquid depth.");
        var kindByte = span[8];
        if (kindByte > 7)
            throw new InvalidDataException("The compressed cell data has an invalid liquid kind.");
        var cell = new WorldCell
        {
            TileId = (TileId)BinaryPrimitives.ReadUInt16LittleEndian(span),
            WallId = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]),
            LiquidDepth = depth,
            LiquidKind = (LiquidKind)kindByte,
            Damage = span[9],
            FrameMask = span[10]
        };
        NormalizeLiquid(ref cell);
        return cell;
    }

    /// <summary>A cell with no liquid units has no kind; a cell with units but no kind drops them.</summary>
    public static void NormalizeLiquid(ref WorldCell cell)
    {
        if (cell.LiquidUnits == 0)
            cell.LiquidKind = LiquidKind.None;
        else if (cell.LiquidKind == LiquidKind.None)
            cell.LiquidUnits = 0;
    }

    private static int WriteVarint(Span<byte> span, uint value)
    {
        var length = 0;
        while (value >= 0x80)
        {
            span[length++] = (byte)(value | 0x80);
            value >>= 7;
        }
        span[length++] = (byte)value;
        return length;
    }

    private static uint ReadVarint(Stream stream)
    {
        uint value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var next = stream.ReadByte();
            if (next < 0)
                throw new InvalidDataException("The compressed cell data ended early.");
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
                return value;
        }
        throw new InvalidDataException("The compressed cell data has an invalid length.");
    }
}
