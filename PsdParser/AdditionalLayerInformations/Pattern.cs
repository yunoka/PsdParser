using System.Buffers.Binary;
using System.Text;

namespace PsdParser.AdditionalLayerInformations
{
    //パターン1つ(Patt・Pat2・Pat3 の中身)。画像は Virtual Memory Array List(VMAL)の配列に、チャンネルごとに入っている
    public sealed class Pattern
    {
        readonly byte[] source;
        readonly PsdLimits limits;
        readonly bool isPSB;
        //PsdFile を Dispose したかを知るため(Read() をほかのチャンネルと同じく ObjectDisposedException にする)
        readonly Patterns owner;
        readonly byte[]? colorTable;

        public uint Version { get; }
        //画像のモード(ファイルの先頭の色の形式と同じ値)。画像にできるのは RGB・グレースケール・インデックスだけ(CanRead)
        public ColorMode ImageMode { get; }
        //仕様書の Point(縦・横)。psd-tools も意味を解釈していない。画像の大きさは Width・Height(VMAL の範囲)を使う
        public short PointVertical { get; }
        public short PointHorizontal { get; }
        //名前(末尾の NUL は除く)
        public string Name { get; }
        //ID(記述子の Ptrn の Idnt と同じ文字列)
        public string Id { get; }
        //インデックスカラーの色の表(R・G・B を 256 色分、768 バイト)。インデックスでなければ null
        public ReadOnlyMemory<byte>? ColorTable => colorTable;
        //インデックスのとき、色の表のあとにある4バイトを、ビッグエンディアンの2つの ushort として読んだもの(インデックスでなければ null)。
        //色の数と透明の色の番号(画像のリソース 1046・1047 と同じ形)の候補だが、仕様書になく、psd-tools も読み飛ばしていて、意味は確かめていない。
        //Read() では使わない(psd-zoo の実物は 256 と 255。その画像は番号 255 を使っておらず、透明かどうかを確かめられなかった)
        public ushort? ColorTableCountCandidate { get; }
        public ushort? TransparentIndexCandidate { get; }
        //VMAL の範囲(上・左・下・右)
        public int Top { get; }
        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        //VMAL のチャンネルの数(ファイルに書かれた値。配列はこの数 + 2 個ある)
        public uint ChannelCount { get; }
        //書かれている配列だけ(ファイルの順)。Index は VMAL の中の番号
        public IReadOnlyList<PatternChannel> Channels { get; }
        //画像のモードが、Read() で画像にできるものか(RGB・グレースケール・インデックス)。
        //true でも、データが壊れていれば Read() は InvalidPsdDataException、対応していないビット数・圧縮なら NotSupportedException
        public bool CanRead => ImageMode is ColorMode.RGB or ColorMode.Grayscale or ColorMode.Indexed;

        Pattern(Patterns owner, byte[] source, PsdLimits limits, bool isPSB, uint version, ColorMode imageMode, short pointVertical, short pointHorizontal,
            string name, string id, byte[]? colorTable, (ushort, ushort)? trailer, (int Top, int Left, int Bottom, int Right) rect, uint channelCount, IReadOnlyList<PatternChannel> channels)
        {
            this.owner = owner;
            this.source = source;
            this.limits = limits;
            this.isPSB = isPSB;
            this.colorTable = colorTable;
            ColorTableCountCandidate = trailer?.Item1;
            TransparentIndexCandidate = trailer?.Item2;
            Version = version;
            ImageMode = imageMode;
            PointVertical = pointVertical;
            PointHorizontal = pointHorizontal;
            Name = name;
            Id = id;
            (Top, Left, Bottom, Right) = rect;
            ChannelCount = channelCount;
            Channels = channels;
        }

