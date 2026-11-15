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
        //対応していない機能(NotSupportedException。ZIP圧縮など)と、値の検査の例外(InvalidPsdDataException)は含めない
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