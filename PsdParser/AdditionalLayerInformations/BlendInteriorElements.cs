namespace PsdParser.AdditionalLayerInformations
{
    //「内部効果をグループとして合成」(infx)。オンなら、内側の効果(内側の光・サテン・オーバーレイ)もレイヤーの描画モードと塗りの不透明度で一緒に合成する
    public class BlendInteriorElements : AdditionalLayerInformation
    {
        //データが空なら null(ほかのソフトが書いた壊れた値で、ファイルを開けなくしないため)
        public bool? Value { get; private set; }

        internal BlendInteriorElements(PsdBinaryReader reader, long length) : base(reader, AdditionalLayerInformationKey.BlendInteriorElements, length)
        {
        }

        private protected override void Load(PsdBinaryReader reader, long length)
        {
            if (length >= 1)
                Value = reader.ReadByte() != 0;
        }
    }
}
