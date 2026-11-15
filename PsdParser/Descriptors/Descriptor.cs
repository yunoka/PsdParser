using System.Text;

namespace PsdParser.Descriptors
{
    //記述子(Descriptor)の値。追加情報の多く(レイヤースタイルの lfx2 など)が、この入れ子の形でデータを持つ(仕様書の Descriptor structure)。
    //型ごとのバイトの並びは、仕様書と psd-tools の読み方で確かめた
    public abstract class DescriptorValue
    {
    }

    //記述子(Objc・GlbO)。項目は、ファイルに書かれた順のキーと値の組
    public sealed class Descriptor : DescriptorValue
    {
        public string Name { get; }
        public string ClassId { get; }
        public IReadOnlyList<KeyValuePair<string, DescriptorValue>> Items { get; }

        internal Descriptor(string name, string classId, IReadOnlyList<KeyValuePair<string, DescriptorValue>> items)
        {
            Name = name;
            ClassId = classId;
            Items = items;
        }

        //キーの値(なければ null。同じキーが複数あれば最初のもの)
        public DescriptorValue? this[string key] => Items.FirstOrDefault(i => i.Key == key).Value;

        //キーの値を、型を指定して取る(なければ、または型が違えば null)
        public T? Get<T>(string key) where T : DescriptorValue => this[key] as T;
    }

    public sealed class DescriptorDouble(double value) : DescriptorValue { public double Value { get; } = value; }

    //単位付きの浮動小数(UntF)。単位は '#Pxl'(画素)・'#Prc'(百分率)・'#Ang'(角度)など
    public sealed class DescriptorUnitFloat(string unit, double value) : DescriptorValue
    {
        public string Unit { get; } = unit;
        public double Value { get; } = value;
    }

    //単位付きの浮動小数の並び(UnFl)
    public sealed class DescriptorUnitFloats(string unit, double[] values) : DescriptorValue
    {
        public string Unit { get; } = unit;
        public IReadOnlyList<double> Values { get; } = values;
    }

    public sealed class DescriptorText(string value) : DescriptorValue { public string Value { get; } = value; }

    //列挙(enum)。Type は列挙の種類('BlnM' など)、Value はその値('Nrml' など)
    public sealed class DescriptorEnum(string type, string value) : DescriptorValue
    {
        public string Type { get; } = type;
        public string Value { get; } = value;
    }

    public sealed class DescriptorInteger(int value) : DescriptorValue { public int Value { get; } = value; }
    public sealed class DescriptorLargeInteger(long value) : DescriptorValue { public long Value { get; } = value; }
    public sealed class DescriptorBool(bool value) : DescriptorValue { public bool Value { get; } = value; }

    //並び(VlLs)
    public sealed class DescriptorList(DescriptorValue[] items) : DescriptorValue { public IReadOnlyList<DescriptorValue> Items { get; } = items; }

    //オブジェクトの配列(ObAr)。先頭の数(データの残りのバイト数を越えないことを確かめてある)と、記述子の形の中身
    public sealed class DescriptorObjectArray(uint count, Descriptor body) : DescriptorValue
    {
        public uint Count { get; } = count;
        public Descriptor Body { get; } = body;
    }

    //クラス(type・GlbC)
    public sealed class DescriptorClass(string name, string classId) : DescriptorValue
    {
        public string Name { get; } = name;
        public string ClassId { get; } = classId;
    }

    //参照(obj )。項目は DescriptorProperty・DescriptorClass・DescriptorEnumReference・DescriptorOffset・DescriptorInteger(識別子・番号)・DescriptorName
    public sealed class DescriptorReference(DescriptorValue[] items) : DescriptorValue { public IReadOnlyList<DescriptorValue> Items { get; } = items; }

    public sealed class DescriptorProperty(string name, string classId, string keyId) : DescriptorValue
    {
        public string Name { get; } = name;
        public string ClassId { get; } = classId;
        public string KeyId { get; } = keyId;
    }

