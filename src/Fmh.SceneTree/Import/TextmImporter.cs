using System.Buffers.Binary;

namespace Fmh.SceneTree;

/// <summary>
/// Liest Textm.sav und Textm.txt (Textmodus-Update III, 2.11.2001).
/// Layout: 16 Byte Kopf, dann Knoten in Preorder: u32 Id, u32 n, n mal u32 Zeilennummer, 44 Byte Parameter, u32 Kinderzahl.
/// </summary>
public static class TextmImporter
{
    public static readonly byte[] Magic = { 0xCE, 0x5D, 0x50, 0xCF, 0x87, 0x8F, 0xAA, 0x94 };

    public static SceneTreeAsset Load(string directory)
    {
        var sav = File.ReadAllBytes(Path.Combine(directory, "Textm.sav"));
        var lines = LoadLines(Path.Combine(directory, "Textm.txt"));
        return Parse(sav, lines);
    }

    public static SceneTreeAsset Parse(ReadOnlySpan<byte> sav, string[] lines)
    {
        if (sav.Length < 16 || !sav[..8].SequenceEqual(Magic))
            throw new InvalidDataException("Textm.sav hat nicht die erwartete Signatur.");
        var headerVersion = BinaryPrimitives.ReadUInt32LittleEndian(sav[8..]);
        var headerFlags = BinaryPrimitives.ReadUInt32LittleEndian(sav[12..]);
        var nodes = new List<SceneNode>();
        var offset = 16;
        while (offset < sav.Length)
        {
            if (offset + 8 > sav.Length) throw new InvalidDataException("Knotenheader abgeschnitten.");
            var id = BinaryPrimitives.ReadInt32LittleEndian(sav[offset..]);
            var count = BinaryPrimitives.ReadInt32LittleEndian(sav[(offset + 4)..]);
            if (count < 0 || count > 32) throw new InvalidDataException($"Ungültige Variantenzahl {count} bei Knoten {nodes.Count}.");
            var refsAt = offset + 8;
            var blockAt = refsAt + 4 * count;
            if (blockAt + 48 > sav.Length) throw new InvalidDataException($"Knoten {nodes.Count} ragt über das Dateiende.");
            var refs = new int[count];
            for (var i = 0; i < count; i++)
                refs[i] = BinaryPrimitives.ReadInt32LittleEndian(sav[(refsAt + 4 * i)..]);
            var parameters = sav.Slice(blockAt, SceneNode.ParameterLength).ToArray();
            var children = BinaryPrimitives.ReadInt32LittleEndian(sav[(blockAt + 44)..]);
            if (children < 0 || children > 64) throw new InvalidDataException($"Ungültige Kinderzahl {children}.");
            nodes.Add(new SceneNode(nodes.Count, id, refs, parameters, children));
            offset = blockAt + 48;
        }
        if (offset != sav.Length) throw new InvalidDataException("Textm.sav endet nicht an einer Knotengrenze.");
        var magic = Convert.ToHexString(Magic);
        return SceneTreeAsset.Build(magic, headerVersion, headerFlags, lines, nodes);
    }

    public static string[] LoadLines(string path)
    {
        var map = new Dictionary<int, string>();
        var max = 0;
        foreach (var raw in SplitLines(File.ReadAllBytes(path)))
        {
            if (raw.Length == 0) continue;
            var text = Cp1252.GetString(raw);
            var space = text.IndexOf(' ');
            if (space <= 0 || !int.TryParse(text.AsSpan(0, space), out var number))
                throw new InvalidDataException("Textzeile ohne Nummer.");
            map[number] = text[(space + 1)..];
            if (number > max) max = number;
        }
        var lines = new string[max + 1];
        foreach (var (number, text) in map) lines[number] = text;
        return lines;
    }

    private static List<byte[]> SplitLines(byte[] file)
    {
        var lines = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < file.Length; i++)
        {
            if (file[i] != (byte)'\n') continue;
            var end = i;
            if (end > start && file[end - 1] == (byte)'\r') end--;
            lines.Add(file[start..end]);
            start = i + 1;
        }
        if (start < file.Length) lines.Add(file[start..]);
        return lines;
    }
}
