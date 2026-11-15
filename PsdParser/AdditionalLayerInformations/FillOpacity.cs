namespace PsdParser.AdditionalLayerInformations
{
    //塗りの不透明度(iOpa)。0〜255。レイヤーの不透明度と違い、レイヤースタイル(効果)にはかからない
    public class FillOpacity : AdditionalLayerInformation
    {
        //データが空なら null(ほかのソフトが書いた壊れた値で、ファイルを開けなくしないため)
        public byte? Value { get; private set; }

        internal FillOpacity(PsdBinaryReader reader, long length) : base(reader, AdditionalLayerInformationKey.FillOpacity, length)
        {
        }

        private protected override void Load(PsdBinaryReader reader, long length)
        {
            if (length >= 1)
                Value = reader.ReadByte();
        }
    }
}
