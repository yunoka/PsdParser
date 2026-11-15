using PsdParser.Descriptors;

namespace PsdParser.AdditionalLayerInformations
{
    //レイヤースタイル(lfx2。同じ種類の効果を複数使うときは lmfx)。効果の版 4 バイト(0)、記述子の版 4 バイト(16)、記述子。
    //記述子は初めて使うときに解析する(スタイルが壊れていても、ファイルとレイヤーは読めるように。壊れていれば Descriptor を使うときに
    //InvalidPsdDataException)。中身のキー(ドロップシャドウの DrSh など)は仕様書に書かれていないので、読む側で解釈する
    public class ObjectBasedEffects : AdditionalLayerInformation
    {
        Lazy<Descriptor> descriptor = new(() => throw new InvalidPsdDataException("Layer effects data is missing."));

        //効果の版と記述子の版(短すぎて読めなければ 0)
        public uint Version { get; private set; }
        public uint DescriptorVersion { get; private set; }
        public Descriptor Descriptor => descriptor.Value;

        internal ObjectBasedEffects(PsdBinaryReader reader, AdditionalLayerInformationKey key, long length) : base(reader, key, length)
        {
        }

        private protected override void Load(PsdBinaryReader reader, long length)
        {
            if (length < 8)
            {
                descriptor = new(() => throw new InvalidPsdDataException($"Layer effects data ({length} bytes) is too short."));
                return;
            }
            Version = reader.ReadUInt32();
            DescriptorVersion = reader.ReadUInt32();
            var (version, descriptorVersion, size) = (Version, DescriptorVersion, length - 8);
            //記述子は、読み込みのときには写さない。ファイルのメモリ(PsdFile は常にファイルをメモリに読む)の上の位置だけを持つ
            var start = reader.BaseStream.Position;
            var memory = reader.UnderlyingBuffer;
            byte[] bytes;
            int offset;
            if (size > int.MaxValue)
            {
                descriptor = new(() => throw new InvalidPsdDataException($"Layer effects data ({size} bytes) is too large."));
                return;
            }
            if (memory is not null)
            {
                bytes = memory;
                offset = checked((int)start);
            }
            else
            {
                //ファイルをメモリに読んでいないとき(PsdFile は常にメモリに読むので、今は起きない)は写す。読み足りなければ、使うときの例外にする
                bytes = reader.ReadBytes((int)size);
                offset = 0;
                if (bytes.Length < size)
                {
                    var read = bytes.Length;
                    descriptor = new(() => throw new InvalidPsdDataException($"Layer effects data ({size} bytes) could only be read in part ({read} bytes)."));
                    return;
                }
            }
            descriptor = new(() =>
            {
                if (version != 0 || descriptorVersion != 16)
                    throw new InvalidPsdDataException($"Unknown layer effects version {version} or descriptor version {descriptorVersion}.");
                return DescriptorReader.Read(bytes, offset, (int)size);
            });
        }
    }
}
