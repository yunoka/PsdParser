namespace PsdParser
{
    public class LayerInfo
    {
        public LayerRecordAndImage[] Items { get; }
        public bool HasTransparencyData { get; }
        //レイヤーの情報の節の長さが0だったか(16・32ビットの文書では、レイヤーは追加情報の Lr16・Lr32 に入る)
        internal bool IsEmptySection { get; }

        internal LayerInfo(PsdBinaryReader reader, bool isPSB, int depth, ColorMode colorMode)
        {
            var position = reader.BaseStream.Position;
            var length = isPSB ? reader.ReadInt64() : reader.ReadUInt32();
            var lengthSize = isPSB ? 8 : 4;
            reader.EnsureAvailable(length, "Layer info");
            var sectionEnd = position + lengthSize + length;

            //長さが0なら、レイヤーはない(16・32ビットの文書では、レイヤーは追加情報の Lr16・Lr32 に入る)
            if (length == 0)
            {
                Items = [];
                IsEmptySection = true;
            }
            else
            {
                (Items, HasTransparencyData) = ReadBody(reader, isPSB, depth, colorMode, sectionEnd);
                //残りが詰め物だけなら終わりまで進める(上流と同じ)
                if (sectionEnd - reader.BaseStream.Position <= 4)
                    reader.BaseStream.Position = sectionEnd;
            }

            InvalidStreamPositionException.ThrowIfInvalid(reader, position, lengthSize + length);
        }

        //追加情報の Lr16・Lr32 に入ったレイヤーの情報を読む。中身は、レイヤーの情報から先頭の長さの欄を除いたもの(レイヤーの数から始まる)。
        //Photoshop は16・32ビットの文書を保存するとき、レイヤーの情報の節を空にして、レイヤーをここに入れる
        //(仕様書には書かれていない。psd-tools の LayerInfoBlock と同じ読み方)。blockEnd は追加情報のデータの終わり
        internal LayerInfo(PsdBinaryReader reader, bool isPSB, int depth, ColorMode colorMode, long blockEnd)
        {
            (Items, HasTransparencyData) = ReadBody(reader, isPSB, depth, colorMode, blockEnd);
        }

        //レイヤーの数から、レイヤーの記録とチャンネルのデータまでを読む。レイヤーの記録とチャンネルのデータは end を越えないこと
        static (LayerRecordAndImage[] Items, bool HasTransparencyData) ReadBody(PsdBinaryReader reader, bool isPSB, int depth, ColorMode colorMode, long end)
        {
            var layerCount = reader.ReadInt16();
            var records = new LayerRecord[Math.Abs((int)layerCount)];//LayerCountは負の値を取ることがある。intで求める(shortの最小値はshortの絶対値にできない)
            var images = new LayerImage[records.Length];
            for (int i = 0; i < records.Length; i++)
                records[i] = new LayerRecord(reader, isPSB);
            if (reader.BaseStream.Position > end)
                throw new InvalidPsdDataException($"Layer records ({reader.BaseStream.Position - end} bytes) run past the end of the layer info.");
            //チャンネルのデータは、このレイヤーの情報の中に収まること(越えて、ほかのセクションを画素として読まないように)
            for (int i = 0; i < records.Length; i++)
                images[i] = new LayerImage(reader, records[i], isPSB, depth, colorMode, end);

            var items = new LayerRecordAndImage[records.Length];
            for (int i = 0; i < records.Length; i++)
                items[i] = new LayerRecordAndImage(records[i], images[i]);
            return (items, layerCount < 0);
        }
        internal LayerInfo()
        {
            Items = [];
        }
    }
}