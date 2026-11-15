using System.Buffers.Binary;
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

        //BGRA(1画素4バイト、8ビット)の画像にする。RGB・グレースケール・CMYKの、8・16・32ビットに対応する。
        //16・32ビットは8ビットに直す(16ビットは四捨五入、32ビットは浮動小数を0〜1に収めてから四捨五入)
        public byte[] Read()
        {
            if (depth is not (8 or 16 or 32))
                throw new NotSupportedException($"Not supported bit depth: {depth}");
            if (colorMode is not (ColorMode.RGB or ColorMode.Grayscale or ColorMode.CMYK))
                throw new NotSupportedException($"Not supported format: {colorMode}");

            //呼び出し側の上限(PsdLimits)は、ファイル全体ではなくこのレイヤーだけに効かせる(キャンバスからはみ出した大きなレイヤーが
            //1枚あっても、ほかのレイヤーは読めるように)。大きさは64ビットで求め、配列に収まるかも確かめる(32ビットではあふれうる)
            var side = limits.SideLimit(isPSB); //仕様の上限と呼び出し側の上限の小さいほう
            long bytes = (long)Width * Height * 4;
            if (Width < 0 || Height < 0 || Width > side || Height > side || (long)Width * Height > limits.MaxPixels || bytes > Array.MaxLength)
                throw new InvalidPsdDataException($"Layer image {Width}x{Height} exceeds the limit.");

            try
            {
                return Compose();
            }
            catch (Exception ex) when (PsdFile.IsBrokenData(ex))
            {
                throw new InvalidPsdDataException($"The layer image is broken: {ex.Message}", ex);
            }
        }

        byte[] Compose()
        {
            byte[] buffer = new byte[Width * Height * 4];

            //色のチャンネル(RGBなら0〜2、グレースケールなら0、CMYKなら0〜3)と透明度(-1)の、8ビットに直した1行分
            int colors = colorMode switch { ColorMode.RGB => 3, ColorMode.Grayscale => 1, _ => 4 };
            var planes = new byte[colors + 1][];
            for (int i = 0; i < planes.Length; i++)
                planes[i] = new byte[Width];
            var alpha = planes[colors];
            Array.Fill(alpha, byte.MaxValue);

            //y行ループの内側で毎回ChannelIdのswitchをしないよう、対応するチャンネルだけ事前に抽出する
            var rowBytes = (int)(((long)Width * depth + 7) / 8); //Width は Read() で上限を確かめてある
            var sources = new List<(ChannelImageData Image, byte[] Raw, byte[] Plane, ChannelImageData.ZipCursor Cursor)>();
            foreach (var image in ChannelImages)
            {
                //色のチャンネルは 0〜(色の数 − 1)、透明度は -1 だけ。それ以外(特色・余分なチャンネル・マスク)は使わない
                int index;
                if (image.ChannelId == ChannelId.TransparencyMask)
                    index = colors;
                else if ((int)image.ChannelId >= 0 && (int)image.ChannelId < colors)
                    index = (int)image.ChannelId;
                else
                    continue;
                sources.Add((image, depth == 8 ? planes[index] : new byte[rowBytes], planes[index], new ChannelImageData.ZipCursor()));
            }

            try
            {
                ComposeRows(buffer, colors, planes, sources);
            }
            finally
            {
                //ZIP の展開の途中の状態(この呼び出しだけのもの)は、読み終えたときも失敗したときも閉じる
                foreach (var source in sources)
                    source.Cursor.Dispose();
            }
            return buffer;
        }

        void ComposeRows(byte[] buffer, int colors, byte[][] planes, List<(ChannelImageData Image, byte[] Raw, byte[] Plane, ChannelImageData.ZipCursor Cursor)> sources)
        {
            var alpha = planes[colors];
            for (int y = 0; y < Height; y++)
            {
                foreach (var (image, raw, plane, cursor) in sources)
                {
                    image.ReadLine(raw, y, cursor);
                    if (depth != 8)
                        To8Bit(raw, plane, isColor: plane != alpha);
                }

                //1画素を4バイトまとめて書く(BGRAの並び。範囲の確認を画素ごと1回に減らす)
                var dst = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan(y * Width * 4, Width * 4));
                switch (colorMode)
                {
                    case ColorMode.RGB:
                        for (int x = 0; x < dst.Length; x++)
                            dst[x] = Pack(planes[2][x], planes[1][x], planes[0][x], alpha[x]);
                        break;
                    case ColorMode.Grayscale:
                        for (int x = 0; x < dst.Length; x++)
                            dst[x] = Pack(planes[0][x], planes[0][x], planes[0][x], alpha[x]);
                        break;
                    default:
                        //CMYK(PSDでは値が反転していて、255がインク0%)。ICCプロファイルを使わない簡単な式
                        for (int x = 0; x < dst.Length; x++)
                        {
                            var k = planes[3][x];
                            dst[x] = Pack((byte)(planes[2][x] * k / 255), (byte)(planes[1][x] * k / 255), (byte)(planes[0][x] * k / 255), alpha[x]);
                        }
                        break;
                }
            }
        }

        //16・32ビットの1行(ビッグエンディアン)を8ビットにする。32ビットの色はリニア(ガンマ 1.0)で保存されているので、
        //色のチャンネルは sRGB の明るさに直してから8ビットにする(透明度はリニアのまま)。丸めは四捨五入(0.5 は大きいほうへ)
        void To8Bit(byte[] raw, byte[] plane, bool isColor)
        {
            if (depth == 16)
            {
                for (int x = 0; x < plane.Length; x++)
                    plane[x] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(x * 2)) * 255 + 32767) / 65535);
            }
            else
            {
                for (int x = 0; x < plane.Length; x++)
                {
                    var value = BinaryPrimitives.ReadSingleBigEndian(raw.AsSpan(x * 4));
                    double v = Math.Clamp(float.IsNaN(value) ? 0 : value, 0, 1);
                    if (isColor)
                        v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
                    plane[x] = (byte)Math.Round(v * 255, MidpointRounding.AwayFromZero);
                }
            }
        }

        //メモリ上の並びが B, G, R, A になる 4 バイトの値(リトルエンディアン・ビッグエンディアンのどちらでも)
        static uint Pack(byte b, byte g, byte r, byte a) => BitConverter.IsLittleEndian
            ? (uint)(b | g << 8 | r << 16 | a << 24)
            : (uint)(b << 24 | g << 16 | r << 8 | a);
    }
}