using System.Globalization;
using System.Text;

namespace MyKadReader.Core;

public static class MyKadDecoder
{
    // Community JPN layout, offsets relative to file offset 3. Hardware verification required.
    public static Identity Personal(byte[] data)
    {
        if (data.Length != 0x1A5)
            throw new InvalidDataException("Unsupported or truncated MyKad personal-data layout.");
        var number = Identity.NormalizeNumber(Text(data, 0x10E, 13) ?? "");
        var name = Text(data, 0, 150);
        var gender = data[0x11B] switch { (byte)'L' => "Male", (byte)'P' => "Female", _ => null };
        return new(number, name, Date(data.AsSpan(0x124, 4)), gender, null, null);
    }

    public static string? Address(byte[] data)
    {
        if (data.Length != 0x94) throw new InvalidDataException("Unsupported or truncated address layout.");
        var postcode = Convert.ToHexString(data.AsSpan(0x5A, 3))[..5];
        if (postcode.Any(c => c is < '0' or > '9')) postcode = "";
        var parts = new[] { Text(data, 0, 30), Text(data, 30, 30), Text(data, 60, 30),
            string.Join(" ", new[] { postcode, Text(data, 0x5D, 25) }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Text(data, 0x76, 30) }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    public static byte[] Photo(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidDataException("The chip portrait is not a supported JPEG image.");
        for (int i = 2; i < data.Length - 1; i++)
            if (data[i] == 0xFF && data[i + 1] == 0xD9) return data[..(i + 2)];
        throw new InvalidDataException("The chip portrait is incomplete.");
    }

    private static string? Text(byte[] data, int offset, int count)
    {
        var bytes = data.AsSpan(offset, count);
        foreach (byte b in bytes)
            if (b != 0 && b != 32 && (b < 32 || b > 126))
                throw new InvalidDataException("Unsupported text encoding in MyKad data.");
        var text = Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
        return text.Length == 0 ? null : text;
    }

    private static string? Date(ReadOnlySpan<byte> data)
    {
        var text = Convert.ToHexString(data);
        return DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
}
