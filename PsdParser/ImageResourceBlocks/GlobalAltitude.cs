namespace PsdParser.ImageResourceBlocks
{
    //包括光源の高度(リソース 1049)。レイヤースタイルのうち、「包括光源を使用」にしたベベルとエンボスの高度(度)。
    //仕様書には既定値の記載がないので、リソースがなければ Photoshop の既定の 30 とみなす(PsdFile.GlobalLightAltitude)
    public class GlobalAltitude : ImageResourceBlock
    {
        //読めなければ(4 バイトより短い) null
        public int? Altitude { get; private set; }

        internal GlobalAltitude(PsdBinaryReader reader, ImageResourceBlockId id, string name, uint dataSize) : base(reader, id, name, dataSize)
        {
        }

        private protected override void Load(PsdBinaryReader reader, uint dataSize)
        {
            var start = reader.BaseStream.Position;
            if (dataSize >= 4)
                Altitude = reader.ReadInt32();
            reader.BaseStream.Position = start + dataSize;
        }
    }
}
