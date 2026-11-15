namespace PsdParser.ImageResourceBlocks
{
    //版の情報(リソース 1057)。合成済みの画像が本物か(Photoshop の「互換性を優先」で保存したか)を持つ
    public class VersionInfo : ImageResourceBlock
    {
        public uint Version { get; private set; }
        //false なら、ファイルの最後の合成済みの画像は本物ではない(白などで埋めたもの)。見た目はレイヤーから組み立てる必要がある。
        //リソースが短くて読めなければ null
        public bool? HasRealMergedData { get; private set; }

        internal VersionInfo(PsdBinaryReader reader, ImageResourceBlockId id, string name, uint dataSize) : base(reader, id, name, dataSize)
        {
        }

        private protected override void Load(PsdBinaryReader reader, uint dataSize)
        {
            var start = reader.BaseStream.Position;
            //任意の情報なので、短すぎても拒否しない(ほかのソフトが書いたファイルを開けなくしないため)
            if (dataSize >= 5)
            {
                Version = reader.ReadUInt32();
                HasRealMergedData = reader.ReadByte() != 0;
            }
            //残り(書いたソフトと読むソフトの名前、ファイルの版)は使わない
            reader.BaseStream.Position = start + dataSize;
        }
    }
}
