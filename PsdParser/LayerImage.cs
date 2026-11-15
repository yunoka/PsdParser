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

        //BGRA(1画素4バイト)の画像にする。8ビットのRGBとCMYKだけに対応する
        public byte[] Read()
        {
            if (depth != 8)
                throw new NotSupportedException($"Not supported bit depth: {depth}");

            //呼び出し側の上限(PsdLimits)は、ファイル全体ではなくこのレイヤーだけに効かせる(キャンバスからはみ出した大きなレイヤーが
            //1枚あっても、ほかのレイヤーは読めるように)。大きさは64ビットで求め、配列に収まるかも確かめる(32ビットではあふれうる)
            var side = limits.SideLimit(isPSB); //仕様の上限と呼び出し側の上限の小さいほう
            long bytes = (long)Width * Height * 4;
            if (Width < 0 || Height < 0 || Width > side || Height > side || (long)Width * Height > limits.MaxPixels || bytes > Array.MaxLength)
                throw new InvalidPsdDataException($"Layer image {Width}x{Height} exceeds the limit.");

            try
            {
                if (colorMode is ColorMode.RGB)
                    return ReadRGB();
                else if (colorMode is ColorMode.CMYK)
                    return ReadCMYK();
            }
            catch (Exception ex) when (PsdFile.IsBrokenData(ex))
            {
                throw new InvalidPsdDataException($"The layer image is broken: {ex.Message}", ex);
            }
            throw new NotSupportedException($"Not supported format: {colorMode}");
        }

        byte[] ReadRGB()
        {
            byte[] buffer = new byte[Width * Height * 4];

            var b = new byte[Width];
            var g = new byte[Width];
            var r = new byte[Width];
            var a = new byte[Width];
            Array.Fill(a, byte.MaxValue);

            //y行ループの内側で毎回ChannelIdのswitchをしないよう、対応するチャンネルだけ事前に抽出する。
            var planes = BuildPlanes(r, g, b, a);

            //範囲を確かめる書き方(Span)で並べ替える
            for (int y = 0; y < Height; y++)
            {
                foreach (var (image, plane) in planes)
                    image.ReadLine(plane, y);

                //1画素を4バイトまとめて書く(BGRAの並び。範囲の確認を画素ごと1回に減らす)
                var dst = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan(y * Width * 4, Width * 4));
                for (int x = 0; x < dst.Length; x++)
                    dst[x] = Pack(b[x], g[x], r[x], a[x]);
            }
            return buffer;
        }

        //メモリ上の並びが B, G, R, A になる 4 バイトの値(リトルエンディアン・ビッグエンディアンのどちらでも)
        static uint Pack(byte b, byte g, byte r, byte a) => BitConverter.IsLittleEndian
            ? (uint)(b | g << 8 | r << 16 | a << 24)
            : (uint)(b << 24 | g << 16 | r << 8 | a);

        List<(ChannelImageData image, byte[] plane)> BuildPlanes(byte[] r, byte[] g, byte[] b, byte[] a)
        {
            var planes = new List<(ChannelImageData, byte[])>(ChannelImages.Length);
            foreach (var image in ChannelImages)
            {
                var plane = image.ChannelId switch { ChannelId.R => r, ChannelId.G => g, ChannelId.B => b, ChannelId.TransparencyMask => a, _ => null };
                if (plane is not null)
                    planes.Add((image, plane));
            }
            return planes;
        }

        byte[] ReadCMYK()
        {
            byte[] buffer = new byte[Width * Height * 4];
            var c = new byte[Width];
            var m = new byte[Width];
            var y = new byte[Width];
            var k = new byte[Width];
            var a = new byte[Width];
            Array.Fill(a, byte.MaxValue);

            var planes = new List<(ChannelImageData image, byte[] plane)>(ChannelImages.Length);
            foreach (var image in ChannelImages)
            {
                var plane = image.ChannelId switch { ChannelId.C => c, ChannelId.M => m, ChannelId.Y => y, ChannelId.K => k, ChannelId.TransparencyMask => a, _ => null };
                if (plane is not null)
                    planes.Add((image, plane));
            }

            //範囲を確かめる書き方(Span)で並べ替える
            for (int yi = 0; yi < Height; yi++)
            {
                foreach (var (image, plane) in planes)
                    image.ReadLine(plane, yi);

                var dst = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan(yi * Width * 4, Width * 4));
                for (int x = 0; x < dst.Length; x++)
                    dst[x] = Pack((byte)(y[x] * k[x] / 255), (byte)(m[x] * k[x] / 255), (byte)(c[x] * k[x] / 255), a[x]);
            }
            return buffer;
        }
    }
}