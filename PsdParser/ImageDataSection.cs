namespace PsdParser
{
    //ファイルの最後の節(Image Data)。全レイヤーを合成した1枚の画像(Photoshop の「互換性を優先」で保存したときに本物になる。
    //本物かは PsdFile.HasRealMergedData)。データはチャンネルごとに続けて並ぶ(全画素の1つめのチャンネル、次に2つめ、…)
    public class ImageDataSection
    {
        public ChannelImageDataCompression Compression { get; }
        //ファイルのすべてのチャンネル(色、透明度、そのほかの余分なチャンネル)
        public ChannelImageData[] ChannelImages { get; }
        //合成済みの画像。Read()・ReadRgba16()・ReadRgbaFloat() で読む(レイヤーの画像と同じ)
        public LayerImage Image { get; }

        //hasTransparency: 最初の余分なチャンネル(色のチャンネルの次)が、合成済みの画像の透明度か(レイヤーの数が負のとき。仕様書の Layer info)
        internal ImageDataSection(PsdBinaryReader reader, FileHeaderSection header, bool hasTransparency)
        {
            bool isPSB = header.Version is 2;
            int width = header.Width, height = header.Height, depth = header.Depth, channels = header.Channels;
            Compression = (ChannelImageDataCompression)reader.ReadUInt16();
            var rowBytes = ((long)width * depth + 7) / 8;
            if (rowBytes > Array.MaxLength)
                throw new InvalidPsdDataException($"Image row ({rowBytes} bytes) is too long.");
            //色のチャンネルの数(その次が透明度になりうる)。マルチチャンネルは、すべてインクのチャンネルなので透明度を持たない
            int colors = header.ColorMode switch
            {
                ColorMode.RGB or ColorMode.Lab => 3,
                ColorMode.CMYK => 4,
                ColorMode.Multichannel => channels,
                _ => 1,
            };

            var positions = new long[channels];
            var rleOffsets = new long[channels][];
            long zipLength = 0;
            switch (Compression)
            {
                case ChannelImageDataCompression.RawData:
                    {
                        var size = rowBytes * height;
                        reader.EnsureAvailable(size * channels, "Image data");
                        for (int c = 0; c < channels; c++)
                            positions[c] = reader.BaseStream.Position + size * c;
                        break;
                    }
                case ChannelImageDataCompression.RLE:
                    {
                        //すべてのチャンネルの、すべての行のバイト数(PSD は2バイト、PSB は4バイト。仕様書は PSB の幅を書いていないが、
                        //レイヤーのチャンネルと同じに読む)。そのあとに圧縮した行が、チャンネルの順に続く
                        var countSize = isPSB ? 4 : 2;
                        reader.EnsureAvailable((long)channels * height * countSize, "RLE byte counts");
                        var minRow = rowBytes == 0 ? 0 : 2 * ((rowBytes + 127) / 128);
                        //チャンネルごとの行の位置(累積)を、表を読みながら作る(表を別に写して持たない)
                        long total = 0;
                        for (int c = 0; c < channels; c++)
                        {
                            var offsets = new long[height + 1];
                            long offset = 0;
                            for (int y = 0; y < height; y++)
                            {
                                long count = isPSB ? reader.ReadInt32() : reader.ReadUInt16();
                                if (count < minRow)
                                    throw new InvalidPsdDataException($"RLE byte count {count} of image row {y} (channel {c}) is too short for a row of {rowBytes} bytes.");
                                offsets[y] = offset;
                                offset += count;
                            }
                            offsets[height] = offset;
                            rleOffsets[c] = offsets;
                            positions[c] = total; //表のあとの位置からの相対。下で絶対の位置に直す
                            total += offset;
                        }
                        var dataStart = reader.BaseStream.Position;
                        for (int c = 0; c < channels; c++)
                            positions[c] += dataStart;
                        reader.EnsureAvailable(total, "RLE compressed image data");
                        break;
                    }
                case ChannelImageDataCompression.ZipWithoutPrediction or ChannelImageDataCompression.ZipWithPrediction:
                    //ZIP は、全チャンネルが1つの zlib のストリームに続けて入る。ストリームはファイルの終わりまで
                    //チャンネルごとに、ストリームの先頭から展開して自分の行まで進む(チャンネルの数に比例して展開の手間が増えるが、
                    //Photoshop は合成済みの画像を ZIP で保存しないので、まれな形として受け入れる)。
                    //zlib の検査値は、ストリームの最後の行(最後のチャンネル)まで読んだときに確かめる
                    zipLength = reader.BaseStream.Length - reader.BaseStream.Position;
                    for (int c = 0; c < channels; c++)
                        positions[c] = reader.BaseStream.Position;
                    break;
                default:
                    //仕様にない値(壊れたデータ)
                    throw new InvalidPsdDataException($"Unknown image data compression: {(int)Compression}");
            }

            ChannelImages = new ChannelImageData[channels];
            for (int c = 0; c < channels; c++)
            {
                //色のチャンネルは 0〜(色の数 − 1)。その次は、透明度があれば透明度(-1)。ほかの余分なチャンネルは番号のまま(画像には使わない)
                var id = c < colors ? (ChannelId)c : c == colors && hasTransparency ? ChannelId.TransparencyMask : (ChannelId)c;
                ChannelImages[c] = new ChannelImageData(reader, id, width, height, depth, Compression, positions[c], rleOffsets[c] ?? [],
                    zipLength, c * height, channels * height);
            }
            Image = new LayerImage(ChannelImages, width, height, depth, header.ColorMode, isPSB, reader.Limits);
        }
    }
}
