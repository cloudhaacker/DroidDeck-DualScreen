using System;
using System.IO;

namespace DualScreenBridge
{
    // Baseline JPEG (4:4:4, standard tables) so the picture can be encoded off the main thread; UnityEngine's encoder is main-thread only.
    internal sealed class JpegEncoder
    {
        private static readonly byte[] LumaQuant = { 6, 4, 5, 6, 5, 4, 6, 6, 5, 6, 7, 7, 6, 8, 10, 16, 10, 10, 9, 9, 10, 20, 14, 15, 12, 16, 23, 20, 24, 24, 23, 20, 22, 22, 26, 29, 37, 31, 26, 27, 35, 28, 22, 22, 32, 44, 32, 35, 38, 39, 41, 42, 41, 25, 31, 45, 48, 45, 40, 48, 37, 40, 41, 40 };
        private static readonly byte[] ChromaQuant = { 7, 7, 7, 10, 8, 10, 19, 10, 10, 19, 40, 26, 22, 26, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40 };

        private static readonly byte[] DcBits = { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly byte[] DcVals = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        private static readonly byte[] DcChromaBits = { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
        private static readonly byte[] AcBits = { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 125 };
        private static readonly byte[] AcVals = { 1, 2, 3, 0, 4, 17, 5, 18, 33, 49, 65, 6, 19, 81, 97, 7, 34, 113, 20, 50, 129, 145, 161, 8, 35, 66, 177, 193, 21, 82, 209, 240, 36, 51, 98, 114, 130, 9, 10, 22, 23, 24, 25, 26, 37, 38, 39, 40, 41, 42, 52, 53, 54, 55, 56, 57, 58, 67, 68, 69, 70, 71, 72, 73, 74, 83, 84, 85, 86, 87, 88, 89, 90, 99, 100, 101, 102, 103, 104, 105, 106, 115, 116, 117, 118, 119, 120, 121, 122, 131, 132, 133, 134, 135, 136, 137, 138, 146, 147, 148, 149, 150, 151, 152, 153, 154, 162, 163, 164, 165, 166, 167, 168, 169, 170, 178, 179, 180, 181, 182, 183, 184, 185, 186, 194, 195, 196, 197, 198, 199, 200, 201, 202, 210, 211, 212, 213, 214, 215, 216, 217, 218, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250 };
        private static readonly byte[] AcChromaBits = { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 119 };
        private static readonly byte[] AcChromaVals = { 0, 1, 2, 3, 17, 4, 5, 33, 49, 6, 18, 65, 81, 7, 97, 113, 19, 34, 50, 129, 8, 20, 66, 145, 161, 177, 193, 9, 35, 51, 82, 240, 21, 98, 114, 209, 10, 22, 36, 52, 225, 37, 241, 23, 24, 25, 26, 38, 39, 40, 41, 42, 53, 54, 55, 56, 57, 58, 67, 68, 69, 70, 71, 72, 73, 74, 83, 84, 85, 86, 87, 88, 89, 90, 99, 100, 101, 102, 103, 104, 105, 106, 115, 116, 117, 118, 119, 120, 121, 122, 130, 131, 132, 133, 134, 135, 136, 137, 138, 146, 147, 148, 149, 150, 151, 152, 153, 154, 162, 163, 164, 165, 166, 167, 168, 169, 170, 178, 179, 180, 181, 182, 183, 184, 185, 186, 194, 195, 196, 197, 198, 199, 200, 201, 202, 210, 211, 212, 213, 214, 215, 216, 217, 218, 226, 227, 228, 229, 230, 231, 232, 233, 234, 242, 243, 244, 245, 246, 247, 248, 249, 250 };

        private static readonly int[] Zigzag =
        {
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        };

        private static readonly float[,] Cos = BuildCos();

        private readonly int[] dcLumaCode = new int[12], dcLumaLen = new int[12], dcChromaCode = new int[12], dcChromaLen = new int[12];
        private readonly int[] acLumaCode = new int[256], acLumaLen = new int[256], acChromaCode = new int[256], acChromaLen = new int[256];
        private readonly float[] lumaScale = new float[64], chromaScale = new float[64];

        private MemoryStream output;
        private int bitBuffer, bitCount;

        public JpegEncoder()
        {
            BuildCodes(DcBits, DcVals, dcLumaCode, dcLumaLen);
            BuildCodes(DcChromaBits, DcVals, dcChromaCode, dcChromaLen);
            BuildCodes(AcBits, AcVals, acLumaCode, acLumaLen);
            BuildCodes(AcChromaBits, AcChromaVals, acChromaCode, acChromaLen);
            for (int k = 0; k < 64; k++)
            {
                lumaScale[Zigzag[k]] = 1f / LumaQuant[k];
                chromaScale[Zigzag[k]] = 1f / ChromaQuant[k];
            }
        }

        private static float[,] BuildCos()
        {
            float[,] t = new float[8, 8];
            for (int u = 0; u < 8; u++)
            {
                for (int x = 0; x < 8; x++)
                {
                    double c = u == 0 ? Math.Sqrt(0.125) : 0.5;
                    t[u, x] = (float)(c * Math.Cos((2 * x + 1) * u * Math.PI / 16));
                }
            }
            return t;
        }

        private static void BuildCodes(byte[] bits, byte[] vals, int[] code, int[] len)
        {
            int c = 0, k = 0;
            for (int l = 1; l <= 16; l++)
            {
                for (int i = 0; i < bits[l - 1]; i++)
                {
                    code[vals[k]] = c;
                    len[vals[k]] = l;
                    k++;
                    c++;
                }
                c <<= 1;
            }
        }

        // rgb is Unity's RGB24 raw texture data: bottom row first.
        public byte[] Encode(byte[] rgb, int width, int height)
        {
            output = new MemoryStream(width * height / 8);
            bitBuffer = 0;
            bitCount = 0;
            WriteHeader(width, height);

            float[] y = new float[64], cb = new float[64], cr = new float[64], tmp = new float[64];
            int dcY = 0, dcCb = 0, dcCr = 0;
            for (int by = 0; by < height; by += 8)
            {
                for (int bx = 0; bx < width; bx += 8)
                {
                    for (int j = 0; j < 8; j++)
                    {
                        int row = Math.Min(by + j, height - 1);
                        int src = ((height - 1 - row) * width + bx) * 3;
                        for (int i = 0; i < 8; i++)
                        {
                            int px = Math.Min(bx + i, width - 1) - bx;
                            int p = src + px * 3;
                            float r = rgb[p], g = rgb[p + 1], b = rgb[p + 2];
                            int n = j * 8 + i;
                            y[n] = 0.299f * r + 0.587f * g + 0.114f * b - 128f;
                            cb[n] = -0.168736f * r - 0.331264f * g + 0.5f * b;
                            cr[n] = 0.5f * r - 0.418688f * g - 0.081312f * b;
                        }
                    }
                    dcY = Block(y, tmp, lumaScale, dcY, dcLumaCode, dcLumaLen, acLumaCode, acLumaLen);
                    dcCb = Block(cb, tmp, chromaScale, dcCb, dcChromaCode, dcChromaLen, acChromaCode, acChromaLen);
                    dcCr = Block(cr, tmp, chromaScale, dcCr, dcChromaCode, dcChromaLen, acChromaCode, acChromaLen);
                }
            }
            Flush();
            output.WriteByte(0xFF);
            output.WriteByte(0xD9);
            return output.ToArray();
        }

        private int Block(float[] data, float[] tmp, float[] scale, int prevDc, int[] dcCode, int[] dcLen, int[] acCode, int[] acLen)
        {
            for (int u = 0; u < 8; u++)
            {
                for (int j = 0; j < 8; j++)
                {
                    float s = 0;
                    for (int x = 0; x < 8; x++) s += data[j * 8 + x] * Cos[u, x];
                    tmp[j * 8 + u] = s;
                }
            }
            int[] q = new int[64];
            for (int v = 0; v < 8; v++)
            {
                for (int u = 0; u < 8; u++)
                {
                    float s = 0;
                    for (int j = 0; j < 8; j++) s += tmp[j * 8 + u] * Cos[v, j];
                    int n = v * 8 + u;
                    q[n] = (int)Math.Round(s * scale[n]);
                }
            }

            int diff = q[0] - prevDc;
            int size = Size(diff);
            Put(dcCode[size], dcLen[size]);
            if (size > 0) Put(diff < 0 ? diff - 1 : diff, size);

            int zeros = 0;
            for (int k = 1; k < 64; k++)
            {
                int v = q[Zigzag[k]];
                if (v == 0)
                {
                    zeros++;
                    continue;
                }
                while (zeros > 15)
                {
                    Put(acCode[0xF0], acLen[0xF0]);
                    zeros -= 16;
                }
                int s = Size(v);
                int sym = (zeros << 4) | s;
                Put(acCode[sym], acLen[sym]);
                Put(v < 0 ? v - 1 : v, s);
                zeros = 0;
            }
            if (zeros > 0) Put(acCode[0], acLen[0]);
            return q[0];
        }

        private static int Size(int v)
        {
            v = Math.Abs(v);
            int s = 0;
            while (v != 0)
            {
                s++;
                v >>= 1;
            }
            return s;
        }

        private void Put(int value, int bits)
        {
            if (bits == 0) return;
            bitBuffer = (bitBuffer << bits) | (value & ((1 << bits) - 1));
            bitCount += bits;
            while (bitCount >= 8)
            {
                int b = (bitBuffer >> (bitCount - 8)) & 0xFF;
                output.WriteByte((byte)b);
                if (b == 0xFF) output.WriteByte(0);
                bitCount -= 8;
            }
            bitBuffer &= (1 << bitCount) - 1;
        }

        private void Flush()
        {
            if (bitCount > 0) Put(0x7F, 8 - bitCount);
        }

        private void WriteHeader(int w, int h)
        {
            Raw(0xFF, 0xD8);
            Segment(0xDB, Concat(new byte[] { 0 }, LumaQuant));
            Segment(0xDB, Concat(new byte[] { 1 }, ChromaQuant));
            Segment(0xC0, new byte[] { 8, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1 });
            Segment(0xC4, Concat(new byte[] { 0x00 }, DcBits, DcVals));
            Segment(0xC4, Concat(new byte[] { 0x10 }, AcBits, AcVals));
            Segment(0xC4, Concat(new byte[] { 0x01 }, DcChromaBits, DcVals));
            Segment(0xC4, Concat(new byte[] { 0x11 }, AcChromaBits, AcChromaVals));
            Segment(0xDA, new byte[] { 3, 1, 0x00, 2, 0x11, 3, 0x11, 0, 63, 0 });
        }

        private void Raw(params byte[] bytes)
        {
            output.Write(bytes, 0, bytes.Length);
        }

        private void Segment(byte marker, byte[] body)
        {
            int len = body.Length + 2;
            Raw(0xFF, marker, (byte)(len >> 8), (byte)len);
            output.Write(body, 0, body.Length);
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (byte[] p in parts) n += p.Length;
            byte[] r = new byte[n];
            int o = 0;
            foreach (byte[] p in parts)
            {
                Buffer.BlockCopy(p, 0, r, o, p.Length);
                o += p.Length;
            }
            return r;
        }
    }
}
