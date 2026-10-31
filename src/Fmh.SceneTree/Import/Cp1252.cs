namespace Fmh.SceneTree;

/// <summary>
/// CP1252 ohne Codepage-Paket. Die Laufzeit ist mit InvariantGlobalization gebaut, dort fehlt das Encoding.
/// Bytes 0x00–0x7F und 0xA0–0xFF entsprechen Unicode; 0x80–0x9F sind die Windows-Sonderzeichen.
/// </summary>
static class Cp1252
{
    private static readonly char[] High =
    {
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    };

    public static string GetString(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i] = b < 0x80 || b >= 0xA0 ? (char)b : High[b - 0x80];
        }
        return new string(chars);
    }
}
