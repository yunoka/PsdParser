using System.Buffers.Binary;

namespace PsdParser.AdditionalLayerInformations
{
    //パターンの画像の一覧(Patt。16ビットの文書は Pat2、32ビットは Pat3)。文書全体の追加情報に入り、パターンオーバーレイ・
    //パターンの塗りつぶしレイヤー・境界線のパターンなどは、記述子の中の ID でここのパターンを引く。
    //一覧は初めて使うときに解析する(壊れていても、ファイルとレイヤーは読めるように。壊れていれば Items を使うときに InvalidPsdDataException)。
    //形は仕様書の Patterns と Virtual Memory Array List に、実物と psd-tools(psd/patterns.py)の読み方で2点を合わせた:
    //インデックスカラーの色の表のあとに4バイトがある(仕様書にない)、配列の圧縮は 0 = 生・1 = RLE・2 = ZIP・3 = 予測付きの ZIP(仕様書は「1 = ZIP」)
    public class Patterns : AdditionalLayerInformation
    {
        Lazy<IReadOnlyList<Pattern>> items = new(() => throw new InvalidPsdDataException("Pattern data is missing."));
        //Load は基底のコンストラクタから呼ばれ、この値はそのあとに入る。解析(初めて Items を使うとき)のときに読む
        readonly bool isPSB;

        public IReadOnlyList<Pattern> Items => items.Value;

        //PsdFile.Dispose で立てる。以後の Pattern.Read() は ObjectDisposedException(チャンネルの読み込みと同じ契約)
        volatile bool closed;
        internal bool Closed => closed;
        internal void Close() => closed = true;

        internal Patterns(PsdBinaryReader reader, AdditionalLayerInformationKey key, long length, bool isPSB) : base(reader, key, length)
        {
            this.isPSB = isPSB;
        }

        private protected override void Load(PsdBinaryReader reader, long length)
        {
            if (length > int.MaxValue)
            {
                items = new(() => throw new InvalidPsdDataException($"Pattern data ({length} bytes) is too large."));
                return;
            }
            var limits = reader.Limits;
            var size = (int)length;
            //ファイルのメモリ(PsdFile は常にファイルをメモリに読む)の上の位置だけを持つ。画素も、読むときにそこから展開する
            byte[] bytes;
            int offset;
            if (reader.UnderlyingBuffer is { } memory)
            {
                bytes = memory;
                offset = checked((int)reader.BaseStream.Position);
            }
            else
            {
                bytes = reader.ReadBytes(size);
                offset = 0;
                if (bytes.Length < size)
                {
                    var read = bytes.Length;
                    items = new(() => throw new InvalidPsdDataException($"Pattern data ({size} bytes) could only be read in part ({read} bytes)."));
                    return;
                }
            }
            items = new(() => Parse(this, bytes, offset, size, limits, isPSB));
        }

        //パターンごとに、長さ(4バイト)と中身。中身のあとは、長さを4の倍数にそろえる詰め物(最後のパターンでは欠けていてもよい)
        static IReadOnlyList<Pattern> Parse(Patterns owner, byte[] bytes, int offset, int size, PsdLimits limits, bool isPSB)
        {
            try
            {
                var reader = new PatternReader(bytes, offset, offset + size);
                var list = new List<Pattern>();
                while (reader.Remaining >= 4)
                {
                    var length = reader.ReadUInt32();
                    list.Add(Pattern.Parse(reader.Sub(length, "Pattern"), owner, bytes, limits, isPSB));
                    reader.Skip(Math.Min((int)((4 - length % 4) % 4), reader.Remaining));
                }
                return list;
            }
            catch (Exception ex) when (PsdFile.IsBrokenData(ex))
            {
                throw new InvalidPsdDataException($"Pattern data is broken: {ex.Message}", ex);
            }
        }
    }

    //パターンのデータを、範囲(end まで)を確かめながらビッグエンディアンで読む。範囲を越えれば InvalidPsdDataException
    internal sealed class PatternReader(byte[] bytes, int position, int end)
    {
        public byte[] Bytes { get; } = bytes;
        public int Position { get; private set; } = position;
        public int Remaining => end - Position;

        ReadOnlySpan<byte> Take(long count, string what)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidPsdDataException($"{what} ({count} bytes) does not fit in the pattern data.");
            var span = Bytes.AsSpan(Position, (int)count);
            Position += (int)count;
            return span;
        }

        public byte ReadByte() => Take(1, "Pattern data")[0];
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2, "Pattern data"));
        public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Take(2, "Pattern data"));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4, "Pattern data"));
        public ReadOnlySpan<byte> ReadBytes(long count, string what) => Take(count, what);
        public void Skip(int count) => Take(count, "Pattern data");

        //count バイトの範囲を切り出して読み手を作り、自分はそのあとへ進む
        public PatternReader Sub(long count, string what)
        {
            var start = Position;
            Take(count, what);
            return new PatternReader(Bytes, start, Position);
        }
    }
}
