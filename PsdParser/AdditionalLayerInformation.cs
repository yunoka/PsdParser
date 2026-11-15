using PsdParser.AdditionalLayerInformations;

namespace PsdParser
{
    public class AdditionalLayerInformation
    {
        static readonly AdditionalLayerInformationKey[] LongLengthKeys =
        [
            AdditionalLayerInformationKey.UserMask,
            AdditionalLayerInformationKey.Lr16,
            AdditionalLayerInformationKey.Lr32,
            AdditionalLayerInformationKey.Layr,
            AdditionalLayerInformationKey.SavingMergedTransparency16,
            AdditionalLayerInformationKey.SavingMergedTransparency32,
            AdditionalLayerInformationKey.SavingMergedTransparency,
            AdditionalLayerInformationKey.Alph,
            AdditionalLayerInformationKey.FilterMask,
            AdditionalLayerInformationKey.LinkedLayer2,
            AdditionalLayerInformationKey.FilterEffects2,
            AdditionalLayerInformationKey.FilterEffects,
            AdditionalLayerInformationKey.PixelSourceData
        ];

        private protected PsdBinaryReader reader;
        public long Position { get; }
        public AdditionalLayerInformationKey Key { get; }
        public string KeyName
        {
            get
            {
                var iKey = (int)Key;
                var chars = new char[]
                {
                    (char)((iKey >> 24) & 0xFF),
                    (char)((iKey >> 16) & 0xFF),
                    (char)((iKey >> 8) & 0xFF),
                    (char)(iKey & 0xFF),
                };
                return new string(chars);
            }
        }
        public long Length { get; }

        private protected AdditionalLayerInformation(PsdBinaryReader reader, AdditionalLayerInformationKey key, long length)
        {
            this.reader = reader;
            Position = reader.BaseStream.Position;
            Key = key;
            Length = length;

            Load(reader,length);
            reader.BaseStream.Position = Position + length;
        }

        private protected virtual void Load(PsdBinaryReader reader, long length) { }

        //end: 追加情報が並ぶ範囲の終わり(レイヤーの追加のデータか、レイヤーとマスクの情報の節の終わり)。あとの詰め物はここまでしか読み飛ばさない
        internal static AdditionalLayerInformation Parse(PsdBinaryReader reader, bool isPSB, long end)
        {
            var position = reader.BaseStream.Position;

            var signature = new string(reader.ReadChars(4));
            if (signature != "8BIM" && signature != "8B64")
                throw new InvalidSignatureException(signature);

            var key = (AdditionalLayerInformationKey)reader.ReadInt32();

            bool isLongLength = isPSB && LongLengthKeys.Contains(key);
            var length = isLongLength ? reader.ReadInt64() : reader.ReadUInt32();
            reader.EnsureAvailable(length, "Additional layer information");

            var info = key switch
            {
                AdditionalLayerInformationKey.LayerID => new LayerID(reader, length),
                AdditionalLayerInformationKey.UnicodeLayerName => new UnicodeLayerName(reader, length),
                AdditionalLayerInformationKey.SectionDividerSetting or AdditionalLayerInformationKey.SectionDividerSetting2 => new SectionDividerSetting(reader, key, length),
                AdditionalLayerInformationKey.ObjectBasedEffects or AdditionalLayerInformationKey.MultipleObjectBasedEffects => new ObjectBasedEffects(reader, key, length),
                AdditionalLayerInformationKey.FillOpacity => new FillOpacity(reader, length),
                _ => new AdditionalLayerInformation(reader, key, length),
            };

            //2バイトアライメントと4バイトアライメントが混在している？
            //CAIは4バイトアライメントされている必要がある？CAIに関する記述がドキュメント（https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/）に存在しないため詳細は不明
            //Photoshop 2024以降で保存したファイルにCAIが含まれる可能性がある
            //https://helpx.adobe.com/jp/photoshop/using/content-credentials.html
            length += SkipZeroPadding(reader, end);

            var offset = 4 + 4 + (isLongLength ? 8 : 4);
            InvalidStreamPositionException.ThrowIfInvalid(reader, position, offset + length);
            return info;
        }
        //0の詰め物を読み飛ばす。範囲の終わり(end)で止める(越えて次の節の先頭の0や、ファイルの終わりまで読まないように)
        static int SkipZeroPadding(BinaryReader reader, long end)
        {
            var current = reader.BaseStream.Position;
            while (reader.BaseStream.Position < end)
            {
                if (reader.ReadByte() != 0)
                {
                    reader.BaseStream.Position--;
                    break;
                }
            }
            return (int)(reader.BaseStream.Position - current);
        }
        public static int MinSize(bool isPSB) => 4 + 4 + (isPSB ? 8 : 4);
    }
}