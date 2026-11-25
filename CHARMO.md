# Charmo 用の変更（charmo ブランチ）

このフォーク（yunoka/PsdParser）の `charmo` ブランチは、上流（[manju-summoner/PsdParser](https://github.com/manju-summoner/PsdParser)）の `master` に、Charmo（ロゴアニメーションの制作ツール）で使うための変更を積んだものです。上流との差分をいつでも説明でき、上流へフィードバックできる状態を保つために、変更をすべてここに書きます（フィードバックするかは、その都度決めます）。

上流との差分は、この一覧に必ず書きます（変更ごとに 1 項目。新しいものを下に足す）。あとの変更で置き換えた項目は消し、今の形だけを残します。番号は振り直しません（ほかの文書から番号で指しているため）。「コミット」は、その変更を入れたコミットです。コミットの番号は作ったあとでなければ決まらないので、変更のコミットのあとに、番号を書き入れるだけのコミットを続けて作ります（番号を書き入れるだけのコミットと、この文書だけを直したコミットは、一覧に載せません）。

| # | 日付 | コミット | 変更 | 理由 | ファイル | 上流でも役立つか |
|---|---|---|---|---|---|---|
| 2 | 2026-11-15 | `12a0aae` | 壊れたファイル・細工したファイルへの備え：ファイルに書かれた値を、使う前に仕様の範囲と上限に照らして確かめる。外れていれば、新しい例外 `InvalidPsdDataException` にする。壊れたデータで起きるほかの例外（ファイルの終わり、署名の誤りなど）も、この型にそろえる | ファイルに書かれた大きさ・長さを確かめずに、配列の確保や位置の計算に使っていた。細工したファイルで、極端な大きさのメモリの確保や、範囲の外の読み書き（下の 3）が起きえた。例外の型がそろっていないと、呼び出し側がまとめて受け止められない | `PsdLimits.cs`（新）・`InvalidPsdDataException.cs`（新）・`InvalidStreamPositionException.cs`・`InvalidSignatureException.cs`・`PsdFile.cs`・`PsdBinaryReader.cs`・`FileHeaderSection.cs`・`ColorModeDataSection.cs`・`ImageResourceSection.cs`・`ImageResourceBlock.cs`・`LayerAndMaskInformationSection.cs`・`LayerInfo.cs`・`LayerRecord.cs`・`LayerMaskAndAdjustmentLayerData.cs`・`LayerBlendingRangesData.cs`・`GlobalLayerMaskInfo.cs`・`AdditionalLayerInformation.cs`・`ChannelInformation.cs`・`ChannelImageData.cs`・`LayerImage.cs` | はい |
| 3 | 2026-11-15 | `12a0aae` | `unsafe` のポインタの処理を、範囲を確かめる書き方（`Span`）に置き換え、`AllowUnsafeBlocks` を外す。BGRA への変換は 1 画素 4 バイトをまとめて書き、行の読み込み用の作業領域はファイルをメモリに読んでいないときだけ確保する。RLE の行の表は、行の位置（累積）だけを持つ | RLE の展開は、書き込み先の範囲は確かめていたが、読み込み元（行のデータ）の範囲を確かめずにポインタで読んでいた。BGRA への変換は、画像の大きさ（幅 × 高さ × 4）を 32 ビットで求めていて、あふれると小さな配列を確保し、その外へポインタで書き込みえた | `PsdParser/ChannelImageData.cs`・`PsdParser/LayerImage.cs`・`PsdParser/PsdParser.csproj` | はい |
| 4 | 2026-11-15 | `12a0aae` | RLE の行ごとのバイト数（PSD では 2 バイト）を、符号なしで読む | 符号付きで読んでいたため、1 行の圧縮データが 32,767 バイトを超えると負の値になっていた。仕様では 2 バイトの数（負の値はない） | `PsdParser/ChannelImageData.cs` | はい |
| 5 | 2026-11-15 | `12a0aae` | RLE（PackBits）の -128 を「何もしない」として扱う | PackBits の仕様では -128 は何もしない印。以前は 129 回の繰り返しとして扱っていた（Photoshop はこの値を書かないので、ふつうのファイルでは違いは出ない） | `PsdParser/ChannelImageData.cs` | はい |
| 6 | 2026-11-15 | `12a0aae` | `LayerImage.Read()` は、対応していないビット数・色の形式のファイルではっきりした例外（`NotSupportedException`）を出す（10・11 で 16・32 ビットとグレースケールに対応した）。`ChannelImageData.ReadLine` は、チャンネルの高さの外の行を指定されたら例外を出す | 1 行のバイト数が変換用の配列と合わず、わかりにくい例外になっていた | `PsdParser/LayerImage.cs` | はい |
| 7 | 2026-11-15 | `12a0aae` | レイヤーマスクのチャンネル -3（実際のユーザーマスク）の大きさを、マスクのデータの後半の範囲（Real の上下左右）から求める | 以前は -2 と同じ前半の範囲を使っていた。2 の検査（行の最小の長さ・宣言された長さ）が加わったため、ベクターマスクとピクセルマスクの両方を持つ正しいファイルを拒否しうる（仕様書の Layer mask / adjustment layer data） | `PsdParser/LayerImage.cs` | はい |
| 8 | 2026-11-15 | `12a0aae` | レイヤーの情報がセクションの終わりで終わっているかの判定で、長さの欄の大きさを PSB では 8 バイトとして扱う | 4 バイト決め打ちだったため、全体のレイヤーマスクも追加情報もない PSB で、次のセクションの先頭を全体のレイヤーマスクとして読み、正しいファイルを拒否しえた | `PsdParser/LayerAndMaskInformationSection.cs` | はい |
| 9 | 2026-11-15 | `e44d96c` | ZIP 圧縮（予測なし・予測付き）のチャンネルを読む。展開は行の順に 1 行ずつ行い、チャンネル全体を確保しない（前の行に戻るときは先頭から展開し直す）。展開の途中の状態は、`LayerImage.Read()` では呼ぶたびに別々に持ち、読み終えたときと失敗したときに閉じる。公開の `ReadLine` が使う状態は 1 つで、`PsdFile.Dispose()` で閉じ、そのあとの `ReadLine` は `ObjectDisposedException` にする。最後の行まで展開したら、展開したデータから求めた Adler-32 を、圧縮データの最後の 4 バイト（zlib の検査値。そのあとの 0 の詰め物は 16 バイトまで許す）と照らし合わせ、画像より多いデータも壊れたものとする。宣言された長さが 2 バイト未満のデータは、ファイルを開くときに、圧縮データの長さの約 1032 倍（deflate で展開できる上限）を越える大きさの画像を言うデータは、そのチャンネルの展開を始めるときに `InvalidPsdDataException` にする（使わないチャンネルが壊れていても、ファイルとほかのチャンネルは読めるように）。ZIP は、ファイルをメモリに読んでいるとき（`PsdFile` は常にそう）だけ読む。予測付きは、8 ビットは 1 バイトずつ、16 ビットはビッグエンディアンの 2 バイトずつ、32 ビットは 1 行を 4 つのバイトの面に分けたものの差分を戻す（それ以外のビット数は `NotSupportedException`） | Photoshop は 16・32 ビットのレイヤーを予測付きの ZIP で保存するので、読めなかった（例外になっていた）。1 行ずつ展開するのは、使うメモリを RLE と同じく行の数によらないものにし、小さな圧縮データで巨大な確保をさせる細工（マスクのチャンネルのように、画像にするときの大きさの上限を通らないものを含む）を防ぐため。展開の状態を呼び出しごとに持つのは、同じレイヤーを複数のスレッドで同時に画像にしても、互いの状態を壊さないため（同時に読めるのは、ファイルをメモリに読んだとき。生のデータと RLE は、メモリに読んでいないとファイルの位置を動かすため）。検査値を自分で確かめるのは、.NET の `ZLibStream` が、最後の検査値が欠けたデータでも例外を出さないため（展開したデータからもう一度 Adler-32 を求めるので、展開の計算が少し増える。.NET の zlib からは、圧縮データを最後まで使い切ったかを知る方法がないため） | `PsdParser/ChannelImageData.cs`・`PsdParser/LayerImage.cs`・`PsdParser/PsdFile.cs` | はい |
| 10 | 2026-11-15 | `e44d96c` | `LayerImage.Read()` で、16・32 ビットを 8 ビットにする（16 ビットは四捨五入。32 ビットは浮動小数を 0〜1 に収め、色のチャンネルはリニアから sRGB の明るさに直してから四捨五入し、透明度はリニアのまま四捨五入する）。画像にするチャンネルは、色の 0〜(色の数 − 1) と透明度の -1 だけにする | 8 ビットのファイルしか画像にできなかった。Photoshop は 32 ビットの色をリニア（ガンマ 1.0）で保存するので、そのまま 8 ビットにすると暗くなる。特色などの余分なチャンネル（色の数以上の番号）を、透明度として読まないため | `PsdParser/LayerImage.cs` | はい |
| 11 | 2026-11-15 | `e44d96c` | `LayerImage.Read()` で、グレースケールのファイルを読む（灰色を R・G・B に並べる） | RGB と CMYK のファイルしか画像にできなかった | `PsdParser/LayerImage.cs` | はい |
| 12 | 2026-11-15 | `1e09fd8` | `LayerImage` に、精度を落とさずに読むメソッドを 2 つ足す。`ReadRgba16()` は 1 チャンネル 16 ビットの RGBA（`ushort[]`、ストレートアルファ）を返す（8 ビットは ×257 で広げ、32 ビットは `Read()` と同じく色をリニアから sRGB に直してから 16 ビットにする）。`ReadRgbaFloat(bool linear = true)` は 1 チャンネル `float` の RGBA を返し、色はビット数によらず、リニア（既定）か sRGB の明るさにそろえる（32 ビットのファイルはリニア、8・16 ビットは sRGB で保存されているとみなす。埋め込まれた ICC プロファイルは使わない。色の 0〜1 の外の値は残し、NaN は 0、無限大は float の最大の値にする。透明度は 0〜1 に収め、リニア・sRGB の直しはしない。8・16 ビットをリニアにするのは表で引く）。メモリは 1 画素 8 バイト・16 バイトで、`Read()` の 2 倍・4 倍になる（`PsdLimits` の画素の数の上限は、形式によらず同じに効く）。並びはどちらも R・G・B・A で、Skia の `Rgba16161616`・`RgbaF32` にそのまま渡せる。グレースケールと CMYK は、3 つの形式で同じ式で RGB にする（CMYK は保存されたままの値で計算する）。32 ビットの CMYK は、3 つとも `NotSupportedException` にする（Photoshop の 32 ビットは RGB とグレースケールだけ）。`Read()`（8 ビットの BGRA）の結果は変えず、3 つは同じ行の読み込みの処理を使い、1 行ずつ型の決まった配列に変換してから並べる。壊れたデータの例外（`InvalidPsdDataException`）にそろえるのは、チャンネルの読み込みで起きた例外だけにする | `Read()` は 8 ビットにしか変換できず、16・32 ビットの PSD を精度を保って使う用途（印刷、HDR の合成など）に使えなかった。8 ビットへの変換は Charmo の今の都合で、ライブラリとしては精度を保つ読み方も要る。浮動小数の色の意味をビット数でそろえるのは、呼ぶ側がファイルのビット数を見て変換し分けなくて済むようにするため | `PsdParser/LayerImage.cs` | はい |
| 13 | 2026-11-15 | `48639f5` | 対象のフレームワークを、上流の `net8.0` から `net8.0` と `net10.0` の両方にする。`System.Threading.Lock`（.NET 9 から）は、.NET 8 では `object` の lock にする | Charmo は .NET 10 でビルドし、このフォークは Charmo 以外のプロジェクト（.NET 8 のものを含む）からも使えるようにするため。Charmo は自分のビルドの設定で `net10.0` だけを作る | `PsdParser/PsdParser.csproj`・`PsdParser/ChannelImageData.cs` | はい（上流は `net8.0` だけ） |
| 14 | 2026-11-15 | `84bcaca` | 16・32 ビットの文書で、レイヤーを追加情報の `Lr16`・`Lr32` から読む（レイヤーの情報の節の長さが 0 のとき。ビット数に合うほうを先に探し、なければもう一方を使う。中身は、レイヤーの情報から先頭の長さの欄を除いたもので、レイヤーの数から始まる。レイヤーの記録とチャンネルのデータは、その追加情報の終わりを越えないこと）。長さが 0 のレイヤーの情報は、レイヤーの数を読まずに空にする。レイヤーの記録がレイヤーの情報の終わりを越えたら `InvalidPsdDataException` にする。`Lr16`・`Lr32` が壊れていれば、ふつうのレイヤーの情報が壊れているときと同じく、ファイルを開けない | Photoshop は 16・32 ビットの文書を保存するとき、レイヤーの情報の節を空にして、レイヤーを `Lr16`・`Lr32` に入れる。読まないと、Photoshop で保存した 16・32 ビットの PSD のレイヤーが 1 つも取れない。仕様書には書かれていない（psd-tools の `LayerInfoBlock` と同じ読み方。psd-tools も 2 つのキーの両方を見る）。長さが 0 のときは、以前はレイヤーの数として次の節の 2 バイトを読み、位置を戻して合わせていた | `PsdParser/LayerInfo.cs`・`PsdParser/LayerAndMaskInformationSection.cs`・`PsdParser/LayerRecord.cs` | はい |
| 15 | 2026-11-15 | `84bcaca` | 追加情報のあとの 0 の詰め物を読み飛ばすのを、追加情報が並ぶ範囲の終わり（レイヤーの追加のデータ、またはレイヤーとマスクの情報の節の終わり）で止める | 範囲の終わりを越えて読み進めていたため、次の節（合成済みの画像）の先頭の 0 まで読み飛ばし（あとで位置を戻して合わせていた）、ファイルがそこで終わっていると、ファイルの終わりを越えて読もうとして失敗した | `PsdParser/AdditionalLayerInformation.cs`・`PsdParser/LayerAndMaskInformationSection.cs`・`PsdParser/LayerRecord.cs` | はい |
| 16 | 2026-11-15 | `66eda4f` | ファイルの最後の節（Image Data。全レイヤーを合成した 1 枚）を読む。`PsdFile.ImageDataSection`（ファイルがその前で終わっていれば null）の `ChannelImages` と `Image`（`LayerImage` で、`Read()`・`ReadRgba16()`・`ReadRgbaFloat()` で読める）。節は初めて使うときに読み、壊れていればそのときに `InvalidPsdDataException` にする（仕様にない圧縮の種類も同じ）。生・RLE・ZIP（全チャンネルが 1 つのストリームに続けて入る。チャンネルごとにストリームの先頭から展開するので、チャンネルの数に比例して手間が増える。検査値は最後の行まで読んだときに確かめる）に対応する。色のチャンネルの次のチャンネルは、レイヤーの数が負のときだけ透明度として使う（マルチチャンネルの文書は、すべてインクのチャンネルとして扱う）。版の情報のリソース（1057）を読み、合成済みの画像が本物か（Photoshop の「互換性を優先」で保存したか）を `PsdFile.HasRealMergedData` で返す（リソースがないか、短くて読めなければ null）。RLE の行のバイト数の表は、PSB では 4 バイトとして読む（仕様書に書かれていないので、レイヤーのチャンネルと同じに読む） | レイヤーの合成（重ね方・レイヤー効果・調整レイヤー）を自分で再現しなくても、Photoshop で見たとおりの 1 枚が取れる。取り込んだレイヤーと見た目を比べる基準にも使える。節を使うときに読むのは、この節だけが壊れたファイルでも、レイヤーは読めるようにするため。ZIP の展開の手間は、Photoshop が合成済みの画像を ZIP で保存しないので、まれな形として受け入れる | `PsdParser/ImageDataSection.cs`（新）・`PsdParser/ImageResourceBlocks/VersionInfo.cs`（新）・`PsdParser/ImageResourceBlock.cs`・`PsdParser/ChannelImageData.cs`・`PsdParser/LayerImage.cs`・`PsdParser/PsdFile.cs` | はい |
| 17 | 2026-11-15 | `a8946f3` | 記述子（Descriptor）を、汎用の木として読む部品を足す（`PsdParser.Descriptors`。`Descriptor` と、型ごとの値のクラス）。仕様書の Descriptor structure のすべての型（`Objc`・`GlbO`・`VlLs`・`ObAr`・`obj `・`doub`・`UntF`・`UnFl`・`TEXT`・`enum`・`long`・`comp`・`bool`・`type`・`GlbC`・`alis`・`tdta`・`Pth `）を読む。入れ子の深さは、記述子・並び・オブジェクトの配列のどれで入れ子にしても 64 段まで（スタックの残りも確かめる）、数と長さ（オブジェクトの配列の先頭の数を含む）は残りのバイト数に収まることを確かめ、外れれば `InvalidPsdDataException` にする。これを使って、レイヤースタイル（`lfx2`、同じ種類の効果を複数使うときの `lmfx`）を `ObjectBasedEffects` として、塗りの不透明度（`iOpa`）を `FillOpacity` として読む。スタイルの記述子は初めて使うときに、ファイルのメモリの上から解析する（読み込みのときには写さない。短すぎる・大きすぎる・版が違う〔効果の版 0、記述子の版 16 でない〕・壊れているときも、ファイルとレイヤーは読め、`Descriptor` を使うときに例外になる）。空の `iOpa` は値なし（null）にする | レイヤースタイル（ドロップシャドウ・光彩・カラーオーバーレイなど）と塗りの不透明度を読めなかった。記述子は意味を解釈せず木のまま返す（効果のキー〔`DrSh` など〕は仕様書に書かれていないので、読む側で解釈する）。型ごとのバイトの並びは、仕様書と psd-tools の読み方で確かめた | `PsdParser/Descriptors/Descriptor.cs`（新）・`PsdParser/AdditionalLayerInformations/ObjectBasedEffects.cs`（新）・`PsdParser/AdditionalLayerInformations/FillOpacity.cs`（新）・`PsdParser/AdditionalLayerInformation.cs`・`PsdParser/AdditionalLayerInformationKey.cs` | はい |
| 18 | 2026-11-15 | `29ac6c7` | 透明度のある 8・16 ビットの文書（レイヤーの数が負）の合成済みの画像を読むとき、色を「白の上に重ねた色」から元の色に戻す（c = (c' − 白 × (1 − α)) ÷ α。α が 0 なら色は 0）。ビット数ごとの生の値の段階で戻すので、3 つの読み方のすべてに効く。CMYK とグレースケールも、保存の値の最大が白なので同じ式。32 ビットの文書（リニア）は、Photoshop がどの段階で白の上に重ねているかを確かめていないので戻さない | Photoshop は、透明度のある文書の合成済みの画像を、白の上に重ねた色で保存している。そのまま読むと、半透明の縁が白っぽくなる。Photoshop で保存した実物の PSD（背景が透明で縁が半透明のもの）で、半透明の画素の色が白の上に重ねた式に合うことを確かめた（仕様書には書かれていない） | `PsdParser/LayerImage.cs`・`PsdParser/ImageDataSection.cs` | はい |
| 19 | 2026-11-15 | `29ac6c7` | レイヤースタイル（17）の記述子を、ファイルをメモリに読んでいないときに写して持つ道で、読み足りなければ、`Descriptor` を使うときの例外にする | 以前は読み足りないまま、全体の長さで解析しようとして、分かりにくい例外になっていた（`PsdFile` は常にファイルをメモリに読むので、今は通らない道） | `PsdParser/AdditionalLayerInformations/ObjectBasedEffects.cs` | はい |
| 20 | 2026-11-15 | `19c44af` | 包括光源の角度（画像のリソース 1037）を読む。`PsdFile.GlobalLightAngle`（度。リソースがない・読めなければ、仕様書の既定の 30） | レイヤースタイルの「包括光源を使用」にした効果（ドロップシャドウなど）は、効果の中の角度ではなく、ファイル全体のこの角度を使う | `PsdParser/ImageResourceBlocks/GlobalAngle.cs`（新）・`PsdParser/ImageResourceBlock.cs`・`PsdParser/PsdFile.cs` | はい |
| 21 | 2026-11-19 | `f11e237` | レイヤーマスクのデータ（`LayerMaskAndAdjustmentLayerData`）を psd-tools と同じ順に読む：後半の範囲（Real の値。チャンネル -3 の範囲）は、レイヤーがチャンネル -3 を持ち、長さが 36 以上のときだけ読み、マスクのパラメータはそのあとに読む。パラメータは、立っているビットの値を全部順に読む（ユーザーマスクの濃度・ぼかし、ベクターマスクの濃度・ぼかし）。新しいプロパティ `UserMaskDensity`・`UserMaskFeather`・`VectorMaskDensity`・`VectorMaskFeather`（設定がなければ null）。以前からの `MaskDensity`・`MaskFeather` は、ユーザーマスクの値があればそれ、なければベクターマスクの値にする（以前の読み方で読めた、ビットが 1 つだけのファイルでは、以前と同じ値になる）。チャンネル -3 があるのに長さが 36 未満なら、後半の範囲として前半の範囲と値を使う。パラメータがマスクのデータの終わりを越えるなら `InvalidPsdDataException`。コンストラクターに、チャンネル -3 を持つかを渡す | マスクに「濃度」「ぼかし」を設定した PSD（Photoshop の属性パネル）が、読み取りの位置がずれて読めずに失敗していた（`Invalid stream position`）。パラメータを後半の範囲より前に読み、フラグを 1 つ（濃度かぼかしのどちらか）しか読まず、チャンネル -3 のないレイヤーでも後半の範囲を読んでいたため。仕様書のこの部分の順は実際のファイルと合わない（psd-tools のコードにも「順は試して決めた」とあり、長さだけで後半の範囲の有無を決めると取り違える問題は psd-tools の #693）。psd-zoo（CC0）の `mask/density.psd`・`feather.psd`・`vector_mask_density.psd`・`vector_mask_feather.psd` で確かめた | `PsdParser/LayerMaskAndAdjustmentLayerData.cs`・`PsdParser/LayerRecord.cs` | はい |
| 22 | 2026-11-25 | （コミットのあとに書き入れる） | パターンの画像の一覧（追加情報の `Patt`。16 ビットの文書は `Pat2`、32 ビットは `Pat3`）を読む。`PsdParser.AdditionalLayerInformations.Patterns`（`Items` で `Pattern` の一覧）と、文書全体の追加情報の 3 つをファイルの順に並べた `PsdFile.Patterns`。`Pattern` は ID（記述子の `Ptrn` の `Idnt` と同じ文字列）・名前（末尾の NUL を除く）・大きさ（VMAL の範囲）・画像のモード・インデックスの色の表（`ReadOnlyMemory<byte>` で、外から変えられない）と、色の表のあとの 4 バイトを 2 つの ushort にした `ColorTableCountCandidate`・`TransparentIndexCandidate`（色の数と透明の色の番号の候補。意味は未確認で、`Read()` では使わない）・書かれている配列（`PatternChannel`：VMAL の中の番号・深さ・範囲・圧縮・データの長さ）を持ち、`Read()` で `LayerImage.Read()` と同じ 8 ビットの BGRA（ストレートアルファ）にする。画像にできるのは RGB・グレースケール（8・16・32 ビット。8 ビットへの直し方は `LayerImage.Read()` と同じ）とインデックス（8 ビット）で、`CanRead` で分かる。それ以外の画像のモード（CMYK・Lab など）・ビット数（1 ビットなど）は `NotSupportedException`。圧縮は生・RLE・ZIP（予測なし・予測付き）で、展開はレイヤーのチャンネルと同じ部品（`ChannelImageData`）を使う。色の配列 i は、VMAL の番号（`Index`）が i の配列で、欠けていれば `InvalidPsdDataException`（書かれた順で選ぶと、欠けた配列の代わりに隣の配列を色として読んでしまう）。透明度は、番号が色の数以上の配列のうち、いちばん小さい番号のもの（RGB・グレースケールだけ。インデックスは透明度を使わない。psd-tools は「書かれている配列の、色の数の次のもの」を透明度にしていて、色の配列が 0〜(色の数 − 1) にそろっているときはこの決まりと同じ）。色の配列どうし、色と透明度の配列で、深さ（4 バイトの深さと 2 バイトの Pixel depth の両方）と大きさが違えば `InvalidPsdDataException`（Photoshop は 1 つのパターンの配列を同じ深さで書くので壊れたデータとみなす。透明度の配列を無視して不透明にすると、見た目が黙って変わるため断る）。`PsdFile` を Dispose したあとの `Read()` は、チャンネルの読み込みと同じく `ObjectDisposedException`（一覧の値は見られる）。RLE の行のバイト数の表は、PSB の文書でも 2 バイトとして読む（psd-tools はパターンの配列を、ファイルの版によらず版 1〔2 バイト〕で展開する。レイヤーのチャンネルは PSB で 4 バイト）。一覧は初めて使うときにファイルのメモリの上から解析し、長さ・数・範囲がデータに収まらない、版（パターン 1、VMAL 3）・画像のモードが仕様にない値、範囲が逆さま・32 ビットの整数に収まらない、配列の長さが 23 バイト未満なら `InvalidPsdDataException`（ファイルとレイヤーは読める）。画素は `Read()` のときに展開し、大きさの上限（仕様の 1 辺の上限と `PsdLimits`）・配列の範囲とパターンの範囲の一致・データの長さ（生のデータの大きさ、RLE の行のバイト数の表と行の最小の長さ）をそこで確かめる（上限を越えるパターンがあっても、一覧とほかのパターンは読める）。書かれていない配列のオブジェクトは作らない（配列の数がどれほど大きくても、確保はデータの長さで抑えられる） | 今まで `Patt` は中身を解釈せずに飛ばしていた。パターンオーバーレイ・パターンの塗りつぶしレイヤー・境界線のパターンは、記述子の中の ID でこの一覧のパターンを引くので、読まないとパターンの絵が取れない。仕様書（Patterns・Virtual Memory Array List）と実物で 2 点が違った：インデックスの色の表（768 バイト）のあとに 4 バイトがある（実物は `01 00 00 FF`。意味は未確認で、使わない。psd-tools も読み飛ばす）、配列の圧縮の 1 は ZIP ではなく RLE（行ごとのバイト数の表〔2 バイト〕で始まる。psd-tools も 0 生・1 RLE・2 ZIP・3 予測付き ZIP として読む）。Photoshop 2026 で作られた公開の PSD（psd-zoo の `fill/pattern.psd`。CC0。946 × 946 のインデックス、RLE）で、読んだ画素が psd-tools 1.27.0 で読んだものと全部同じで、Photoshop が合成した正解の画像（パターンの塗りつぶしレイヤー、200 × 200）が、パターンの左上と画素まで同じことを確かめた。**未確認：** 透明度の入る配列（psd-tools の読み方に合わせた。仕様書は、チャンネルの数のあとの 2 つをユーザーマスクとシートマスクとしている）、16・32 ビットのパターン（`Pat2`・`Pat3`）と RGB・グレースケールのパターンの実物、ZIP で保存された実物、PSB で RLE の行のバイト数の表が 4 バイトになるか（psd-tools に合わせて 2 バイトとして読む。PSB の実物はない）、色の表のあとの 4 バイトの意味（実物の 256・255 は、色の数と透明の色の番号〔画像のリソース 1046・1047 と同じ形〕と読めるが、その画像は番号 255 を使っておらず確かめられない。psd-tools も読み飛ばす） | `PsdParser/AdditionalLayerInformations/Patterns.cs`（新）・`PsdParser/AdditionalLayerInformations/Pattern.cs`（新）・`PsdParser/AdditionalLayerInformation.cs`・`PsdParser/AdditionalLayerInformationKey.cs`・`PsdParser/PsdFile.cs` | はい |

### 2 の検査の内容

- **ファイルの先頭：** チャンネル数は 1〜56、幅・高さは 1〜30,000（PSB は 300,000）、ビット数は 1・8・16・32、色の形式は仕様にある値（0・1・2・3・4・7・8・9）だけ（Adobe の Photoshop File Formats Specification の File Header Section）
- **長さの欄：** 各セクション・画像のリソース・レイヤーの追加情報・レイヤーマスク・合成の範囲・Unicode の文字列・チャンネルのデータの長さが、0 以上で、ファイルの残りに収まること
- **RLE：** 行ごとのバイト数の表と、圧縮データ全体がファイルに収まること。1 行の展開が、ちょうどその行の長さになること
- **RLE の行の最小の長さ：** 1 行の PackBits の圧縮データは、少なくとも 2 × ⌈1 行のバイト数 ÷ 128⌉ バイトある。これより短い行は拒否する（中身が空の行を並べた小さなファイルで、巨大な画像を確保させないため）
- **チャンネルの宣言された長さ：** データ（行ごとのバイト数の表が指す範囲）が、チャンネルの情報に書かれた長さを越えないこと。宣言された長さは、レイヤーの情報（Layer info）の終わりを越えないこと（越えると、隣のチャンネルやほかのセクションを画素として読んでしまう）
- **レイヤーとレイヤーマスクの範囲（読み込みのとき）：** 幅と高さが 0 以上で 32 ビットに収まること。レイヤーはキャンバスからはみ出してよく、仕様の上限より大きいこともあるので、大きさの上限（仕様と呼び出し側）は、そのレイヤーを画像にするとき（`LayerImage.Read()`）に確かめる
- **上限：** 呼び出し側が `PsdLimits`（1 辺の長さ・画素の数・ファイルの大きさ）で、仕様の範囲より厳しい上限を渡せる。ファイルの先頭の大きさとファイルの大きさには読み込みのときに、レイヤーの大きさには画像にするとき（`LayerImage.Read()`）に効く。レイヤーはキャンバスからはみ出してよいので、上限を越える大きなレイヤーがあっても、ファイル全体は読め、そのレイヤーだけが画像にできない。`new PsdFile(path)` は仕様の範囲だけで確かめる（上流と同じ呼び方のまま使える）
- **例外：** 値の検査の外れと、壊れたデータで起きる例外（ファイルの終わり、計算のあふれ、署名の誤り、位置の不一致など）は `InvalidPsdDataException` にそろえる（署名と位置の例外はこの型を継承する）。対応していない機能（1 ビットの画像、インデックス・Lab などの色の形式の画像への変換など）は、これまでどおり `NotSupportedException`。どの例外でも、`PsdFile` の作成に失敗したら、ファイル全体を読んだメモリをすぐに手放す
- **上流のまま残したもの：** セクションの終わりで、詰め物を数バイト読みすぎたときに位置を戻して合わせる処理（正しいファイルでも起きる。読みすぎはファイルの範囲の中なので、メモリの安全には関わらない）

## 使い方（charmo ブランチで足したもの）

上流と同じ呼び方（`new PsdFile(path)`、`LayerImage.Read()`）は、そのまま使えます。この節では、charmo ブランチで足したものの使い方をまとめます。対象のフレームワークは `net8.0` と `net10.0` です。

```csharp
using PsdParser;

// 上限を付けて開く（省略すると、仕様の範囲だけで確かめる）。ファイル全体をメモリに読むので、使い終えたら Dispose する
using var psd = new PsdFile("logo.psd", new PsdLimits { MaxSide = 16_000, MaxPixels = 64_000_000, MaxFileBytes = 1L << 30 });

foreach (var (layer, image) in psd.LayerAndMaskInformationSection.LayerInfo.Items) // layer.LayerName・layer.Opacity など
{
    try
    {
        byte[] bgra = image.Read();                      // 8 ビットの BGRA（上流と同じ）
        ushort[] rgba16 = image.ReadRgba16();            // 16 ビットの RGBA
        float[] linear = image.ReadRgbaFloat();          // float の RGBA（色はリニア）
        float[] srgb = image.ReadRgbaFloat(linear: false); // float の RGBA（色は sRGB の明るさ）
    }
    catch (InvalidPsdDataException) { /* このレイヤーのデータが壊れている。ほかのレイヤーは読める */ }
    catch (NotSupportedException) { /* 対応していないビット数・色の形式（1 ビット、インデックス、Lab など） */ }
}
```

### 読めるもの

- **ビット数と色の形式：** 8・16・32 ビットの RGB とグレースケール、8・16 ビットの CMYK（32 ビットの CMYK は、Photoshop で作れないので扱わない）
- **圧縮：** 生のデータ、RLE、ZIP（予測なし・予測付き）
- **画像にするチャンネル：** 色のチャンネルと透明度（-1）だけ。特色などの余分なチャンネルとマスクは、`ChannelImages` から自分で読む
- **パターンの画像（22）：** `psd.Patterns`（`Patt`・`Pat2`・`Pat3`）。RGB・グレースケール（8・16・32 ビット）とインデックス（8 ビット）、圧縮は生・RLE・ZIP。`Read()` は `LayerImage.Read()` と同じ 8 ビットの BGRA だけ（16 ビット・float で読む方法はない）
- **合成済みの画像：** `psd.ImageDataSection?.Image` を、レイヤーの画像と同じ 3 つの読み方で読む（節は初めて使うときに読むので、壊れていれば `ImageDataSection` を使うときに `InvalidPsdDataException` になる）。本物か（Photoshop の「互換性を優先」で保存したか）は `psd.HasRealMergedData` で確かめる。false なら、見た目はレイヤーから組み立てる必要がある

### 3 つの読み方の違い

| メソッド | 返すもの | 並び | 1 画素のバイト数 | 16・32 ビットの扱い |
|---|---|---|---|---|
| `Read()` | `byte[]` | B・G・R・A | 4 | 8 ビットに丸める。32 ビットの色はリニアから sRGB に直す |
| `ReadRgba16()` | `ushort[]` | R・G・B・A | 8 | 16 ビットのまま。8 ビットは ×257 で広げる。32 ビットの色は sRGB に直す |
| `ReadRgbaFloat(bool linear = true)` | `float[]` | R・G・B・A | 16 | 色は、ビット数によらずリニア（既定）か sRGB の明るさにそろえる。32 ビットの 0〜1 の外の値は残す |

- どれもストレートアルファ（色に透明度を掛けていない値）です。`ReadRgba16()`・`ReadRgbaFloat()` の並びは、SkiaSharp の `SKColorType.Rgba16161616`・`SKColorType.RgbaF32` にそのまま渡せます
- 32 ビットのファイルはリニア、8・16 ビットのファイルは sRGB の明るさで保存されているとみなします。埋め込まれた ICC プロファイルは使いません
- CMYK は、ICC プロファイルを使わない簡単な式で RGB にします（印刷の色とはずれます）
- `PsdLimits` の画素の数の上限は、3 つの読み方に同じに効きます。メモリは `Read()` の 2 倍（16 ビット）・4 倍（float）になるので、上限はそれを見込んで決めてください

### レイヤースタイルと塗りの不透明度

```csharp
using PsdParser.AdditionalLayerInformations;
using PsdParser.Descriptors;

foreach (var info in layer.AdditionalLayerInformations)
{
    if (info is ObjectBasedEffects effects)                // lfx2（複数の効果は lmfx）
    {
        Descriptor d = effects.Descriptor;                  // 壊れていれば、ここで InvalidPsdDataException
        if (d["DrSh"] is Descriptor shadow && shadow.Get<DescriptorBool>("enab")?.Value == true)
        {
            var distance = shadow.Get<DescriptorUnitFloat>("Dstn"); // Unit は "#Pxl"
        }
    }
    else if (info is FillOpacity fill)                      // iOpa（0〜255。データが空なら null）
    {
        byte? opacity = fill.Value;
    }
}
```

記述子は意味を解釈せず、キーと値の木のまま返します。効果のキー（`DrSh`・`OrGl`・`SoFi`・`GrFl` など）と、その中のキーは仕様書に書かれていないので、読む側で解釈してください。

### パターンの画像

```csharp
using PsdParser.AdditionalLayerInformations;

// 文書全体の Patt・Pat2・Pat3 のパターンを、ファイルの順に並べたもの（壊れていれば、ここで InvalidPsdDataException）
foreach (Pattern pattern in psd.Patterns)
{
    // pattern.Id（記述子の Ptrn の Idnt と同じ）・pattern.Name・pattern.Width・pattern.Height・pattern.ImageMode
    if (pattern.CanRead)                                    // RGB・グレースケール・インデックス
    {
        byte[] bgra = pattern.Read();                       // 8 ビットの BGRA（LayerImage.Read() と同じ形）
    }
}
```

- 色の配列は VMAL の番号（`PatternChannel.Index`）で選びます（色 i は番号 i）。透明度は、番号が色の数以上の配列のうち、いちばん小さい番号のものです（psd-tools と同じ結果になる読み方。Photoshop がどの配列に書くかは確かめていません）。インデックスのパターンは透明度を使いません
- `PsdFile` を Dispose したあとの `Read()` は `ObjectDisposedException` です
- 大きさの上限（`PsdLimits`）は、`Read()` のときに確かめます。上限を越えるパターンがあっても、一覧とほかのパターンは読めます
- Photoshop の実物で確かめたのは、8 ビットのインデックスで RLE のパターンだけです（22 の「未確認」）

### 例外

| 例外 | 起きるとき |
|---|---|
| `InvalidPsdDataException` | ファイルが壊れている、細工されている、`PsdLimits` を越える。ファイルを開くとき（ファイル全体）と、レイヤー・パターンを画像にするとき（そのレイヤー・パターンだけ）、レイヤースタイルの記述子・パターンの一覧を使うとき（それだけ）に起きる |
| `NotSupportedException` | 対応していないビット数・色の形式・圧縮 |
| `ObjectDisposedException` | `PsdFile` を Dispose したあとに、チャンネルを読んだ |

### スレッド

同じ `PsdFile` のレイヤーを、複数のスレッドから同時に画像にしてかまいません（ZIP の展開の途中の状態は、読み方を呼ぶたびに別々に持ちます）。`ChannelImageData.ReadLine` を直接呼ぶときは、ZIP のチャンネルの展開の状態が 1 つを共有するので、行を上から順に読むと速く読めます（前の行に戻ると、先頭から展開し直します）。

## Charmo の側でしていること（このフォークのコードは変えていない）

- `Microsoft.SourceLink.GitHub`（パッケージを作るときにだけ使う）の参照を、Charmo のビルドの設定で外している
- Charmo のパッケージの版の一元管理（Central Package Management）を、このフォークのビルドでは使わないようにしている
