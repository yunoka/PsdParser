namespace PsdParser.ImageResourceBlocks
{
    //包括光源の角度(リソース 1037)。レイヤースタイルのうち、「包括光源を使用」にした効果(ドロップシャドウなど)の角度。
    //仕様書では 0〜359 の整数で、リソースがなければ 30 とみなす(PsdFile.GlobalLightAngle)
    public class GlobalAngle : ImageResourceBlock
    {
        //読めなければ(4 バイトより短い) null
        public int? Angle { get; private set; }

        internal GlobalAngle(PsdBinaryReader reader, ImageResourceBlockId id, string name, uint dataSize) : base(reader, id, name, dataSize)
        {
        }

        private protected override void Load(PsdBinaryReader reader, uint dataSize)
        {
            var start = reader.BaseStream.Position;
            if (dataSize >= 4)
                Angle = reader.ReadInt32();
            reader.BaseStream.Position = start + dataSize;
        }
    }
}