    public sealed class DescriptorEnumReference(string name, string classId, string type, string value) : DescriptorValue
    {
        public string Name { get; } = name;
        public string ClassId { get; } = classId;
        public string Type { get; } = type;
        public string Value { get; } = value;
    }

    public sealed class DescriptorOffset(string name, string classId, uint value) : DescriptorValue
    {
        public string Name { get; } = name;
        public string ClassId { get; } = classId;
        public uint Value { get; } = value;
    }

    public sealed class DescriptorName(string name, string classId, string value) : DescriptorValue
    {
        public string Name { get; } = name;
        public string ClassId { get; } = classId;
        public string Value { get; } = value;
    }

    //生のデータ(tdta・alis・Pth )。Type は型の4文字
    public sealed class DescriptorRawData(string type, byte[] data) : DescriptorValue
    {
        public string Type { get; } = type;
        public byte[] Data { get; } = data;
    }

    //記述子を読む。壊れたデータ・細工したデータは InvalidPsdDataException にする。入れ子の深さに上限を付け、
    //数や長さは、残りのバイト数に収まることを確かめてから使う(巨大な配列を確保させないため)
    internal static class DescriptorReader
    {
        //入れ子の深さの上限(Photoshop の書くレイヤースタイルは数段。深すぎるものは細工とみなす)
        public const int MaxDepth = 64;

        //data の先頭から記述子(名前・クラス・項目)を1つ読む
        public static Descriptor Read(byte[] data, int offset, int length)
        {
            try
            {
                var cursor = new Cursor(data, offset, offset + length);
                return ReadBody(ref cursor, 0);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException or IndexOutOfRangeException or InsufficientExecutionStackException)
            {
                throw new InvalidPsdDataException($"The descriptor is broken: {ex.Message}", ex);
            }
        }

        struct Cursor(byte[] data, int position, int end)
        {
            public int Position = position;
            public readonly int End = end;
            public readonly byte[] Data = data;
            public readonly int Remaining => End - Position;

            public ReadOnlySpan<byte> Take(long count, string what)
            {
                if (count < 0 || count > Remaining)
                    throw new InvalidPsdDataException($"Descriptor {what} ({count} bytes) does not fit in the data ({Remaining} bytes left).");
                var span = Data.AsSpan(Position, (int)count);
                Position += (int)count;
                return span;
            }

            public uint UInt32() => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(Take(4, "number"));
            public int Int32() => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(Take(4, "number"));
            public long Int64() => System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(Take(8, "number"));
            public double Double() => System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(Take(8, "number"));
            public string OsType() => Encoding.Latin1.GetString(Take(4, "type"));

            //Unicode の文字列(UTF-16 の文字の数 4 バイト、UTF-16 のビッグエンディアン)。最後の NUL は除く
            public string Unicode()
            {
                var count = UInt32();
                var text = Encoding.BigEndianUnicode.GetString(Take((long)count * 2, "string"));
                return text.TrimEnd('\0');
            }

            //キー・クラス ID(長さ 4 バイト、文字列。長さが 0 なら 4 バイトの ID)
            public string Key()
            {
                var length = UInt32();
                return Encoding.Latin1.GetString(Take(length == 0 ? 4 : length, "key"));
            }

            //数(項目の数・並びの長さ)。1 つが少なくとも minBytes バイトあるので、残りのバイト数で上限を確かめる
            public int Count(int minBytes, string what)
            {
                var count = UInt32();
                if (count > (uint)(Remaining / minBytes))
                    throw new InvalidPsdDataException($"Descriptor {what} count {count} does not fit in the data ({Remaining} bytes left).");
                return (int)count;
            }
        }

