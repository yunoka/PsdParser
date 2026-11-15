namespace PsdParser
{
    public class LayerRecord
    {
        public int Top { get; }
        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }

        public ushort Channels { get; }
        public ChannelInformation[] ChannelInfos { get; }

        public BlendMode BlendMode { get; }
        public byte Opacity { get; }
        public bool Clipping { get; }
        public LayerFlags LayerFlags { get; }
        public uint ExtraDataLength { get; }
        public LayerMaskAndAdjustmentLayerData LayerMaskAndAdjustmentLayerData { get; }
        public LayerBlendingRangesData LayerBlendingRangesData { get; }
        public string LayerName { get; }

        public AdditionalLayerInformation[] AdditionalLayerInformations { get; }

        internal LayerRecord(PsdBinaryReader reader, bool isPSB)
        {
            Top = reader.ReadInt32();
            Left = reader.ReadInt32();
            Bottom = reader.ReadInt32();
            Right = reader.ReadInt32();
            //レイヤーはキャンバスからはみ出してよい(仕様の上限より大きいこともある)ので、ここでは幅・高さが 0 以上で 32 ビットに収まることだけを
            //確かめる。大きさの上限(仕様と呼び出し側)は、そのレイヤーを画像にするとき(LayerImage.Read)に確かめる。チャンネルのデータの
            //大きさは、ファイルとセクションに収まるかで確かめる(ChannelImageData)ので、大きな値で巨大な確保はしない
            reader.EnsureNonNegativeSize((long)Right - Left, (long)Bottom - Top, "Layer");

            Channels = reader.ReadUInt16();
            ChannelInfos = new ChannelInformation[Channels];
            for (int i = 0; i < Channels; i++)
                ChannelInfos[i] = new ChannelInformation(reader, isPSB);

            var signature = new string(reader.ReadChars(4));
            if (signature != "8BIM")
                throw new InvalidSignatureException(signature);

            BlendMode = (BlendMode)reader.ReadInt32();
            Opacity = reader.ReadByte();
            Clipping = reader.ReadByte() is 1;
            LayerFlags = (LayerFlags)reader.ReadByte();
            reader.BaseStream.Position += 1;

            ExtraDataLength = reader.ReadUInt32();
            reader.EnsureAvailable(ExtraDataLength, "Layer extra data");
            var extraDataPosition = reader.BaseStream.Position;

            LayerMaskAndAdjustmentLayerData = new LayerMaskAndAdjustmentLayerData(reader, isPSB);
            LayerBlendingRangesData = new LayerBlendingRangesData(reader);
            LayerName = reader.ReadPascalString(4);

            var maxPadding = AdditionalLayerInformation.MinSize(isPSB);
            var additional = new List<AdditionalLayerInformation>();
            while (maxPadding <= extraDataPosition + ExtraDataLength - reader.BaseStream.Position)
                additional.Add(AdditionalLayerInformation.Parse(reader, isPSB));
            AdditionalLayerInformations = [.. additional];
            //残りが詰め物だけなら終わりまで進める(追加情報のあとの詰め物を読み飛ばすときに数バイト読みすぎることがあり、そのときも戻して合わせる。上流と同じ)
            if (extraDataPosition + ExtraDataLength - reader.BaseStream.Position < maxPadding)
                reader.BaseStream.Position = extraDataPosition + ExtraDataLength;

            InvalidStreamPositionException.ThrowIfInvalid(reader,extraDataPosition,ExtraDataLength);
        }
    }
}