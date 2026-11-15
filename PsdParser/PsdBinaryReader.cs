using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PsdParser
{
    internal class PsdBinaryReader(Stream input, PsdLimits limits) : BinaryReader(input)
    {
        public PsdLimits Limits { get; } = limits;

        //ファイルに書かれた長さ(バイト数)が、0以上で、今の位置からファイルの終わりまでに収まるかを確かめる。
        //収まらない値で配列を確保したり位置を進めたりしないよう、使う前に呼ぶ。
        public void EnsureAvailable(long length, string what)
        {
            if (length < 0 || length > BaseStream.Length - BaseStream.Position)
                throw new InvalidPsdDataException($"{what} ({length} bytes) does not fit in the file.");
        }

        //幅・高さが0以上で、上限に収まるかを確かめる。callerLimits が false なら仕様の範囲だけ、true なら PsdLimits の上限も使う。
        //幅・高さが0以上で、32ビットの整数に収まるか
        public void EnsureNonNegativeSize(long width, long height, string what)
        {
            if (width < 0 || height < 0 || width > int.MaxValue || height > int.MaxValue)
                throw new InvalidPsdDataException($"{what} size {width}x{height} is out of range.");
        }

        public void EnsureSize(long width, long height, bool isPSB, string what, bool callerLimits)
        {
            var side = callerLimits ? Limits.SideLimit(isPSB) : PsdLimits.SpecSideLimit(isPSB);
            if (width < 0 || height < 0 || width > side || height > side)
                throw new InvalidPsdDataException($"{what} size {width}x{height} is out of range (each side must be 0 to {side}).");
            if (callerLimits && width * height > Limits.MaxPixels)
                throw new InvalidPsdDataException($"{what} size {width}x{height} exceeds the limit of {Limits.MaxPixels} pixels.");
        }

        //入力がメモリ上のバッファなら、デコード時にコピーを介さず直接読むために公開する。
        //(PsdFileはpubliclyVisibleなMemoryStreamを渡すためGetBufferが使える)
        public byte[]? UnderlyingBuffer { get; } = (input as MemoryStream)?.GetBuffer();

        public override ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(ReadBigEndian(stackalloc byte[sizeof(ushort)]));
        public override short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(ReadBigEndian(stackalloc byte[sizeof(short)]));

        public override uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadBigEndian(stackalloc byte[sizeof(uint)]));
        public override int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBigEndian(stackalloc byte[sizeof(int)]));

        public override long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(ReadBigEndian(stackalloc byte[sizeof(long)]));
        public override ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(ReadBigEndian(stackalloc byte[sizeof(ulong)]));

        public override float ReadSingle() => BinaryPrimitives.ReadSingleBigEndian(ReadBigEndian(stackalloc byte[sizeof(float)]));
        public override double ReadDouble() => BinaryPrimitives.ReadDoubleBigEndian(ReadBigEndian(stackalloc byte[sizeof(double)]));
        //元実装と互換: decimalだがInt32相当(4バイト)をビッグエンディアンで読む
        public override decimal ReadDecimal() => BinaryPrimitives.ReadInt32BigEndian(ReadBigEndian(stackalloc byte[sizeof(int)]));

        public string ReadPascalString(int align) => ReadPascalString(align, out _);
        public string ReadPascalString(int align, out int readCount)
        {
            readCount = ReadByte();
            var bytes = ReadBytes(readCount);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var result = Encoding.GetEncoding("shift-jis").GetString(bytes, 0, readCount).TrimEnd('\0');

            readCount++;
            var padding = align - readCount % align;
            if (padding != align)
            {
                readCount += padding;
                BaseStream.Position += padding;
            }
            return result;
        }

        public string ReadUnicodeString()
        {
            var count = ReadInt32();
            EnsureAvailable((long)count * 2, "Unicode string");
            var length = count * 2;
            var bytes = ReadBytes(length);
            var txt =  Encoding.BigEndianUnicode.GetString(bytes, 0, length);
            return txt;
        }


        //ストリームからsizeバイトをspanへ読み込んで返す。
        //BinaryPrimitives.Read*BigEndianがビッグエンディアンとして解釈するため、ここでは反転しない。
        ReadOnlySpan<byte> ReadBigEndian(Span<byte> buffer)
        {
            var stream = BaseStream;
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer[read..]);
                if (n <= 0)
                    throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }
    }
}
