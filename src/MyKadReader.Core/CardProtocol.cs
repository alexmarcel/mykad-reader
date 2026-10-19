namespace MyKadReader.Core;

public interface ICardChannel : IDisposable
{
    byte[] Transmit(byte[] command);
}

public sealed class CardProtocol(ICardChannel channel, CancellationToken cancellation)
{
    public Identity Read()
    {
        Command(Convert.FromHexString("00A404000AA0000000744A504E0010"), "Select MyKad JPN application");
        var identity = MyKadDecoder.Personal(ReadFile(1, 3, 0x1A5));
        try { identity = identity with { Address = MyKadDecoder.Address(ReadFile(4, 3, 0x94)) }; }
        catch (InvalidDataException) { /* Optional data unavailable; never suppress transport failures. */ }
        catch (CardCommandException) { }
        try { identity = identity with { Photo = MyKadDecoder.Photo(ReadFile(2, 3, 0xFA0)) }; }
        catch (InvalidDataException) { }
        catch (CardCommandException) { }
        cancellation.ThrowIfCancellationRequested();
        return identity;
    }

    public byte[] ReadFile(byte file, int offset, int length)
    {
        using var result = new MemoryStream();
        while (result.Length < length)
        {
            int count = Math.Min(240, length - (int)result.Length);
            int position = offset + (int)result.Length;
            // JPN has command-specific ready statuses, distinct from ISO 9000 success.
            Command([0xC8, 0x32, 0, 0, 5, 8, 0, 0, (byte)count, 0], "Set read length", (hi, lo) => hi == 0x91 && lo == 8);
            Command([0xCC, 0, 0, 0, 8, file, 0, 1, 0, (byte)position, (byte)(position >> 8), (byte)count, 0], "Select card data", (hi, _) => hi == 0x94);
            var data = Command([0xCC, 6, 0, 0, (byte)count], "Read card data", (hi, _) => hi == 0x94);
            if (data.Length != count) throw new InvalidDataException("The card returned incomplete data. This layout may be unsupported.");
            result.Write(data);
        }
        return result.ToArray();
    }

    private byte[] Command(byte[] command, string stage, Func<byte, byte, bool>? readyStatus = null)
    {
        using var data = new MemoryStream();
        for (int attempt = 0; attempt < 8; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var response = channel.Transmit(command);
            cancellation.ThrowIfCancellationRequested();
            if (response.Length < 2) throw new InvalidDataException("The card returned a malformed response.");
            byte sw1 = response[^2], sw2 = response[^1];
            data.Write(response, 0, response.Length - 2);
            if ((sw1 == 0x90 && sw2 == 0) || readyStatus?.Invoke(sw1, sw2) == true) return data.ToArray();
            if (sw1 == 0x61) { command = [0, 0xC0, 0, 0, sw2]; continue; }
            throw new CardCommandException($"{stage} failed (card status {sw1:X2}{sw2:X2}). This card may require a different protocol or SDK.");
        }
        throw new CardCommandException("The card exceeded the supported response sequence.");
    }
}

public sealed class CardCommandException(string message) : Exception(message);