        //版 4(1)、画像のモード 4、Point 4(縦・横)、名前(Unicode)、ID(パスカル文字列。詰め物なし)、
        //インデックスなら色の表 768 と4バイト(意味は未確認。psd-tools も読み飛ばす)、VMAL
        internal static Pattern Parse(PatternReader reader, Patterns owner, byte[] source, PsdLimits limits, bool isPSB)
        {
            var version = reader.ReadUInt32();
            if (version != 1)
                throw new InvalidPsdDataException($"Unknown pattern version {version}.");
            var mode = reader.ReadUInt32();
            if (mode > ushort.MaxValue || !Enum.IsDefined((ColorMode)mode))
                throw new InvalidPsdDataException($"Pattern image mode {mode} is out of range.");
            var imageMode = (ColorMode)mode;
            var pointVertical = reader.ReadInt16();
            var pointHorizontal = reader.ReadInt16();
            var nameLength = reader.ReadUInt32();
            var name = Encoding.BigEndianUnicode.GetString(reader.ReadBytes(nameLength * 2L, "Pattern name")).TrimEnd('\0');
            var idLength = reader.ReadByte();
            var id = Encoding.Latin1.GetString(reader.ReadBytes(idLength, "Pattern ID"));
            byte[]? colorTable = null;
            (ushort, ushort)? trailer = null;
            if (imageMode == ColorMode.Indexed)
            {
                colorTable = reader.ReadBytes(768, "Pattern color table").ToArray();
                trailer = (reader.ReadUInt16(), reader.ReadUInt16());
            }

            var vmalVersion = reader.ReadUInt32();
            if (vmalVersion != 3)
                throw new InvalidPsdDataException($"Unknown pattern image data version {vmalVersion}.");
            var vmal = reader.Sub(reader.ReadUInt32(), "Pattern image data");
            var rect = ReadRect(vmal, "Pattern");
            var channelCount = vmal.ReadUInt32();
            var channels = new List<PatternChannel>();
            //書かれていない配列は4バイトしかないので、配列の数はデータの長さで抑えられる(書かれていない配列のオブジェクトは作らない)
            for (long i = 0; i < channelCount + 2L; i++)
            {
                if (vmal.ReadUInt32() == 0)
                    continue;
                var length = vmal.ReadUInt32();
                if (length == 0)
                    continue;
                //深さ 4、範囲 16、深さ 2、圧縮 1 のあとがデータ
                if (length < 23)
                    throw new InvalidPsdDataException($"Pattern channel data ({length} bytes) is too short.");
                var array = vmal.Sub(length, "Pattern channel data");
                var depth = array.ReadUInt32();
                var channelRect = ReadRect(array, "Pattern channel");
                var pixelDepth = array.ReadUInt16();
                var compression = array.ReadByte();
                channels.Add(new PatternChannel((int)i, depth, channelRect, pixelDepth, (ChannelImageDataCompression)compression, array.Position, array.Remaining));
            }
            return new Pattern(owner, source, limits, isPSB, version, imageMode, pointVertical, pointHorizontal, name, id, colorTable, trailer, rect, channelCount, channels);
        }

        //範囲(上・左・下・右。符号なしの4バイト)。幅と高さが0以上で、32ビットの整数に収まること(大きさの上限は Read() で確かめる)
        static (int, int, int, int) ReadRect(PatternReader reader, string what)
        {
            long top = reader.ReadUInt32(), left = reader.ReadUInt32(), bottom = reader.ReadUInt32(), right = reader.ReadUInt32();
            if (top > int.MaxValue || left > int.MaxValue || bottom > int.MaxValue || right > int.MaxValue || bottom < top || right < left)
                throw new InvalidPsdDataException($"{what} rectangle ({top}, {left}, {bottom}, {right}) is out of range.");
            return ((int)top, (int)left, (int)bottom, (int)right);
        }

