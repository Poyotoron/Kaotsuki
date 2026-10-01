# OSC の仕様

送り手を使わずに、自作のツールから Kaotsuki を操作するための情報です。

## アドレス

送り先は VRChat の OSC 受信ポート（既定 `127.0.0.1:9000`）です。

| アドレス | 型 | 値 |
|---|---|---|
| `/avatar/parameters/Kaotsuki/Enabled` | Bool | ON / OFF |
| `/avatar/parameters/Kaotsuki/LipSync` | Bool | ON の間も口パクを動かすか |
| `/avatar/parameters/Kaotsuki/Index` | Int32 | 0〜255（1 枠目の番号。0 は対象なし） |
| `/avatar/parameters/Kaotsuki/Value` | Int32 | 0〜255（ウェイト 0〜100 に対応） |
| `/avatar/parameters/Kaotsuki/Index2`、`Index3` … | Int32 | 2 枠目以降の番号（登録数が 255 を超えたアバターのみ） |
| `/avatar/parameters/Kaotsuki/Value2`、`Value3` … | Int32 | 2 枠目以降の値 |

値とウェイトは `ウェイト = 値 × 100 ÷ 255` で対応します。ウェイト `w` を送るときは `値 = round(w × 255 ÷ 100)` にします。

## 送り方

1 本のブレンドシェイプ（スロット）を動かすときは、**`Index` → `Value` の順**に送ります。

!!! warning "順番を守ってください"
    `Value` を先に送ると、切り替える前のスロットに新しい値が書き込まれて、そのまま残ります。

- **同じスロットの値を更新するときも、毎回 `Index` → `Value` の組で送ってください。**
  アバター側の `Index` は、メニューで OFF にしたときやアバターを読み込み直したときに 0 へ戻ります。
- **別のスロットへ切り替えるときは、前の送信から一定時間あけてください**（送り手の既定は 250 ミリ秒）。
  同期は一定の間隔でしか行われないので、短い間に切り替えると他のプレイヤーに途中のスロットが届きません。
- 同じスロットの値の更新は、50 ミリ秒以上の間隔で送れば十分です。
- 255 本ごとに 1 組（枠）です。枠ごとの送り方は 1 枠目と同じで、**異なる枠は同時に送れます。**

## マップ

マップは `%LOCALAPPDATA%\Kaotsuki\Maps\<マップ名>.json` に保存されます（UTF-8）。

| キー | 内容 |
|---|---|
| `format` / `version` | `kaotsuki-map` / `1` |
| `mapName` | マップ名 |
| `avatars[]` | このマップを使うアバター（`name`、`blueprintId`） |
| `parameters.enabled` / `parameters.lipSync` | ON / OFF・口パクのパラメータ名 |
| `parameters.channels[]` | 枠ごとの `index` / `value` のパラメータ名 |
| `slots[].channel` / `slots[].index` | そのブレンドシェイプの枠（1 から）と番号（1〜255） |
| `slots[].mesh` / `slots[].blendShape` | アバターのルートからのメッシュのパスと、ブレンドシェイプ名 |
| `slots[].defaultWeight` | 既定値（0〜100） |
| `slots[].group` | 区切りのグループ名（最初の区切りより前は空） |

パラメータ名は、アドレスを決め打ちせずにマップの `parameters` から読むことをおすすめします。

## 表情ファイル

`format` が `kaotsuki-expression` の JSON です。`blendShapes[]` に `mesh`、`blendShape`、`weight`（0〜100）を持ちます。
