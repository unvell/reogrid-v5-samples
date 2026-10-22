# ReoGrid One (V5) Samples

Sample projects for **ReoGrid One** — the V5 generation of the ReoGrid .NET spreadsheet component
(WinForms / WPF / Avalonia / headless, .NET 10).

ReoGrid One（ReoGrid V5）のサンプルプロジェクト集です。

> This repository contains sample code only. ReoGrid One itself is commercial software, distributed
> from the UNVELL package feed under the [ReoGrid license agreement](https://reogrid.net/terms).
>
> 本リポジトリにはサンプルコードのみを含みます。ReoGrid One 本体は有償製品で、UNVELL のパッケージ
> フィードから配布しています。

## Samples / サンプル一覧

| Platform | Sample | What it shows |
|---|---|---|
| WinForms | [LiveMarketGrid](WinForms/LiveMarketGrid) | A ticking market-data monitor drawn with Direct2D or GDI+ (switchable): 3 sheets × 100,000 rows × 20 columns with grouped headers, merged group rows, per-column fonts, cell types and frozen panes, a choice of update patterns ticking up to every frame, and a benchmark comparing the two renderers. Direct2D と GDI+ を切り替えられる相場表（3 シート × 10 万行 × 20 列）。更新パターンと毎フレーム更新、両方式の描画時間を計測 |
| Avalonia | [LiveMarketGrid](Avalonia/LiveMarketGrid) | The same market monitor on Avalonia (Windows / macOS / Linux), with a choice of update patterns (market feed, a clock column plus a few prices, every visible cell) and repaint calls, ticking up to every frame, and a benchmark of each. Avalonia 版。更新パターンと再描画の呼び方を切り替えて、毎フレーム更新までの描画コストを計測 |

Samples are grouped by platform (`WinForms/`, `WPF/`, `Avalonia/`, `Headless/`); each folder is a
self-contained project with its own README.

サンプルはプラットフォームごとのフォルダー（`WinForms/` `WPF/` `Avalonia/` `Headless/`）に分け、
それぞれが単独でビルドできるプロジェクトです。

## Getting started / はじめに

1. Install the **.NET 10 SDK** (10.0.301 or later).
2. Register the UNVELL package feed once — ReoGrid One is not on nuget.org. Create an access token
   in the [portal](https://portal.unvell.com/access-tokens); the same page shows the command to
   register the feed. See [Installation](https://reogrid.net/docs/v5/getting-started/installation)
   for details.
3. Run a sample:

   ```bash
   dotnet run -c Release --project WinForms/LiveMarketGrid
   ```

A license key is optional for the samples. Without one the grid runs read-only with an evaluation
watermark. To apply yours, set the `REOGRID_LICENSE` environment variable or put the key in
`%LOCALAPPDATA%\unvell\reogrid\license.txt` — never commit a key to source control.

ライセンスキーが無くてもサンプルは動きます（読み取り専用・評価版の透かし付き）。キーを使う場合は
環境変数 `REOGRID_LICENSE`、または `%LOCALAPPDATA%\unvell\reogrid\license.txt` に置いてください。

The ReoGrid version every sample builds against is set once in [`Directory.Build.props`](Directory.Build.props)
(`ReoGridVersion`), and can be overridden with `-p:ReoGridVersion=…`.

## Questions / お問い合わせ

Sign in to the [UNVELL portal](https://portal.unvell.com) and open a ticket under
[Tickets](https://portal.unvell.com/tickets) — or reply to the email thread you already have with us.

[ポータル](https://portal.unvell.com)にログインし、[チケット](https://portal.unvell.com/tickets)からご質問いただけます。

---

Copyright (c) 2026 UNVELL Inc. Sample code is licensed under the [MIT License](LICENSE).