        //BGRA(1画素4バイト、8ビット、ストレートアルファ)の画像にする(LayerImage.Read() と同じ形)。
        //RGB・グレースケールは 8・16・32 ビット(16・32 ビットの直し方は LayerImage.Read() と同じ)、インデックスは 8 ビット。
        //色の配列 i は、VMAL の番号(Index)が i の配列。欠けていれば壊れたデータ。
        //透明度は、番号が色の数以上の配列のうち、いちばん小さい番号のもの(RGB・グレースケールだけ)。psd-tools は「書かれている配列の、
        //色の数の次」を透明度にしていて、色の配列が 0〜(色の数 − 1) にそろっているとき、この決まりと同じになる。
        //Photoshop がどの配列に透明度を書くかは、実物で確かめていない。インデックスは透明度を使わない(psd-tools と同じ)。
        //大きさの上限(PsdLimits)はここで確かめる(レイヤーと同じく、上限を越えるパターンがあってもファイルとほかのパターンは読めるように)
        public byte[] Read()
        {
            ObjectDisposedException.ThrowIf(owner.Closed, this);
            if (!CanRead)
                throw new NotSupportedException($"Not supported pattern image mode: {ImageMode}");
            var colorCount = ImageMode == ColorMode.RGB ? 3 : 1;
            var used = new List<PatternChannel>();
            for (int i = 0; i < colorCount; i++)
                used.Add(Channels.FirstOrDefault(c => c.Index == i) ?? throw new InvalidPsdDataException($"Pattern color channel {i} is missing."));
            //色の配列は、すべて同じ深さであること(Photoshop は1つのパターンの配列を同じ深さで書く。違えば壊れたデータ)
            var depth = used[0].Depth;
            foreach (var channel in used)
                CheckChannel(channel, depth);
            if (ImageMode == ColorMode.Indexed ? depth != 8 : depth is not (8 or 16 or 32))
                throw new NotSupportedException($"Not supported pattern bit depth: {depth}");
            if (ImageMode != ColorMode.Indexed && Channels.Where(c => c.Index >= colorCount).MinBy(c => c.Index) is { } alpha)
            {
                //透明度の深さ・大きさが色と違うものも壊れたデータとして断る(無視して不透明にすると、見た目が黙って変わるため)
                CheckChannel(alpha, depth);
                used.Add(alpha);
            }
            var side = limits.SideLimit(isPSB);
            long elements = (long)Width * Height * 4;
            if (Width > side || Height > side || (long)Width * Height > limits.MaxPixels || elements > Array.MaxLength)
                throw new InvalidPsdDataException($"Pattern image {Width}x{Height} exceeds the limit.");

            //ファイルのメモリの上で直接展開する(位置は source の中の位置)
            var reader = new PsdBinaryReader(new MemoryStream(source, 0, source.Length, writable: false, publiclyVisible: true), limits);
            var images = new ChannelImageData[used.Count];
            for (int i = 0; i < used.Count; i++)
                images[i] = used[i].CreateImage(reader, i < colorCount ? (ChannelId)i : ChannelId.TransparencyMask);
            if (ImageMode != ColorMode.Indexed)
                return new LayerImage(images, Width, Height, (int)depth, ImageMode, isPSB, limits, whiteMatte: false).Read();

            var table = colorTable!;
            var buffer = new byte[elements];
            var row = new byte[Width];
            try
            {
                for (int y = 0; y < Height; y++)
                {
                    try
                    {
                        images[0].ReadLine(row, y);
                    }
                    catch (Exception ex) when (PsdFile.IsBrokenData(ex))
                    {
                        throw new InvalidPsdDataException($"The pattern image is broken: {ex.Message}", ex);
                    }
                    var dst = buffer.AsSpan(y * Width * 4, Width * 4);
                    for (int x = 0; x < row.Length; x++)
                    {
                        var index = row[x] * 3;
                        dst[x * 4] = table[index + 2];
                        dst[x * 4 + 1] = table[index + 1];
                        dst[x * 4 + 2] = table[index];
                        dst[x * 4 + 3] = byte.MaxValue;
                    }
                }
            }
            finally
            {
                //ZIP の展開の途中の状態を閉じる(この呼び出しだけのチャンネル)
                images[0].Close();
            }
            return buffer;
        }

        //配列の深さ(2つの欄)と大きさが、色の配列・パターンと同じか。違えば壊れたデータ
        void CheckChannel(PatternChannel channel, uint depth)
        {
            if (channel.Depth != depth || channel.PixelDepth != depth)
                throw new InvalidPsdDataException($"Pattern channel {channel.Index} has bit depth {channel.Depth} ({channel.PixelDepth}), expected {depth}.");
            if (channel.Width != Width || channel.Height != Height)
                throw new InvalidPsdDataException($"Pattern channel size {channel.Width}x{channel.Height} does not match the pattern size {Width}x{Height}.");
        }
    }

    //VMAL の配列1つ(書かれているもの)。データは、圧縮の種類のあとからの dataLength バイト
    public sealed class PatternChannel
    {
        readonly int dataOffset;

