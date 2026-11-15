using System.Buffers.Binary;
using System.IO.Compression;

namespace PsdParser
{
    public class ChannelImageData
    {
        readonly long position;
        readonly PsdBinaryReader reader;
        readonly int width, height, depth;
        //1行のバイト数(幅×ビット数/8 を切り上げ)。コンストラクタで1回だけ求める
        readonly int rowBytes;
        //各スキャンラインのRLEデータ先頭オフセット(positionからの相対バイト数)を事前計算したもの。末尾に全体の長さを置き、
        //行の長さは隣との差で求める(ReadLineで毎回行の長さを足し合わせるとO(height^2)になるため累積和をここに持つ)。
        readonly long[] rleOffsets = [];
        //RLE生バイトの読み込み用スクラッチ。行ごとにReadBytesで確保し直さず使い回す。
        readonly byte[] rleScratch = [];
        //ZIP圧縮のデータの長さ(圧縮したまま)。展開は行の順に1行ずつ行い、チャンネル全体を確保しない
        readonly long zipLength;
        //ZIP のストリームの中で、このチャンネルの最初の行の前にある行の数と、ストリーム全体の行の数
        //(レイヤーのチャンネルは1チャンネルで1つのストリーム。合成済みの画像は、全チャンネルが1つのストリームに続けて入る)
        readonly int zipRowOffset, zipStreamRows;
        //公開の ReadLine が使う展開の途中の状態(LayerImage.Read は呼ぶたびに自分の状態を持つ)
#if NET9_0_OR_GREATER
        readonly Lock zipLock = new();
#else
        readonly object zipLock = new(); //System.Threading.Lock は .NET 9 から
#endif
        readonly ZipCursor sharedCursor = new();
        volatile bool closed;

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
            var rowLength = ((long)width * depth + 7) / 8;
            if (rowLength > Array.MaxLength)
                throw new InvalidPsdDataException($"Channel row ({rowLength} bytes) is too long.");
            rowBytes = (int)rowLength;
            zipStreamRows = height;

