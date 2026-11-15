namespace PsdParser
{
    public class LayerAndMaskInformationSection
    {
        public long Length { get; }
        public LayerInfo LayerInfo { get; }
        public GlobalLayerMaskInfo GlobalLayerMaskInfo { get; }
        public AdditionalLayerInformation[] AdditionalLayerInformations { get; }

        internal LayerAndMaskInformationSection(PsdBinaryReader reader, bool isPSB, int depth, ColorMode colorMode)
        {
            var position = reader.BaseStream.Position;

            Length = isPSB ? reader.ReadInt64() : reader.ReadUInt32();
            var lengthSize = isPSB ? 8 : 4;
            reader.EnsureAvailable(Length, "Layer and mask information section");
            if (Length > 0)
            {
                LayerInfo = new LayerInfo(reader, isPSB, depth, colorMode);

                if (reader.BaseStream.Position == position + lengthSize + Length)
                {
                    GlobalLayerMaskInfo = new GlobalLayerMaskInfo();
                    AdditionalLayerInformations = [];
                }
                else
                {
                    GlobalLayerMaskInfo = new GlobalLayerMaskInfo(reader);

                    var maxPadding = AdditionalLayerInformation.MinSize(isPSB);
                    var additionalInfos = new List<AdditionalLayerInformation>();
                    while (maxPadding <= position + lengthSize + Length - reader.BaseStream.Position)
                        additionalInfos.Add(AdditionalLayerInformation.Parse(reader, isPSB, position + lengthSize + Length));
                    AdditionalLayerInformations = [.. additionalInfos];
                    //残りが詰め物だけなら終わりまで進める(上流と同じ)
                    if (position + lengthSize + Length - reader.BaseStream.Position < maxPadding)
                        reader.BaseStream.Position = position + lengthSize + Length;

                    //16・32ビットの文書では、レイヤーの情報の節は空(長さ0)で、レイヤーは追加情報の Lr16・Lr32 に入っている。
                    //ビット数に合うほうを先に探し、なければもう一方を使う(psd-tools と同じく両方を見る)。
                    //壊れていれば、ふつうのレイヤーの情報が壊れているときと同じく、ファイルを開けない
                    var (first, second) = depth == 32
                        ? (AdditionalLayerInformationKey.Lr32, AdditionalLayerInformationKey.Lr16)
                        : (AdditionalLayerInformationKey.Lr16, AdditionalLayerInformationKey.Lr32);
                    var block = AdditionalLayerInformations.FirstOrDefault(i => i.Key == first) ?? AdditionalLayerInformations.FirstOrDefault(i => i.Key == second);
                    if (LayerInfo.IsEmptySection && block is not null)
                    {
                        var end = reader.BaseStream.Position;
                        reader.BaseStream.Position = block.Position;
                        LayerInfo = new LayerInfo(reader, isPSB, depth, colorMode, block.Position + block.Length);
                        reader.BaseStream.Position = end;
                    }
                }
            }
            else
            {
                LayerInfo = new LayerInfo();
                GlobalLayerMaskInfo = new GlobalLayerMaskInfo();
                AdditionalLayerInformations = [];
            }
            InvalidStreamPositionException.ThrowIfInvalid(reader, position, lengthSize + Length);
        }
    }

}