        //VMAL の中の番号(0 から。チャンネルの数のあとの2つは、仕様書ではユーザーマスクとシートマスク)
        public int Index { get; }
        public uint Depth { get; }
        public int Top { get; }
        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        //仕様書の「Pixel depth」(1・8・16・32)。Read() は Depth と同じ値のときだけ読む
        public ushort PixelDepth { get; }
        //0 = 生、1 = RLE(行ごとのバイト数の表は2バイト)、2 = ZIP、3 = 予測付きの ZIP
        public ChannelImageDataCompression Compression { get; }
        public int DataLength { get; }

        internal PatternChannel(int index, uint depth, (int Top, int Left, int Bottom, int Right) rect, ushort pixelDepth, ChannelImageDataCompression compression, int dataOffset, int dataLength)
        {
            Index = index;
            Depth = depth;
            (Top, Left, Bottom, Right) = rect;
            PixelDepth = pixelDepth;
            Compression = compression;
            this.dataOffset = dataOffset;
            DataLength = dataLength;
        }

        //データの範囲を確かめて、チャンネルのデータ(1行ずつ展開する部品)にする。
        //深さと大きさは呼ぶ側(Pattern.Read)で確かめてある
        internal ChannelImageData CreateImage(PsdBinaryReader reader, ChannelId id)
        {
            var rowLength = ((long)Width * Depth + 7) / 8;
            if (rowLength > Array.MaxLength)
                throw new InvalidPsdDataException($"Pattern channel row ({rowLength} bytes) is too long.");
            var rowBytes = (int)rowLength;
            switch (Compression)
            {
                case ChannelImageDataCompression.RawData:
                    if (rowLength * Height > DataLength)
                        throw new InvalidPsdDataException($"Pattern channel data ({DataLength} bytes) is too short for {Width}x{Height}.");
                    return new ChannelImageData(reader, id, Width, Height, (int)Depth, Compression, dataOffset, [], 0, 0, Height);
                case ChannelImageDataCompression.RLE:
                    {
                        //行ごとのバイト数の表と、行の圧縮データ。表は PSB の文書でも2バイトとして読む(psd-tools はパターンの配列を、
                        //ファイルの版によらず版 1(2バイト)で展開する。レイヤーのチャンネルは PSB で4バイト。PSB の実物では確かめていない)。
                        //行の最小の長さは、レイヤーのチャンネルと同じ(中身が空の行を並べた小さなデータで、巨大な画像を確保させない)
                        long tableLength = Height * 2L;
                        if (tableLength > DataLength)
                            throw new InvalidPsdDataException($"Pattern channel RLE byte counts ({tableLength} bytes) do not fit in the data ({DataLength} bytes).");
                        var minRow = rowBytes == 0 ? 0 : 2 * ((rowBytes + 127) / 128);
                        var offsets = new long[Height + 1];
                        long offset = 0;
                        var table = reader.UnderlyingBuffer!.AsSpan(dataOffset, (int)tableLength);
                        for (int y = 0; y < Height; y++)
                        {
                            int count = BinaryPrimitives.ReadUInt16BigEndian(table[(y * 2)..]);
                            if (count < minRow)
                                throw new InvalidPsdDataException($"RLE byte count {count} of pattern row {y} is too short for a row of {rowBytes} bytes.");
                            offsets[y] = offset;
                            offset += count;
                        }
                        offsets[Height] = offset;
                        if (offset > DataLength - tableLength)
                            throw new InvalidPsdDataException($"Pattern channel RLE data ({offset} bytes) does not fit in the data ({DataLength - tableLength} bytes).");
                        return new ChannelImageData(reader, id, Width, Height, (int)Depth, Compression, dataOffset + tableLength, offsets, 0, 0, Height);
                    }
                case ChannelImageDataCompression.ZipWithoutPrediction or ChannelImageDataCompression.ZipWithPrediction:
                    //展開の大きさと検査値はチャンネルのデータの部品が確かめる(レイヤーのチャンネルと同じ)
                    return new ChannelImageData(reader, id, Width, Height, (int)Depth, Compression, dataOffset, [], DataLength, 0, Height);
                default:
                    throw new InvalidPsdDataException($"Unknown pattern compression {(int)Compression}.");
            }
        }
    }
}
