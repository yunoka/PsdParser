namespace PsdParser
{
    public class PsdFile : IDisposable
    {
        readonly Stream stream;
        readonly PsdBinaryReader reader;

        public FileHeaderSection Header { get; }
        public ColorModeDataSection ColorModeDataSection { get; }
        public ImageResourceSection ImageResourceSection { get; }
        public LayerAndMaskInformationSection LayerAndMaskInformationSection { get; }
        //合成済みの画像(ファイルの最後の節)。ファイルがレイヤーとマスクの情報の節で終わっていれば null。初めて使うときに読む
        //(この節が壊れていても、ファイルを開いてレイヤーは読めるように。壊れていれば、使うときに InvalidPsdDataException)
        public ImageDataSection? ImageDataSection
        {
            get
            {
                lock (imageDataLock)
                {
                    ObjectDisposedException.ThrowIf(disposedValue, this);
                    if (imageDataPosition is not { } position || imageDataSection is not null)
                        return imageDataSection;
                    try
                    {
                        reader.BaseStream.Position = position;
                        return imageDataSection = new ImageDataSection(reader, Header, LayerAndMaskInformationSection.LayerInfo.HasTransparencyData);
                    }
                    catch (Exception ex) when (IsBrokenData(ex))
                    {
                        throw new InvalidPsdDataException($"The merged image data is broken: {ex.Message}", ex);
                    }
                }
            }
        }
        readonly long? imageDataPosition;
        ImageDataSection? imageDataSection;
#if NET9_0_OR_GREATER
        readonly Lock imageDataLock = new();
#else
        readonly object imageDataLock = new();
#endif
        //合成済みの画像が本物か(版の情報のリソース 1057。Photoshop の「互換性を優先」で保存したときに true)。リソースがなければ null
        public bool? HasRealMergedData => ImageResourceSection.Blocks.OfType<ImageResourceBlocks.VersionInfo>().FirstOrDefault()?.HasRealMergedData;
        //包括光源の角度(度。リソース 1037)。レイヤースタイルの「包括光源を使用」の効果が使う。リソースがない・読めなければ 30(仕様書の既定)
        public int GlobalLightAngle => ImageResourceSection.Blocks.OfType<ImageResourceBlocks.GlobalAngle>().FirstOrDefault()?.Angle ?? 30;

        public PsdFile(string file) : this(file, PsdLimits.Default)
        {
        }

        //limits: ファイルに書かれた値と照らし合わせる上限。壊れたファイルや細工したファイルは InvalidPsdDataException になる。
        public PsdFile(string file, PsdLimits limits)
        {

            //RGBデコードはチャンネルをスキャンライン単位で R->G->B->A と飛び飛びにシークするため、
            //FileStreamのままだとバッファが効かず実質ランダムIOになる。
            //ファイル全体を一度メモリへ読み込み、メモリ上でシークすることでこのコストを排除する。
            //(PSDは全レイヤーを展開する用途であり、全体ロードのメモリ増加は許容範囲)
            //publiclyVisible:trueでGetBuffer()を許可し、デコード時にバッファから直接読めるようにする。
            //大きさの確認と読み込みを同じファイルの開き方で行う(確認のあとでファイルが差し替わっても上限を越えないように)
            byte[] bytes;
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length > limits.MaxFileBytes)
                    throw new InvalidPsdDataException($"The file ({input.Length} bytes) exceeds the limit of {limits.MaxFileBytes} bytes.");
                bytes = new byte[input.Length];
                input.ReadExactly(bytes);
            }
            stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            reader = new PsdBinaryReader(stream, limits);

            try
            {
                Header = new FileHeaderSection(reader);
                ColorModeDataSection = new ColorModeDataSection(reader);
                ImageResourceSection = new ImageResourceSection(reader);
                LayerAndMaskInformationSection = new LayerAndMaskInformationSection(reader, Header.Version is 2, Header.Depth, Header.ColorMode);
                if (stream.Length - stream.Position >= 2)
                    imageDataPosition = stream.Position;
            }
            catch (Exception ex)
            {
                //どの例外でも、ファイル全体を読んだメモリをすぐに手放す(呼び出し側はこのオブジェクトを受け取れないので破棄できない)
                Dispose();
                //壊れたファイル・細工したファイルで起きる例外は、InvalidPsdDataException にそろえる(呼び出し側が 1 つの型で受け止められるように)
                if (IsBrokenData(ex) || ex is ArgumentOutOfRangeException)
                    throw new InvalidPsdDataException($"The file is broken: {ex.Message}", ex);
                throw;
            }
        }

        //壊れたデータで起きる例外(ファイルの途中で終わる、計算があふれる、範囲の外を指す)か。
        //対応していない機能(NotSupportedException。1ビットの画像、インデックスなどの色の形式の画像への変換など)と、
        //値の検査の例外(InvalidPsdDataException)は含めない
        //(引数の誤りの例外は、呼び出し方の誤りかもしれないので含めない。読み込みの中で起きるものは、上の catch で別に扱う)
        internal static bool IsBrokenData(Exception ex) =>
            ex is EndOfStreamException or OverflowException or IndexOutOfRangeException or InvalidCastException;

        #region IDisposable
        private bool disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // マネージド状態を破棄します (マネージド オブジェクト)
                    //チャンネルを閉じる(公開の ReadLine が残した ZIP の展開の途中の状態を閉じ、以後の読み込みを例外にする)
                    foreach (var item in LayerAndMaskInformationSection?.LayerInfo?.Items ?? [])
                        foreach (var channel in item.Image?.ChannelImages ?? [])
                            channel.Close();
                    lock (imageDataLock)
                        foreach (var channel in imageDataSection?.ChannelImages ?? [])
                            channel.Close();
                    stream?.Dispose();
                    reader?.Dispose();
                }

                // アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        // // 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        // ~PsdFile()
        // {
        //     // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}