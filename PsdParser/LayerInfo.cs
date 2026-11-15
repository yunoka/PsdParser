namespace PsdParser
{
    public class LayerInfo
    {
        public LayerRecordAndImage[] Items { get; }
        public bool HasTransparencyData { get; }

        internal LayerInfo(PsdBinaryReader reader, bool isPSB, int depth, ColorMode colorMode)
        {
            var position = reader.BaseStream.Position;
            var length = isPSB ? reader.ReadInt64() : reader.ReadUInt32();
            var lengthSize = isPSB ? 8 : 4;
            reader.EnsureAvailable(length, "Layer info");

            var layerCount = reader.ReadInt16();
            HasTransparencyData = layerCount < 0;
            var records = new LayerRecord[Math.Abs((int)layerCount)];//LayerCountは負の値を取ることがある。intで求める(shortの最小値はshortの絶対値にできない)
            var images = new LayerImage[records.Length];
            for (int i = 0; i < records.Length; i++)
                records[i] = new LayerRecord(reader, isPSB);
            //チャンネルのデータは、このレイヤーの情報の中に収まること(越えて、ほかのセクションを画素として読まないように)
            var sectionEnd = position + lengthSize + length;
            for (int i = 0; i < records.Length; i++)
                images[i] = new LayerImage(reader, records[i], isPSB, depth, colorMode, sectionEnd);

            Items = new LayerRecordAndImage[records.Length];
            for(int i = 0; i < records.Length; i++)
                Items[i] = new LayerRecordAndImage(records[i], images[i]);

            //残りが詰め物だけなら終わりまで進める(上流と同じ)
            if (position + lengthSize + length - reader.BaseStream.Position <= 4)
                reader.BaseStream.Position = position + lengthSize + length;

            InvalidStreamPositionException.ThrowIfInvalid(reader, position, lengthSize + length);
        }
        internal LayerInfo()
        {
            Items = [];
        }
    }
}