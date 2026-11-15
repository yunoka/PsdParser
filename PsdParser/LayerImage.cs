using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PsdParser
{
    public class LayerImage
    {
        public ChannelImageData[] ChannelImages { get; }

        public int Width { get; }
        public int Height { get; }
        readonly ColorMode colorMode;
        readonly int depth;
        readonly bool isPSB;
        readonly PsdLimits limits;
        internal LayerImage(PsdBinaryReader reader, LayerRecord layer, bool isPSB, int depth, ColorMode colorMode, long sectionEnd)
        {
            this.depth = depth;
            this.isPSB = isPSB;
            limits = reader.Limits;
            Width = layer.Right - layer.Left;
            Height = layer.Bottom - layer.Top;
            this.colorMode = colorMode;
            
            ChannelImages = new ChannelImageData[layer.Channels];
            for (int c = 0; c < layer.Channels; c++)
            {
                int channelImageWidth, channelImageHeight;
                var mask = layer.LayerMaskAndAdjustmentLayerData;
                if(ChannelId.TransparencyMask <= layer.ChannelInfos[c].Id)
                {
                    channelImageWidth = Width;
                    channelImageHeight = Height;
                }
                else if (layer.ChannelInfos[c].Id is ChannelId.RealUserSuppliedLayerMask)
                {
                    //-3(実際のユーザーマスク)は、マスクのデータの後半の範囲(Real ...)を使う(仕様書の Layer mask / adjustment layer data)
                    channelImageWidth = mask.MaskRight - mask.MaskLeft;
                    channelImageHeight = mask.MaskBottom - mask.MaskTop;
                }
                else
                {
                    channelImageWidth = mask.Right - mask.Left;
                    channelImageHeight = mask.Bottom - mask.Top;
                }
                if (reader.BaseStream.Position + layer.ChannelInfos[c].ChannelDataLength > sectionEnd)
                    throw new InvalidPsdDataException($"Channel data ({layer.ChannelInfos[c].ChannelDataLength} bytes) does not fit in the layer info section.");
                ChannelImages[c] = new ChannelImageData(reader, layer.ChannelInfos[c].Id, isPSB, channelImageWidth, channelImageHeight, depth, layer.ChannelInfos[c].ChannelDataLength);
            }
        }

        //BGRA(1画素4バイト、8ビット)の画像にする。RGB・グレースケール・CMYKの、8・16・32ビットに対応する(32ビットの CMYK は除く)。
        //16・32ビットは8ビットに直す(16ビットは四捨五入。32ビットは浮動小数を0〜1に収め、色はリニアから sRGB に直してから四捨五入)
        public byte[] Read()
        {
            var buffer = new byte[CheckedLength()];
            var (planes, alpha) = Planes(byte.MaxValue);
            Compose((y, colors, alphaRow) =>
            {
                //8ビットは読んだ行をそのまま使う(写さない)
                var source = depth == 8 ? colors : planes;
                var a = depth == 8 && alphaRow is not null ? alphaRow : alpha;
                if (depth != 8)
                {
                    for (int i = 0; i < colors.Length; i++)
                        To8(colors[i], planes[i], isColor: true);
                    if (alphaRow is not null)
                        To8(alphaRow, alpha, isColor: false);
                }
                var (r, g, b) = Rgb(source, byte.MaxValue);
                var dst = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan(y * Width * 4, Width * 4));
                for (int x = 0; x < dst.Length; x++)
                    dst[x] = Pack(b[x], g[x], r[x], a[x]);
            });
            return buffer;
        }

        //1チャンネル16ビットの RGBA(1画素4つの ushort、ストレートアルファ)の画像にする(Skia の Rgba16161616 の並び)。
        //8ビットは ×257 で広げ、32ビットは Read() と同じく0〜1に収め、色をリニアから sRGB に直してから16ビットにする。
        //1画素8バイトで、Read() の2倍のメモリを使う(PsdLimits の画素の数の上限は、形式によらず同じに効く)
        public ushort[] ReadRgba16()
        {
            var buffer = new ushort[CheckedLength()];
            var (planes, alpha) = Planes(ushort.MaxValue);
            Compose((y, colors, alphaRow) =>
            {
                for (int i = 0; i < colors.Length; i++)
                    To16(colors[i], planes[i], isColor: true);
                if (alphaRow is not null)
                    To16(alphaRow, alpha, isColor: false);
                var (r, g, b) = Rgb(planes, ushort.MaxValue);
                Interleave(buffer.AsSpan(y * Width * 4, Width * 4), r, g, b, alpha);
            });
            return buffer;
        }

        //1チャンネル浮動小数の RGBA(1画素4つの float、ストレートアルファ)の画像にする(Skia の RgbaF32 の並び)。
        //linear が true なら色はリニア(ガンマ 1.0)、false なら sRGB の明るさで返す。ビット数によらずそろえる
        //(32ビットのファイルはリニア、8・16ビットは sRGB の明るさで保存されているとみなす。埋め込まれた ICC プロファイルは使わない)。
        //32ビットの色の0〜1の外の値は残し(sRGB にするときも、負の値は符号を保って同じ式で直す)、NaN は0、無限大は float の最大の値にする。
        //透明度は0〜1に収め、リニア・sRGB の直しはしない。CMYK の式は、保存されたままの値で計算してから直す。
        //1画素16バイトで、Read() の4倍のメモリを使う(PsdLimits の画素の数の上限は、形式によらず同じに効く)
        public float[] ReadRgbaFloat(bool linear = true)
        {
            var buffer = new float[CheckedLength()];
            var (planes, alpha) = Planes(1f);
            //8・16ビットの RGB・グレースケールは、値の数が限られるので、リニアへの直しを表で引く
            var table = linear && depth != 32 && colorMode != ColorMode.CMYK ? LinearTable(depth) : null;
            Compose((y, colors, alphaRow) =>
            {
                for (int i = 0; i < colors.Length; i++)
                    ToFloat(colors[i], planes[i], table);
                if (alphaRow is not null)
                {
                    ToFloat(alphaRow, alpha, null);
                    for (int x = 0; x < alpha.Length; x++)
                        alpha[x] = Math.Clamp(alpha[x], 0, 1);
                }
                var (r, g, b) = Rgb(planes, 1f);
                if (table is null && linear != (depth == 32))
                {
                    //グレースケールは3つが同じ行なので、1回だけ直す
                    Transfer(r, linear);
                    if (g != r)
                        Transfer(g, linear);
                    if (b != r)
                        Transfer(b, linear);
                }
                Interleave(buffer.AsSpan(y * Width * 4, Width * 4), r, g, b, alpha);
            });
            return buffer;
        }

        //形式とビット数を確かめ、画像の要素の数(幅 × 高さ × 4)を返す。
        //呼び出し側の上限(PsdLimits)は、ファイル全体ではなくこのレイヤーだけに効かせる(キャンバスからはみ出した大きなレイヤーが
        //1枚あっても、ほかのレイヤーは読めるように)。大きさは64ビットで求め、配列の要素の数の上限に収まるかも確かめる(32ビットではあふれうる)
        int CheckedLength()
        {
            if (depth is not (8 or 16 or 32))
                throw new NotSupportedException($"Not supported bit depth: {depth}");
            if (colorMode is not (ColorMode.RGB or ColorMode.Grayscale or ColorMode.CMYK))
                throw new NotSupportedException($"Not supported format: {colorMode}");
            //Photoshop の32ビットは RGB とグレースケールだけ(32ビットの CMYK のファイルは作れない)
            if (depth == 32 && colorMode == ColorMode.CMYK)
                throw new NotSupportedException("32-bit CMYK is not supported.");
            var side = limits.SideLimit(isPSB); //仕様の上限と呼び出し側の上限の小さいほう
            long elements = (long)Width * Height * 4;
            if (Width < 0 || Height < 0 || Width > side || Height > side || (long)Width * Height > limits.MaxPixels || elements > Array.MaxLength)
                throw new InvalidPsdDataException($"Layer image {Width}x{Height} exceeds the limit.");
            return (int)elements;
        }

        int ColorCount => colorMode switch { ColorMode.RGB => 3, ColorMode.Grayscale => 1, _ => 4 };

        //色のチャンネルと透明度の、変換したあとの1行(透明度は、チャンネルがなければ max のまま)
        (T[][] Colors, T[] Alpha) Planes<T>(T max)
        {
            var colors = new T[ColorCount][];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new T[Width];
            var alpha = new T[Width];
            Array.Fill(alpha, max);
            return (colors, alpha);
        }

        //色の3つの行。グレースケールは同じ行を3つ並べ、CMYK は ICC プロファイルを使わない簡単な式で、行をその場で書き換える
        //(PSDでは値が反転していて、max がインク0%。R = C × K ÷ max。整数は切り捨て)。3つの形式で同じ式を使う
        (T[] R, T[] G, T[] B) Rgb<T>(T[][] planes, T max) where T : INumber<T>
        {
            switch (colorMode)
            {
                case ColorMode.Grayscale:
                    return (planes[0], planes[0], planes[0]);
                case ColorMode.CMYK:
                    var k = planes[3];
                    for (int i = 0; i < 3; i++)
                    {
                        var plane = planes[i];
                        if (typeof(T) == typeof(float))
                        {
                            for (int x = 0; x < plane.Length; x++)
                                plane[x] = plane[x] * k[x] / max;
                        }
                        else
                        {
                            //整数は long で計算する(65535 × 65535 は int に収まらない)
                            long m = long.CreateTruncating(max);
                            for (int x = 0; x < plane.Length; x++)
                                plane[x] = T.CreateTruncating(long.CreateTruncating(plane[x]) * long.CreateTruncating(k[x]) / m);
                        }
                    }
                    return (planes[0], planes[1], planes[2]);
                default:
                    return (planes[0], planes[1], planes[2]);
            }
        }

        //R・G・B・A の並びに書く
        static void Interleave<T>(Span<T> dst, T[] r, T[] g, T[] b, T[] a)
        {
            for (int x = 0; x < r.Length; x++)
            {
                dst[x * 4] = r[x];
                dst[x * 4 + 1] = g[x];
                dst[x * 4 + 2] = b[x];
                dst[x * 4 + 3] = a[x];
            }
        }

        //colors: 色のチャンネルの1行(本来のビット数、ビッグエンディアン。ないチャンネルは0)。alpha: 透明度の1行(ないときは null)
        delegate void RowWriter(int y, byte[][] colors, byte[]? alpha);

        //色のチャンネル(RGBなら0〜2、グレースケールなら0、CMYKなら0〜3)と透明度(-1)を、上から1行ずつ読んで write に渡す
        void Compose(RowWriter write)
        {
            var rowBytes = (int)(((long)Width * depth + 7) / 8); //Width は CheckedLength で上限を確かめてある
            var colors = new byte[ColorCount][];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new byte[rowBytes];
            byte[]? alpha = null;

            //y行ループの内側で毎回ChannelIdのswitchをしないよう、対応するチャンネルだけ事前に抽出する
            var sources = new List<(ChannelImageData Image, byte[] Row, ChannelImageData.ZipCursor Cursor)>();
            foreach (var image in ChannelImages)
            {
                //色のチャンネルは 0〜(色の数 − 1)、透明度は -1 だけ。それ以外(特色・余分なチャンネル・マスク)は使わない
                byte[] row;
                if (image.ChannelId == ChannelId.TransparencyMask)
                    row = alpha ??= new byte[rowBytes];
                else if ((int)image.ChannelId >= 0 && (int)image.ChannelId < colors.Length)
                    row = colors[(int)image.ChannelId];
                else
                    continue;
                sources.Add((image, row, new ChannelImageData.ZipCursor()));
            }

            try
            {
                for (int y = 0; y < Height; y++)
                {
                    try
                    {
                        foreach (var (image, row, cursor) in sources)
                            image.ReadLine(row, y, cursor);
                    }
                    catch (Exception ex) when (PsdFile.IsBrokenData(ex))
                    {
                        //壊れたデータの例外にそろえるのは読み込みだけ(変換と書き込みの誤りは、ファイルの壊れと取り違えないようにそのまま出す)
                        throw new InvalidPsdDataException($"The layer image is broken: {ex.Message}", ex);
                    }
                    write(y, colors, alpha);
                }
            }
            finally
            {
                //ZIP の展開の途中の状態(この呼び出しだけのもの)は、読み終えたときも失敗したときも閉じる
                foreach (var source in sources)
                    source.Cursor.Dispose();
            }
        }

        //32ビットの値(NaN は0、無限大は float の最大の値)
        static float Float32(byte[] row, int x)
        {
            var f = BinaryPrimitives.ReadSingleBigEndian(row.AsSpan(x * 4));
            return float.IsNaN(f) ? 0 : Math.Clamp(f, float.MinValue, float.MaxValue);
        }

        //リニアの値を sRGB の明るさにする(負の値は符号を保って同じ式で直す)と、その逆
        static double LinearToSrgb(double v) => v < 0 ? -LinearToSrgb(-v) : v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        static double SrgbToLinear(double v) => v < 0 ? -SrgbToLinear(-v) : v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

        //8・16ビットの値(0〜255・0〜65535)をリニアの値にする表(初めて使うときに1回だけ作る)
        static float[]? linear8, linear16;
        static float[] LinearTable(int depth)
        {
            var max = depth == 8 ? 255 : 65535;
            ref var table = ref depth == 8 ? ref linear8 : ref linear16;
            if (table is null)
            {
                var values = new float[max + 1];
                for (int i = 0; i <= max; i++)
                    values[i] = (float)SrgbToLinear(i / (double)max);
                table = values; //別のスレッドが同時に作っても、同じ中身になる
            }
            return table;
        }

        //32ビットの値を0〜1に収め、色はリニアから sRGB の明るさに直す(32ビットの色はリニア〔ガンマ 1.0〕で保存されている)
        static double Clamped32(byte[] row, int x, bool isColor)
        {
            double v = Math.Clamp(Float32(row, x), 0, 1);
            return isColor ? LinearToSrgb(v) : v;
        }

        //1行を8ビットにする(8ビットのときは呼ばない)。16ビットは四捨五入、32ビットは Clamped32 のあと四捨五入(0.5 は大きいほうへ)
        void To8(byte[] row, byte[] plane, bool isColor)
        {
            if (depth == 16)
            {
                for (int x = 0; x < plane.Length; x++)
                    plane[x] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(x * 2)) * 255 + 32767) / 65535);
            }
            else
            {
                for (int x = 0; x < plane.Length; x++)
                    plane[x] = (byte)Math.Round(Clamped32(row, x, isColor) * 255, MidpointRounding.AwayFromZero);
            }
        }

        //1行を16ビットにする。8ビットは ×257(0〜255 を 0〜65535 にちょうど広げる)、32ビットは Clamped32 のあと四捨五入
        void To16(byte[] row, ushort[] plane, bool isColor)
        {
            switch (depth)
            {
                case 8:
                    for (int x = 0; x < plane.Length; x++)
                        plane[x] = (ushort)(row[x] * 257);
                    break;
                case 16:
                    for (int x = 0; x < plane.Length; x++)
                        plane[x] = BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(x * 2));
                    break;
                default:
                    for (int x = 0; x < plane.Length; x++)
                        plane[x] = (ushort)Math.Round(Clamped32(row, x, isColor) * 65535, MidpointRounding.AwayFromZero);
                    break;
            }
        }

        //1行を浮動小数にする。8・16ビットは0〜1(sRGB の明るさ。table があれば、それで引いたリニアの値)、32ビットはファイルの値(リニア)
        void ToFloat(byte[] row, float[] plane, float[]? table)
        {
            switch (depth)
            {
                case 8:
                    for (int x = 0; x < plane.Length; x++)
                        plane[x] = table is null ? row[x] / 255f : table[row[x]];
                    break;
                case 16:
                    for (int x = 0; x < plane.Length; x++)
                    {
                        var v = BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(x * 2));
                        plane[x] = table is null ? v / 65535f : table[v];
                    }
                    break;
                default:
                    for (int x = 0; x < plane.Length; x++)
                        plane[x] = Float32(row, x);
                    break;
            }
        }

        //色の1行を、リニアに(linear が true)、または sRGB の明るさに直す
        static void Transfer(float[] plane, bool linear)
        {
            for (int x = 0; x < plane.Length; x++)
                plane[x] = (float)(linear ? SrgbToLinear(plane[x]) : LinearToSrgb(plane[x]));
        }

        //メモリ上の並びが B, G, R, A になる 4 バイトの値(リトルエンディアン・ビッグエンディアンのどちらでも)
        static uint Pack(byte b, byte g, byte r, byte a) => BitConverter.IsLittleEndian
            ? (uint)(b | g << 8 | r << 16 | a << 24)
            : (uint)(b << 24 | g << 16 | r << 8 | a);
    }
}