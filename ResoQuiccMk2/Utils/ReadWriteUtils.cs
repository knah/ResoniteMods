namespace ResoQuiccMk2.Utils;

public static class ReadWriteUtils
{
    public static int FormatUleb128(long value, Span<byte> target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        
        var position = 0;
        do
        {
            var b = (byte) (value & 0x7Fu);
            value >>= 7;
            if (value != 0) b |= 0x80;
            target[position++] = b;
        } while (value != 0);

        return position;
    }

    public static long ReadUleb128(ReadOnlySpan<byte> buffer, ref int offset)
    {
        long result = 0;
        while (true)
        {
            var b = (long) buffer[offset];
            result |= (b & 0x7Fu) << (offset * 7);
            offset++;
            if ((b & 0x80) == 0)
                return result;
        }
    }
}