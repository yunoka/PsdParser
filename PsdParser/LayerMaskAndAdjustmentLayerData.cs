namespace PsdParser
{
    public class LayerMaskAndAdjustmentLayerData
    {
        public int Size { get; }

        public int Top { get; }
        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }

        public byte DefaultColor { get; }
        public LayerMaskAndAdjustmentLayerDataFlags Flags { get; }
        /// <summary>マスクのパラメータのフラグ(ビット 0:ユーザーマスクの濃度、1:ユーザーマスクのぼかし、2:ベクターマスクの濃度、3:ベクターマスクのぼかし)。</summary>
        public byte MaskParameters { get; }
        /// <summary>ユーザーマスクの濃度があればその値、なければベクターマスクの濃度(以前からの名前。どちらのマスクかは区別しない)。</summary>
        public byte MaskDensity { get; }
        /// <summary>ユーザーマスクのぼかしがあればその値、なければベクターマスクのぼかし(以前からの名前。どちらのマスクかは区別しない)。</summary>
        public double MaskFeather { get; }
        /// <summary>ユーザーマスク(ピクセルマスク)の濃度(0〜255。255 が 100%)。設定がなければ null。</summary>
        public byte? UserMaskDensity { get; }
        /// <summary>ユーザーマスク(ピクセルマスク)のぼかし(ピクセル)。設定がなければ null。</summary>
        public double? UserMaskFeather { get; }
        /// <summary>ベクターマスクの濃度(0〜255)。設定がなければ null。</summary>
        public byte? VectorMaskDensity { get; }
        /// <summary>ベクターマスクのぼかし(ピクセル)。設定がなければ null。</summary>
        public double? VectorMaskFeather { get; }
        public LayerMaskAndAdjustmentLayerDataFlags RealFlags { get; }
        public byte RealUserMaskBackground { get; }
        public int MaskTop { get; }
        public int MaskLeft { get; }
        public int MaskBottom { get; }
        public int MaskRight { get; }
        /// <param name="hasRealMask">レイヤーがチャンネル -3(実際のユーザーマスク)を持つか。持つときだけ、後半の範囲(Real の値)を読む。</param>
        internal LayerMaskAndAdjustmentLayerData(PsdBinaryReader reader, bool isPSB, bool hasRealMask)
        {
            var position = reader.BaseStream.Position;
            Size = reader.ReadInt32();
            reader.EnsureAvailable(Size, "Layer mask data");
            //値の検査のエラー(InvalidPsdDataException)は、そのまま出す(finallyの位置の検査で別の例外に置き換えない)
            var invalid = false;
            try
            {
                if (Size is 0)
                    return;
                
                Top = reader.ReadInt32();
                Left = reader.ReadInt32();
                Bottom = reader.ReadInt32();
                Right = reader.ReadInt32();
                invalid = true;
                reader.EnsureNonNegativeSize((long)Right - Left, (long)Bottom - Top, "Layer mask");
                invalid = false;

                DefaultColor = reader.ReadByte();
                Flags = (LayerMaskAndAdjustmentLayerDataFlags)reader.ReadByte();

                //順は psd-tools に合わせる(仕様書のこの部分の順は、実際のファイルと合わない)。後半の範囲(Real の値)は、レイヤーが
                //チャンネル -3 を持ち、長さが 36 以上のときだけ読む(長さだけで決めると、パラメータが長いときに取り違える。psd-tools の #693)
                if (hasRealMask && Size < 36)
                {
                    //チャンネル -3 があるのに後半の範囲が書かれていない(Photoshop は書く)。前半の範囲と値を使う(0 のままだと、-3 のマスクが黙って消える)
                    RealFlags = Flags;
                    RealUserMaskBackground = DefaultColor;
                    (MaskTop, MaskLeft, MaskBottom, MaskRight) = (Top, Left, Bottom, Right);
                }
                else if (hasRealMask)
                {
                    RealFlags = (LayerMaskAndAdjustmentLayerDataFlags)reader.ReadByte();
                    RealUserMaskBackground = reader.ReadByte();
                    MaskTop = reader.ReadInt32();
                    MaskLeft = reader.ReadInt32();
                    MaskBottom = reader.ReadInt32();
                    MaskRight = reader.ReadInt32();
                    invalid = true;
                    reader.EnsureNonNegativeSize((long)MaskRight - MaskLeft, (long)MaskBottom - MaskTop, "Real layer mask");
                    invalid = false;
                }

                //パラメータは、立っているビットの値を順に読む(濃度とぼかしの両方を設定できる)
                if (Flags.HasFlag(LayerMaskAndAdjustmentLayerDataFlags.IndicatesThatTheUserAndOrVectorMasksHaveParametersAppliedToThem))
                {
                    //値は、マスクのデータの終わりを越えて読まない(越えるなら値の誤りとして、そのまま例外にする)
                    var end = position + 4 + Size;
                    void EnsureInside(int length)
                    {
                        if (reader.BaseStream.Position + length > end)
                        {
                            invalid = true;
                            throw new InvalidPsdDataException($"Layer mask parameters (flags 0x{MaskParameters:x2}) do not fit in the layer mask data.");
                        }
                    }
                    byte ReadByteParameter() { EnsureInside(1); return reader.ReadByte(); }
                    double ReadDoubleParameter() { EnsureInside(8); return reader.ReadDouble(); }
                    if (reader.BaseStream.Position < end)
                    {
                        MaskParameters = reader.ReadByte();
                        if ((MaskParameters & 1) != 0)
                            UserMaskDensity = ReadByteParameter();
                        if ((MaskParameters & 2) != 0)
                            UserMaskFeather = ReadDoubleParameter();
                        if ((MaskParameters & 4) != 0)
                            VectorMaskDensity = ReadByteParameter();
                        if ((MaskParameters & 8) != 0)
                            VectorMaskFeather = ReadDoubleParameter();
                    }

                    MaskDensity = UserMaskDensity ?? VectorMaskDensity ?? 0;
                    MaskFeather = UserMaskFeather ?? VectorMaskFeather ?? 0;
                }

                //残り(長さ 20 のときの 2 バイトの埋め草など)は飛ばす。データの外まで読んでいれば、finally の検査で例外にする
                if (reader.BaseStream.Position < position + 4 + Size)
                    reader.BaseStream.Position = position + 4 + Size;
            }
            finally
            {
                if (!invalid)
                    InvalidStreamPositionException.ThrowIfInvalid(reader, position, Size + 4);
            }
        }
    }
    [Flags]
    public enum LayerMaskAndAdjustmentLayerDataFlags
    {
        PositionRelativeToLayer = 1,
        LayerMaskDisabled = 2,
        InvertLayerMaskWhenBlending = 4,
        IndicatesThatTheUserMaskActuallyCameFromRenderingOtherData  = 8,
        IndicatesThatTheUserAndOrVectorMasksHaveParametersAppliedToThem = 16,
    }
}