            long size;
            if (Compression is ChannelImageDataCompression.RawData)
            {
                position = reader.BaseStream.Position;
                size = (long)rowBytes * height;
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
            else if (Compression is ChannelImageDataCompression.ZipWithoutPrediction or ChannelImageDataCompression.ZipWithPrediction)
            {
                //ZIP(zlib)圧縮。圧縮したデータはチャンネルの情報に書かれた長さ(圧縮の種類の2バイトを除く)いっぱいにある
                position = reader.BaseStream.Position;
                if (declaredLength < 2)
                    throw new InvalidPsdDataException($"Channel data length {declaredLength} is too short for ZIP compressed data.");
                zipLength = declaredLength - 2;
                reader.EnsureAvailable(zipLength, "ZIP compressed data");
                reader.BaseStream.Position += zipLength;
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
        //合成済みの画像(Image Data の節)のチャンネル。圧縮の種類は節に1つで、位置と RLE の行の位置は ImageDataSection が求めて渡す。
        //ZIP のときは、全チャンネルが1つのストリームに続けて入っている(zipRowOffset はこのチャンネルの前の行の数)
        internal ChannelImageData(PsdBinaryReader reader, ChannelId channelId, int width, int height, int depth, ChannelImageDataCompression compression,
            long position, long[] rleOffsets, long zipLength, int zipRowOffset, int zipStreamRows)
        {
            this.reader = reader;
            this.width = width;
            this.height = height;
            this.depth = depth;
            rowBytes = (int)(((long)width * depth + 7) / 8); //ImageDataSection が範囲を確かめてある
            Compression = compression;
            ChannelId = channelId;
            this.position = position;
            this.rleOffsets = rleOffsets;
            this.zipLength = zipLength;
            this.zipRowOffset = zipRowOffset;
            this.zipStreamRows = zipStreamRows;
            if (compression is ChannelImageDataCompression.RLE && reader.UnderlyingBuffer is null)
            {
                long maxLine = 0;
                for (int i = 0; i < height; i++)
                    maxLine = Math.Max(maxLine, rleOffsets[i + 1] - rleOffsets[i]);
                rleScratch = new byte[maxLine];
            }
        }

        //1行を展開してbufferに書く。bufferは1行のバイト数(幅×ビット数/8)ちょうどの長さであること。
        //範囲を確かめる書き方(Span)にして、壊れたファイル・細工したファイルでも範囲の外を読み書きしない
        //ファイルをメモリに読んでいないとき(PsdFile は常にメモリに読むので、今は起きない)は、生のデータと RLE はファイルのストリームの位置を
        //動かすので、複数のスレッドから同時に読まないこと
        public void ReadLine(byte[] buffer, int y)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if ((uint)y >= (uint)height)
                throw new ArgumentOutOfRangeException(nameof(y), $"Row {y} is outside the channel (height {height}).");
            if(Compression is ChannelImageDataCompression.RawData)
            {
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
            else if (Compression is ChannelImageDataCompression.ZipWithoutPrediction or ChannelImageDataCompression.ZipWithPrediction)
            {
                lock (zipLock)
                    ReadLine(buffer, y, sharedCursor);
            }
            else
            {
                throw new NotSupportedException($"Not supported compression: {Compression}");
            }
        }

        //1行を読む(LayerImage.Read が使う)。ZIP のときは、呼ぶ側が持つ展開の途中の状態(cursor)を使う
        //(同じチャンネルを複数のスレッドで同時に画像にしても、互いの展開の状態を壊さないように)
        internal void ReadLine(byte[] buffer, int y, ZipCursor cursor)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (Compression is ChannelImageDataCompression.ZipWithoutPrediction or ChannelImageDataCompression.ZipWithPrediction)
            {
                if ((uint)y >= (uint)height)
                    throw new ArgumentOutOfRangeException(nameof(y), $"Row {y} is outside the channel (height {height}).");
                if (buffer.Length < rowBytes)
                    throw new ArgumentException($"The buffer ({buffer.Length} bytes) is shorter than a row ({rowBytes} bytes).", nameof(buffer));
                ReadZipLine(buffer.AsSpan(0, rowBytes), y, cursor);
            }
            else
            {
                ReadLine(buffer, y);
            }
        }

        //ZIP圧縮のデータを、行の順に1行ずつ展開する(チャンネル全体を確保しない。使うメモリは行の数によらない)。
        //前の行に戻るときは、先頭から展開し直す(LayerImage.Read は上から順に読むので、戻ることはない)。
        //最後の行まで展開したら、展開したデータから求めた Adler-32 を、圧縮データの最後の4バイト(zlib の検査値)と照らし合わせる
        //(.NET の ZLibStream は、検査値が欠けていても例外を出さないため)。予測付きなら、行ごとの差分を戻す
        void ReadZipLine(Span<byte> row, int y, ZipCursor c)
        {
            bool prediction = Compression is ChannelImageDataCompression.ZipWithPrediction;
            if (prediction && depth is not (8 or 16 or 32))
                throw new NotSupportedException($"ZIP with prediction is not supported for bit depth {depth}.");
            //PsdFile は常にファイルをメモリに読む。ZIP はメモリの上のデータだけを扱う(ファイルのストリームを複数のチャンネルで共有して動かさないため)
            var mem = reader.UnderlyingBuffer ?? throw new NotSupportedException("ZIP compressed channels can only be read from a file in memory.");
            try
            {
                //ストリームの中の行(合成済みの画像では、前のチャンネルの行のあとに続く)
                int streamRow = zipRowOffset + y;
                if (c.Zip is null || streamRow < c.NextRow)
                {
                    //deflate は 1 バイトから最大でおよそ 1032 バイトにしか展開できない。それを越える大きさを言う圧縮データは壊れているか細工したもの
                    //(展開しきれないことが、多くの行を展開してからわかるのを避ける。RLE の行の最小の長さと同じ考え)。
                    //ファイルを開くときではなくここで確かめる(使わないチャンネルが壊れていても、ファイルとほかのチャンネルは読めるように)
                    if ((long)rowBytes * zipStreamRows > (zipLength + 1) * 1032)
                        throw new InvalidPsdDataException($"ZIP compressed channel data ({zipLength} bytes) is too short for an image of {(long)rowBytes * zipStreamRows} bytes.");
                    c.Reset();
                    c.Zip = new ZLibStream(new MemoryStream(mem, checked((int)position), checked((int)zipLength), writable: false), CompressionMode.Decompress);
                }
                if ((streamRow > c.NextRow || (prediction && depth == 32)) && c.Scratch.Length < rowBytes)
                    c.Scratch = new byte[rowBytes];
                while (c.NextRow < streamRow)
                {
                    var skipped = c.Scratch.AsSpan(0, rowBytes);
                    c.Zip.ReadExactly(skipped);
                    c.Update(skipped);
                    c.NextRow++;
                }
                c.Zip.ReadExactly(row);
                c.Update(row);
                c.NextRow++;
                if (c.NextRow == zipStreamRows)
                {
                    //画像より多いデータも壊れたものとして扱う
                    Span<byte> extra = stackalloc byte[1];
                    if (c.Zip.Read(extra) != 0)
                        throw new InvalidPsdDataException("ZIP compressed channel data decodes to more bytes than the image.");
                    if (!EndsWithChecksum(mem.AsSpan(checked((int)position), checked((int)zipLength)), c.Adler))
                        throw new InvalidPsdDataException("ZIP compressed channel data has a wrong or missing checksum.");
                    c.Reset();
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or InvalidPsdDataException)
            {
                c.Reset();
                if (ex is InvalidPsdDataException)
                    throw;
                throw new InvalidPsdDataException($"ZIP compressed channel data is broken or too short: {ex.Message}");
            }
            if (prediction)
                UndoPrediction(row, c.Scratch);
        }

        //圧縮データが zlib の検査値(ビッグエンディアンの4バイト)で終わるか。そのあとの詰め物(0)は16バイトまで許す
        //(Photoshop は詰め物を書かないが、ほかの書き出し元が偶数にそろえるなどで足すことがありうるため)
        static bool EndsWithChecksum(ReadOnlySpan<byte> data, uint adler)
        {
            for (int pad = 0; pad <= 16 && pad + 4 <= data.Length; pad++)
            {
                if (pad > 0 && data[^pad] != 0)
                    return false; //詰め物は0だけ(後ろから見て、0でないバイトより前には検査値がある)
                if (BinaryPrimitives.ReadUInt32BigEndian(data[^(pad + 4)..^pad]) == adler)
                    return true;
            }
            return false;
        }

        //予測(行の中の隣との差分)を戻す。8ビットは1バイトずつ、16ビットはビッグエンディアンの2バイトずつ。
        //32ビットは、1行を4つのバイトの面(上位のバイトから)に分けて並べたものの差分なので、差分を戻してから1画素4バイトに並べ直す
        //scratch は 32 ビットのときの並べ直しの作業領域(1行分)
        void UndoPrediction(Span<byte> row, byte[] scratch)
        {
            switch (depth)
            {
                case 8:
                    for (int x = 1; x < row.Length; x++)
                        row[x] += row[x - 1];
                    break;
                case 16:
                    for (int x = 2; x + 1 < row.Length; x += 2)
                    {
                        var value = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(row[x..]) + BinaryPrimitives.ReadUInt16BigEndian(row[(x - 2)..]));
                        BinaryPrimitives.WriteUInt16BigEndian(row[x..], value);
                    }
                    break;
                case 32:
                    {
                        for (int x = 1; x < row.Length; x++)
                            row[x] += row[x - 1];
                        var planes = scratch.AsSpan(0, row.Length);
                        row.CopyTo(planes);
                        for (int i = 0; i < width; i++)
                            for (int k = 0; k < 4; k++)
                                row[i * 4 + k] = planes[k * width + i];
                        break;
                    }
                default:
                    throw new NotSupportedException($"ZIP with prediction is not supported for bit depth {depth}.");
            }
        }

        //PsdFile.Dispose が呼ぶ。公開の ReadLine が残した展開の途中の状態を閉じ、以後の読み込みを ObjectDisposedException にする
        internal void Close()
        {
            lock (zipLock)
            {
                closed = true;
                sharedCursor.Dispose();
            }
        }

        //ZIP の展開の途中の状態(次に展開する行と、そこまでの Adler-32)
        internal sealed class ZipCursor : IDisposable
        {
            internal ZLibStream? Zip;
            internal int NextRow;
            internal byte[] Scratch = []; //行を飛ばすときと、32ビットの予測を並べ直すときの作業領域(1行分。やり直しても使い回す)
            uint a = 1, b = 0;
            internal uint Adler => b << 16 | a;

            //展開したデータで Adler-32 を進める(RFC 1950。5552 バイトごとに剰余をとれば 32 ビットであふれない)
            internal void Update(ReadOnlySpan<byte> data)
            {
                const uint Mod = 65521;
                while (data.Length > 0)
                {
                    var n = Math.Min(data.Length, 5552);
                    foreach (var value in data[..n])
                    {
                        a += value;
                        b += a;
                    }
                    a %= Mod;
                    b %= Mod;
                    data = data[n..];
                }
            }

            //展開を閉じて、先頭からやり直せる状態にする
            internal void Reset()
            {
                Zip?.Dispose();
                Zip = null;
                NextRow = 0;
                a = 1;
                b = 0;
            }

            public void Dispose()
            {
                Reset();
                Scratch = [];
            }
        }
    }
}