        //入れ子の深さを確かめる。記述子・並び・オブジェクトの配列のどれで入れ子にしても、ここを通る。
        //スタックの残りも確かめる(細工したデータの深い入れ子で、受け止められないスタックのあふれを起こさないため)
        static void CheckDepth(int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidPsdDataException($"Descriptor is nested more than {MaxDepth} levels.");
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        static Descriptor ReadBody(ref Cursor c, int depth)
        {
            CheckDepth(depth);
            var name = c.Unicode();
            var classId = c.Key();
            //1 項目は少なくとも 10 バイト(キーの長さ 4 ＋ 1 文字、型 4、値 1)
            var count = c.Count(10, "item");
            var items = new List<KeyValuePair<string, DescriptorValue>>(count);
            for (int i = 0; i < count; i++)
            {
                var key = c.Key();
                var type = c.OsType();
                items.Add(new(key, ReadValue(ref c, type, depth)));
            }
            return new Descriptor(name, classId, items);
        }

        static DescriptorValue ReadValue(ref Cursor c, string type, int depth)
        {
            CheckDepth(depth);
            switch (type)
            {
                case "Objc":
                case "GlbO":
                    return ReadBody(ref c, depth + 1);
                case "VlLs":
                    {
                        //1 つは、型(4)と、少なくとも 1 バイトの値
                        var count = c.Count(5, "list");
                        var items = new DescriptorValue[count];
                        for (int i = 0; i < count; i++)
                            items[i] = ReadValue(ref c, c.OsType(), depth + 1);
                        return new DescriptorList(items);
                    }
                case "ObAr":
                    {
                        //先頭の数は、使う側が配列の大きさに使いうるので、ほかの数と同じく残りのバイト数で上限を確かめる
                        var count = c.Count(1, "object array");
                        return new DescriptorObjectArray((uint)count, ReadBody(ref c, depth + 1));
                    }
                case "obj ":
                    {
                        var count = c.Count(5, "reference");
                        var items = new DescriptorValue[count];
                        for (int i = 0; i < count; i++)
                            items[i] = ReadReferenceItem(ref c, c.OsType());
                        return new DescriptorReference(items);
                    }
                case "doub":
                    return new DescriptorDouble(c.Double());
                case "UntF":
                    return new DescriptorUnitFloat(c.OsType(), c.Double());
                case "UnFl":
                    {
                        var unit = c.OsType();
                        var count = c.Count(8, "unit float");
                        var values = new double[count];
                        for (int i = 0; i < count; i++)
                            values[i] = c.Double();
                        return new DescriptorUnitFloats(unit, values);
                    }
                case "TEXT":
                    return new DescriptorText(c.Unicode());
                case "enum":
                    return new DescriptorEnum(c.Key(), c.Key());
                case "long":
                    return new DescriptorInteger(c.Int32());
                case "comp":
                    return new DescriptorLargeInteger(c.Int64());
                case "bool":
                    return new DescriptorBool(c.Take(1, "bool")[0] != 0);
                case "type":
                case "GlbC":
                    return new DescriptorClass(c.Unicode(), c.Key());
                case "alis":
                case "tdta":
                case "Pth ":
                    {
                        var length = c.UInt32();
                        return new DescriptorRawData(type, c.Take(length, "raw data").ToArray());
                    }
                default:
                    throw new InvalidPsdDataException($"Unknown descriptor item type '{type}'.");
            }
        }

        static DescriptorValue ReadReferenceItem(ref Cursor c, string type) => type switch
        {
            "prop" => new DescriptorProperty(c.Unicode(), c.Key(), c.Key()),
            "Clss" => new DescriptorClass(c.Unicode(), c.Key()),
            "Enmr" => new DescriptorEnumReference(c.Unicode(), c.Key(), c.Key(), c.Key()),
            "rele" => new DescriptorOffset(c.Unicode(), c.Key(), c.UInt32()),
            "Idnt" or "indx" => new DescriptorInteger(c.Int32()),
            "name" => new DescriptorName(c.Unicode(), c.Key(), c.Unicode()),
            _ => throw new InvalidPsdDataException($"Unknown descriptor reference type '{type}'."),
        };
    }
}
