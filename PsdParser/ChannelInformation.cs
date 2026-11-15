namespace PsdParser
{
    public class ChannelInformation
    {
        public ChannelId Id { get; }
        public long ChannelDataLength { get; }

        internal ChannelInformation(PsdBinaryReader reader, bool isPSB)
        {
            Id = (ChannelId)reader.ReadInt16();
            ChannelDataLength = isPSB ? reader.ReadInt64() : reader.ReadUInt32();
            //データの本体はレイヤーの記録のあとに並ぶので、ここではファイルの大きさと比べるだけにする
            if (ChannelDataLength < 0 || ChannelDataLength > reader.BaseStream.Length)
                throw new InvalidPsdDataException($"Channel data length {ChannelDataLength} does not fit in the file.");
        }
    }
}