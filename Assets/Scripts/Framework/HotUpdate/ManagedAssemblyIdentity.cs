using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Client.Framework.HotUpdate
{
    // Bounded ECMA-335 identity preflight only. Never executes IL or loads an assembly.
    internal sealed class ManagedAssemblyIdentity
    {
        internal string Name { get; private set; }
        internal IReadOnlyList<string> References { get; private set; }
        internal static ManagedAssemblyIdentity Read(byte[] bytes)
        {
            try { return new Reader(bytes).Read(); }
            catch (InvalidDataException) { throw; }
            catch (Exception e) when (e is OverflowException || e is ArgumentException || e is IndexOutOfRangeException || e is DecoderFallbackException)
            { throw new InvalidDataException("Malformed managed DLL metadata.", e); }
        }
        private sealed class Reader
        {
            private readonly byte[] bytes;
            private readonly uint[] rows = new uint[64];
            private readonly long[] offsets = new long[64];
            private int stringsWidth, guidWidth, blobWidth;
            private long stringStart, stringLength;
            internal Reader(byte[] bytes) { this.bytes = bytes ?? throw Bad(); }
            private static InvalidDataException Bad() => new InvalidDataException("Malformed or unsupported PE/CLI assembly identity metadata.");
            private void Range(long offset, long size, long begin = 0, long end = -1)
            { if (end < 0) end = bytes.LongLength; if (offset < begin || size < 0 || offset > end || size > end - offset) throw Bad(); }
            private ushort U16(long p) { Range(p, 2); return (ushort)(bytes[(int)p] | bytes[(int)p + 1] << 8); }
            private uint U32(long p) { Range(p, 4); return (uint)(bytes[(int)p] | bytes[(int)p + 1] << 8 | bytes[(int)p + 2] << 16 | bytes[(int)p + 3] << 24); }
            private ulong U64(long p) => U32(p) | (ulong)U32(p + 4) << 32;
            private uint Index(long p, int width) => width == 2 ? U16(p) : U32(p);
            private static long Align4(long p) => checked((p + 3) & ~3L);
            internal ManagedAssemblyIdentity Read()
            {
                Range(0, 64);
                if (U16(0) != 0x5a4d) throw Bad();
                long pe = U32(0x3c); Range(pe, 24);
                if (U32(pe) != 0x00004550) throw Bad();
                int sections = U16(pe + 6), optionalSize = U16(pe + 20);
                if (sections < 1 || sections > 96) throw Bad();
                long optional = pe + 24; Range(optional, optionalSize);
                ushort magic = U16(optional);
                int directoryStart = magic == 0x10b ? 96 : magic == 0x20b ? 112 : throw Bad();
                if (optionalSize < directoryStart + 15 * 8 || U32(optional + directoryStart - 4) < 15) throw Bad();
                long sectionStart = optional + optionalSize; Range(sectionStart, sections * 40L);
                Func<uint, uint, long> rva = (address, length) =>
                {
                    if (address == 0 || length == 0) throw Bad();
                    long found = -1;
                    for (int i = 0; i < sections; i++)
                    {
                        long section = sectionStart + i * 40L;
                        uint virtualAddress = U32(section + 12), rawSize = U32(section + 16), raw = U32(section + 20), virtualSize = U32(section + 8);
                        long relative = (long)address - virtualAddress;
                        if (relative < 0 || relative >= Math.Max((long)virtualSize, rawSize)) continue;
                        if (relative > rawSize || length > rawSize - relative || found >= 0) throw Bad();
                        found = (long)raw + relative; Range(found, length);
                    }
                    if (found < 0) throw Bad();
                    return found;
                };
                uint cliRva = U32(optional + directoryStart + 14 * 8), cliSize = U32(optional + directoryStart + 14 * 8 + 4);
                long cli = rva(cliRva, cliSize);
                if (cliSize < 72 || U32(cli) < 72 || U32(cli) > cliSize) throw Bad();
                uint metadataSize = U32(cli + 12);
                long metadata = rva(U32(cli + 8), metadataSize), metadataEnd = checked(metadata + metadataSize);
                Range(metadata, 20);
                if (U32(metadata) != 0x424a5342) throw Bad();
                uint versionSize = U32(metadata + 12);
                if (versionSize == 0 || versionSize > 256) throw Bad();
                long cursor = Align4(metadata + 16 + versionSize); Range(cursor, 4, metadata, metadataEnd);
                int streamCount = U16(cursor + 2); cursor += 4;
                if (streamCount < 1 || streamCount > 16) throw Bad();
                var streams = new Dictionary<string, Tuple<long, long>>(StringComparer.Ordinal);
                for (int i = 0; i < streamCount; i++)
                {
                    Range(cursor, 9, metadata, metadataEnd);
                    uint start = U32(cursor), length = U32(cursor + 4); cursor += 8;
                    long nameStart = cursor;
                    while (cursor < metadataEnd && cursor - nameStart < 32 && bytes[(int)cursor] != 0) { if (bytes[(int)cursor] > 127) throw Bad(); cursor++; }
                    if (cursor >= metadataEnd || cursor - nameStart == 32) throw Bad();
                    string name = Encoding.ASCII.GetString(bytes, checked((int)nameStart), checked((int)(cursor - nameStart)));
                    cursor = Align4(cursor + 1);
                    long streamStart = checked(metadata + start); Range(streamStart, length, metadata, metadataEnd);
                    if (streams.ContainsKey(name)) throw Bad();
                    streams.Add(name, Tuple.Create(streamStart, (long)length));
                }
                var sorted = streams.Values.OrderBy(s => s.Item1).ToArray();
                long previousEnd = cursor;
                foreach (var stream in sorted) { if (stream.Item1 < previousEnd) throw Bad(); previousEnd = checked(stream.Item1 + stream.Item2); }
                if (!streams.TryGetValue("#Strings", out var strings) || strings.Item2 == 0) throw Bad();
                stringStart = strings.Item1; stringLength = strings.Item2;
                bool compressed = streams.TryGetValue("#~", out var tables), uncompressed = streams.TryGetValue("#-", out var tablesUncompressed);
                if (compressed == uncompressed) throw Bad();
                if (uncompressed) tables = tablesUncompressed;
                long tableStart = tables.Item1, tableEnd = checked(tableStart + tables.Item2); Range(tableStart, 24, tableStart, tableEnd);
                byte heapSizes = bytes[(int)tableStart + 6];
                if ((heapSizes & ~7) != 0) throw Bad();
                stringsWidth = (heapSizes & 1) != 0 ? 4 : 2; guidWidth = (heapSizes & 2) != 0 ? 4 : 2; blobWidth = (heapSizes & 4) != 0 ? 4 : 2;
                ulong valid = U64(tableStart + 8);
                if ((valid >> 45) != 0) throw Bad();
                cursor = tableStart + 24;
                for (int table = 0; table < 45; table++) if ((valid & (1UL << table)) != 0) { Range(cursor, 4, tableStart, tableEnd); rows[table] = U32(cursor); cursor += 4; }
                for (int table = 0; table < 45; table++)
                {
                    offsets[table] = cursor;
                    long size = checked((long)rows[table] * RowSize(table)); Range(cursor, size, tableStart, tableEnd); cursor += size;
                }
                if (rows[0] != 1 || rows[32] != 1 || rows[35] > 1024) throw Bad();
                string assemblyName = String(Index(offsets[32] + 16 + blobWidth, stringsWidth));
                var references = new List<string>();
                for (uint row = 0; row < rows[35]; row++) references.Add(String(Index(offsets[35] + row * RowSize(35) + 12 + blobWidth, stringsWidth)));
                if (references.Distinct(StringComparer.Ordinal).Count() != references.Count) throw Bad();
                return new ManagedAssemblyIdentity { Name = assemblyName, References = references.AsReadOnly() };
            }
            private string String(uint index)
            {
                if (index == 0 || index >= stringLength) throw Bad();
                long start = stringStart + index, end = start;
                while (end < stringStart + stringLength && end - start <= 128 && bytes[(int)end] != 0) end++;
                if (end == stringStart + stringLength || end - start > 128) throw Bad();
                string value = new UTF8Encoding(false, true).GetString(bytes, (int)start, (int)(end - start));
                if (string.IsNullOrEmpty(value) || value.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.'))) throw Bad();
                return value;
            }
            private int Table(int table) => rows[table] < 65536 ? 2 : 4;
            private int Coded(int bits, params int[] tables) => tables.Any(t => rows[t] >= (1U << (16 - bits))) ? 4 : 2;
            private int TypeDefOrRef => Coded(2, 2, 1, 27);
            private int MethodDefOrRef => Coded(1, 6, 10);
            private int Implementation => Coded(2, 38, 35, 39);
            private int RowSize(int table)
            {
                switch (table)
                {
                    case 0: return 2 + stringsWidth + guidWidth * 3;
                    case 1: return Coded(2, 0, 26, 35, 1) + stringsWidth * 2;
                    case 2: return 4 + stringsWidth * 2 + TypeDefOrRef + Table(4) + Table(6);
                    case 3: return Table(4);
                    case 4: return 2 + stringsWidth + blobWidth;
                    case 5: return Table(6);
                    case 6: return 8 + stringsWidth + blobWidth + Table(8);
                    case 7: return Table(8);
                    case 8: return 4 + stringsWidth;
                    case 9: return Table(2) + TypeDefOrRef;
                    case 10: return Coded(3, 2, 1, 26, 6, 27) + stringsWidth + blobWidth;
                    case 11: return 2 + Coded(2, 4, 8, 23) + blobWidth;
                    case 12: return Coded(5, 6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43) + Coded(3, 6, 10) + blobWidth;
                    case 13: return Coded(1, 4, 8) + blobWidth;
                    case 14: return 2 + Coded(2, 2, 6, 32) + blobWidth;
                    case 15: return 6 + Table(2);
                    case 16: return 4 + Table(4);
                    case 17: return blobWidth;
                    case 18: return Table(2) + Table(20);
                    case 19: return Table(20);
                    case 20: return 2 + stringsWidth + TypeDefOrRef;
                    case 21: return Table(2) + Table(23);
                    case 22: return Table(23);
                    case 23: return 2 + stringsWidth + blobWidth;
                    case 24: return 2 + Table(6) + Coded(1, 20, 23);
                    case 25: return Table(2) + MethodDefOrRef * 2;
                    case 26: return stringsWidth;
                    case 27: return blobWidth;
                    case 28: return 2 + Coded(1, 4, 6) + stringsWidth + Table(26);
                    case 29: return 4 + Table(4);
                    case 30: return 8;
                    case 31: return 4;
                    case 32: return 16 + blobWidth + stringsWidth * 2;
                    case 33: return 4;
                    case 34: return 12;
                    case 35: return 12 + blobWidth * 2 + stringsWidth * 2;
                    case 36: return 4 + Table(35);
                    case 37: return 12 + Table(35);
                    case 38: return 4 + stringsWidth + blobWidth;
                    case 39: return 8 + stringsWidth * 2 + Implementation;
                    case 40: return 8 + stringsWidth + Implementation;
                    case 41: return Table(2) * 2;
                    case 42: return 4 + Coded(1, 2, 6) + stringsWidth;
                    case 43: return MethodDefOrRef + blobWidth;
                    case 44: return Table(42) + TypeDefOrRef;
                    default: throw Bad();
                }
            }
        }
    }
}
