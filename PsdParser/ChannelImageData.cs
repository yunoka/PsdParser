namespace PsdParser
{
    public class ChannelImageData
    {
        readonly long position;
        readonly PsdBinaryReader reader;
        readonly int width, height, depth;
        //各スキャンラインのRLEデータ先頭オフセット(positionからの相対バイト数)を事前計算したもの。末尾に全体の長さを置き、
        //行の長さは隣との差で求める(ReadLineで毎回行の長さを足し合わせるとO(height^2)になるため累積和をここに持つ)。
        readonly long[] rleOffsets = [];
        //RLE生バイトの読み込み用スクラッチ。行ごとにReadBytesで確保し直さず使い回す。
        readonly byte[] rleScratch = [];

        public ChannelImageDataCompression Compression { get; }
        public ChannelId ChannelId { get; }
        //declaredLength: チャンネルの情報に書かれたデータの長さ(圧縮の種類の2バイトを含む)。データがこの長さを越えて
        //隣のチャンネルやセクションに入り込まないことを確かめる
        internal ChannelImageData(PsdBinaryReader reader,ChannelId channelId, bool isPSB, int width, int height, int depth, long declaredLength)
        {
            var start = reader.BaseStream.Position;
            this.reader = reader;
            this.width = width;
            this.height = height;
            this.depth = depth;
            Compression = (ChannelImageDataCompression)reader.ReadUInt16();
            ChannelId = channelId;

            //1行のバイト数と全体のバイト数は64ビットで求める(32ビットではあふれうる)。
            //幅・高さはLayerRecord・LayerMaskAndAdjustmentLayerDataで上限を確かめてある
            if (width < 0 || height < 0)
                throw new InvalidPsdDataException($"Channel size {width}x{height} is out of range.");
            var rowBytes = ((long)width * depth + 7) / 8;
            if (rowBytes > Array.MaxLength)
                throw new InvalidPsdDataException($"Channel row ({rowBytes} bytes) is too long.");

            long size;
            if (Compression is ChannelImageDataCompression.RawData)
            {
                position = reader.BaseStream.Position;
                size = rowBytes * height;
                reader.EnsureAvailable(size, "Channel image data");
                reader.BaseStream.Position += size;
            }
            else if(Compression is ChannelImageDataCompression.RLE)
            {
                //行ごとのバイト数の表(PSDは2バイト、PSBは4バイト)。表そのものもファイルに収まること
                reader.EnsureAvailable((long)height * (isPSB ? 4 : 2), "RLE byte counts");
                //1行のPackBitsの圧縮データは、少なくとも 2 × ⌈1行のバイト数 ÷ 128⌉ バイトある(連長1回で最大128バイト)。
                //これより短い行を許すと、小さなファイルで巨大な画像を作らせられる(展開できるかは画像を確保してからわかるため)
                var minRow = rowBytes == 0 ? 0 : 2 * ((rowBytes + 127) / 128);
                //行の位置(先頭からの累積)だけを持つ。行の長さは隣との差で求める(行ごとに長さと位置の両方を持たない)
                rleOffsets = new long[height + 1];
                long offset = 0;
                int maxLine = 0;
                for (int i = 0; i < height; i++)
                {
                    var count = isPSB ? reader.ReadInt32() : reader.ReadUInt16();
                    if (count < minRow)
                        throw new InvalidPsdDataException($"RLE byte count {count} of row {i} is too short for a row of {rowBytes} bytes.");
                    rleOffsets[i] = offset;
                    offset += count;
                    if (count > maxLine) maxLine = count;
                }
                rleOffsets[height] = offset;
                position = reader.BaseStream.Position;
                //圧縮したデータ全体がファイルに収まること(行の位置・長さが範囲の外を指さないことの保証にもなる)
                reader.EnsureAvailable(offset, "RLE compressed data");
                //ファイルをメモリに読んでいれば直接読むので、行の読み込み用の作業領域は要らない
                if (reader.UnderlyingBuffer is null)
                    rleScratch = new byte[maxLine];
                reader.BaseStream.Position += offset;
            }
            else
            {
                throw new NotSupportedException($"Not supported compression: {Compression}");
            }

            //データがチャンネルの情報に書かれた長さに収まること(越えると、隣のチャンネルやセクションを画素として読んでしまう)
            if (reader.BaseStream.Position - start > declaredLength)
                throw new InvalidPsdDataException($"Channel data ({reader.BaseStream.Position - start} bytes) is longer than declared ({declaredLength} bytes).");
            reader.BaseStream.Position = start + declaredLength;
        }
        //1行を展開してbufferに書く。bufferは1行のバイト数(幅×ビット数/8)ちょうどの長さであること。
        //範囲を確かめる書き方(Span)にして、壊れたファイル・細工したファイルでも範囲の外を読み書きしない
        public void ReadLine(byte[] buffer, int y)
        {
            if ((uint)y >= (uint)height)
                throw new ArgumentOutOfRangeException(nameof(y), $"Row {y} is outside the channel (height {height}).");
            if(Compression is ChannelImageDataCompression.RawData)
            {
                int rowBytes = (int)(((long)width * depth + 7) / 8);
                if (buffer.Length < rowBytes)
                    throw new ArgumentException($"The buffer ({buffer.Length} bytes) is shorter than a row ({rowBytes} bytes).", nameof(buffer));
                long src = position + (long)rowBytes * y;
                var mem = reader.UnderlyingBuffer;
                if (mem is not null)
                {
                    mem.AsSpan(checked((int)src), rowBytes).CopyTo(buffer);
                }
                else
                {
                    reader.BaseStream.Position = src;
                    reader.BaseStream.ReadExactly(buffer, 0, rowBytes);
                }
            }
            else if(Compression is ChannelImageDataCompression.RLE)
            {
                int len = (int)(rleOffsets[y + 1] - rleOffsets[y]);
                long src = position + rleOffsets[y];
                var mem = reader.UnderlyingBuffer;
                ReadOnlySpan<byte> rle;
                if (mem is not null)
                {
                    //メモリ上のファイルバッファから直接デコード(スクラッチへのコピー不要)。範囲はコンストラクタで確かめてある
                    rle = mem.AsSpan(checked((int)src), len);
                }
                else
                {
                    reader.BaseStream.Position = src;
                    reader.BaseStream.ReadExactly(rleScratch, 0, len);
                    rle = rleScratch.AsSpan(0, len);
                }

                var dst = buffer.AsSpan();
                int di = 0;
                int si = 0;
                while (si < rle.Length)
                {
                    var count = (sbyte)rle[si];
                    if (count >= 0)
                    {
                        //リテラル: count+1 バイトをそのままコピー
                        int n = count + 1;
                        if (di + n > dst.Length || si + 1 + n > rle.Length) break; //破損データ保護(書き込み先と読み込み元の両方)
                        rle.Slice(si + 1, n).CopyTo(dst.Slice(di, n));
                        di += n;
                        si += n + 1;
                    }
                    else if (count != -128)
                    {
                        //連長: 次の1バイトを -count+1 回繰り返す
                        int n = -count + 1;
                        if (di + n > dst.Length || si + 1 >= rle.Length) break; //破損データ保護(書き込み先と読み込み元の両方)
                        dst.Slice(di, n).Fill(rle[si + 1]);
                        di += n;
                        si += 2;
                    }
                    else
                    {
                        //-128 は何もしない(PackBitsの仕様)
                        si += 1;
                    }
                }
                if (di != buffer.Length)
                    throw new InvalidPsdDataException($"RLE row {y} decodes to {di} bytes, expected {buffer.Length}.");
            }
            else
            {
                throw new NotSupportedException($"Not supported compression: {Compression}");
            }
        }
    }
}