# Charmo 用の変更（charmo ブランチ）

このフォーク（yunoka/PsdParser）の `charmo` ブランチは、上流（[manju-summoner/PsdParser](https://github.com/manju-summoner/PsdParser)）の `master` に、Charmo（ロゴアニメーションの制作ツール）で使うための変更を積んだものです。上流との差分をいつでも説明でき、上流へフィードバックできる状態を保つために、変更をすべてここに書きます（フィードバックするかは、その都度決めます）。

上流との差分は、この一覧に必ず書きます（変更ごとに 1 項目。新しいものを下に足す）。

| # | 日付 | 変更 | 理由 | ファイル | 上流でも役立つか |
|---|---|---|---|---|---|
| 1 | 2026-11-15 | 対象のフレームワークを `net8.0` から `net10.0` に変える | Charmo は .NET 10 だけでビルドし、net8.0 の参照パックに頼らない方針のため。外側のビルドの設定（`Directory.Build.props` など）だけでは、csproj に書かれた単数形の `TargetFramework` を変えられなかった | `PsdParser/PsdParser.csproj` | いいえ（Charmo の都合）。上流に合わせるなら `net8.0;net10.0` の複数指定にする |

## Charmo の側でしていること（このフォークのコードは変えていない）

- `Microsoft.SourceLink.GitHub`（パッケージを作るときにだけ使う）の参照を、Charmo の `external/Directory.Build.targets` で外している
- Charmo のパッケージの版の一元管理（Central Package Management）を、`external/Directory.Packages.props` で切っている

## これから足す予定の変更

Charmo の設計文書（`docs/design/psd-import.md` の「フォークに当てるパッチ」）を参照。予測付きの ZIP 圧縮、合成済みの画像の読み込み、壊れた・細工したファイルへの備え、チャンネルから RGBA への変換とグループの木。
