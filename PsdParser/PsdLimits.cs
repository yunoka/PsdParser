namespace PsdParser
{
    /// <summary>
    /// 読み込むファイルの大きさの上限。壊れたファイルや細工したファイルで、極端な大きさのメモリを確保したり、
    /// 範囲の外を読み書きしたりしないために、ファイルに書かれた値を使う前にこの上限と照らし合わせる。
    /// 既定値は Photoshop のファイル形式の仕様の範囲（幅・高さは PSD で 30,000、PSB で 300,000 まで）。
    /// </summary>
    public sealed class PsdLimits
    {
        /// <summary>既定の上限（仕様の範囲）。</summary>
        public static PsdLimits Default { get; } = new();

        /// <summary>画像（全体とレイヤー）の幅・高さの上限。null なら仕様の上限（PSD は 30,000、PSB は 300,000）。</summary>
        public int? MaxSide { get; init; }

        /// <summary>1 枚の画像（全体とレイヤー）の画素の数の上限。</summary>
        public long MaxPixels { get; init; } = long.MaxValue;

        /// <summary>読み込むファイルの大きさ（バイト）の上限。ファイル全体をメモリに読むので、配列に収まる大きさまで。</summary>
        public long MaxFileBytes { get; init; } = Array.MaxLength;

        internal int SideLimit(bool isPSB) => Math.Min(MaxSide ?? int.MaxValue, SpecSideLimit(isPSB));

        //仕様の幅・高さの上限
        internal static int SpecSideLimit(bool isPSB) => isPSB ? 300_000 : 30_000;
    }
}
