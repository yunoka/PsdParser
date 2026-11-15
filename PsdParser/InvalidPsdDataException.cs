namespace PsdParser
{
    /// <summary>
    /// ファイルに書かれた値が、仕様の範囲や上限（<see cref="PsdLimits"/>）の外にある、またはファイルの中に収まらない。
    /// 壊れたファイルか、細工したファイルである。
    /// </summary>
    public class InvalidPsdDataException : Exception
    {
        public InvalidPsdDataException(string message) : base(message) { }
        public InvalidPsdDataException(string message, Exception inner) : base(message, inner) { }
    }
}
