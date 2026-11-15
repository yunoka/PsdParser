namespace PsdParser
{
    public class FileHeaderSection
    {
        public ushort Version { get; }
        public ushort Channels { get; }
        public int Width { get; }
        public int Height { get; }
        public ushort Depth { get; }
        public ColorMode ColorMode { get; }

        internal FileHeaderSection(PsdBinaryReader reader)
        {
            var sign = new string(reader.ReadChars(4));
            if (sign != "8BPS")
                throw new InvalidPsdDataException($"Not a PSD file (signature \"{sign}\").");

            Version = reader.ReadUInt16();
            if (Version != 1 && Version != 2)
                throw new InvalidPsdDataException($"Not supported file version: {Version}");

            reader.BaseStream.Position += 6;

            Channels = reader.ReadUInt16();
            Height = reader.ReadInt32();
            Width = reader.ReadInt32();
            Depth = reader.ReadUInt16();
            ColorMode = (ColorMode)reader.ReadUInt16();

            //仕様の範囲を確かめる(壊れたファイル・細工したファイルの値を使わないため)
            if (Channels is < 1 or > 56)
                throw new InvalidPsdDataException($"Number of channels {Channels} is out of range (1 to 56).");
            if (Width < 1 || Height < 1)
                throw new InvalidPsdDataException($"Image size {Width}x{Height} is out of range.");
            reader.EnsureSize(Width, Height, Version is 2, "Image", callerLimits: true);
            if (Depth is not (1 or 8 or 16 or 32))
                throw new InvalidPsdDataException($"Bit depth {Depth} is not supported by the format (1, 8, 16 or 32).");
            if (!Enum.IsDefined(ColorMode))
                throw new InvalidPsdDataException($"Color mode {(int)ColorMode} is not defined by the format.");

            InvalidStreamPositionException.ThrowIfInvalid(reader, 0, 4 + 2 + 6 + 2 + 4 + 4 + 2 + 2);
        }
    }
}