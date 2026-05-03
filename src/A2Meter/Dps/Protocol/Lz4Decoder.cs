using System;

namespace A2Meter.Dps.Protocol;

/// LZ4 block format decoder. Single in-place pass over a literal/match token
/// stream. Returns -1 on any out-of-bounds condition (matches the original's
/// fail-closed contract — callers retry as raw bytes when this returns -1).
internal static class Lz4Decoder
{
    public static int Decompress(byte[] src, int srcOffset, int srcLen, byte[] dst, int dstOffset, int dstLen)
    {
        int srcEnd = srcOffset + srcLen;
        int dstEnd = dstOffset + dstLen;
        int s = srcOffset;
        int d = dstOffset;

        while (s < srcEnd)
        {
            byte token = src[s++];
            int litLen   = token >> 4;
            int matchLen = token & 0xF;

            if (litLen == 15)
            {
                while (s < srcEnd)
                {
                    byte b = src[s++];
                    litLen += b;
                    if (b != 0xFF) break;
                }
            }

            if (d + litLen > dstEnd || s + litLen > srcEnd) return -1;
            Buffer.BlockCopy(src, s, dst, d, litLen);
            s += litLen;
            d += litLen;

            if (s >= srcEnd) break;
            if (s + 2 > srcEnd) return -1;

            int offset = src[s] | (src[s + 1] << 8);
            s += 2;
            if (offset == 0) return -1;

            matchLen += 4;
            if ((token & 0xF) == 15)
            {
                while (s < srcEnd)
                {
                    byte b = src[s++];
                    matchLen += b;
                    if (b != 0xFF) break;
                }
            }

            int matchPos = d - offset;
            if (matchPos < dstOffset)        return -1;
            if (d + matchLen > dstEnd)       return -1;

            // Byte-by-byte copy on purpose: matches may overlap (RLE-style) and
            // BlockCopy would not reproduce that behavior.
            for (int i = 0; i < matchLen; i++)
                dst[d++] = dst[matchPos + i];
        }
        return d - dstOffset;
    }
}
