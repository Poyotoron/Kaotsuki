# Kaotsuki

Kaotsuki は、VRChat の外からアバターの表情（ブレンドシェイプ）を OSC で 1 本ずつ直接動かせるツールです。
アバターに組み込む受け手の Unity パッケージと、スライダーで操作する Windows 用の送り手アプリで構成されています。

## 特徴

- **非破壊**: 元のアバターを書き換えず、ビルド時に必要なレイヤーとパラメータを組み込みます。
- **かんたん Setup**: Hierarchy でアバターを右クリックするだけで受け手を追加できます。
- **対象を選べる**: Inspector で操作するブレンドシェイプを選べ、すべてをまとめて ON / OFF にできます。
- **表情を占有**: ON の間はジェスチャーやまばたき、リップシンクに邪魔されず、Kaotsuki だけで登録した表情を操作できます。
- **同期パラメータは必要な分だけ**: 255 本までは 17 bit。255 本を超えると 255 本ごとに 16 bit ずつ増えます（最大 3825 本）。
- **単体アプリ**: 送り手はインストール不要の単体 exe で、Unity からも起動できます。

## 動作環境

- Unity 2022.3
- VRChat SDK - Avatars 3.7.0 以降
- Modular Avatar 1.17.0 以降
- NDMF 1.14.0 以降
- 送り手: Windows 10 / 11、Microsoft Edge WebView2 ランタイム（Windows 11 は標準搭載）

## インストール

VPM に対応した VCC または ALCOM のプロジェクトへ、このパッケージを追加してください。
VRChat SDK、Modular Avatar、NDMF は依存パッケージとして自動で追加されます。

## 使い方

1. Hierarchy でアバターを右クリックし、`Kaotsuki` → `Setup` を選びます。
2. 作成された `Kaotsuki` の Inspector で、操作するメッシュとブレンドシェイプを選びます。
3. アバターをアップロードします。
4. VRChat の Action Menu → Options → OSC → `Enabled` で OSC を有効にします。
5. Expression Menu の `Kaotsuki` を ON にします。
6. Unity の Inspector にある `送り手を起動` を押します。`Tools > Kaotsuki > Open Sender` または `Bin~/KaotsukiSender.exe` からも起動できます。
7. 送り手でアバターのマップを選び、スライダーを動かします。

## 送り手の操作

| 操作 | 動作 |
|---|---|
| `ON` | アバター側の受け手を ON にします。 |
| `OFF` | 受け手を OFF にし、表情とスライダーを既定値へ戻します。 |
| `リセット` | 一度 OFF にして既定値へ戻し、保持時間後に ON にします。 |
| `↺` | そのブレンドシェイプを既定値へ戻します。 |
| 検索 | 名前の一部からブレンドシェイプを絞り込みます。 |
| `⚙` | OSC の送信先と、スロットを切り替えるまでの保持時間を設定します。 |

## 注意

> ON の間は、登録したブレンドシェイプが**すべて** Kaotsuki の値で上書きされます。体型や衣装用の縮小など、表情以外で触られたくないものは Inspector で OFF にしてください。

- 登録を変えたら**アップロードし直してください**。マップは Unity でビルドするたびに更新されますが、VRChat 上のアバターはアップロードするまで古いままです。
- 登録数が多いと同期パラメータを多く使います。VRChat の上限（256 bit）は他のギミックと共有なので、表情に使わないブレンドシェイプは Inspector で OFF にしてください。
- パラメータを追加したアバターを上書きアップロードして OSC が効かない場合は、Action Menu → Options → OSC → `Reset Config` を実行してください。
- 他のプレイヤーからの見え方は同期の間隔に左右されます。複数のスライダーを動かすと、反映まで少し時間がかかります。

## OSC の仕様

自作ツールから送信する場合は、次のアドレスを使います。

| アドレス | 型 | 値 |
|---|---|---|
| `/avatar/parameters/Kaotsuki/Enabled` | Bool | ON / OFF |
| `/avatar/parameters/Kaotsuki/Index` | Int32 | 0〜255（0 は対象なし） |
| `/avatar/parameters/Kaotsuki/Value` | Int32 | 0〜255（ウェイト 0〜100 に対応） |
| `/avatar/parameters/Kaotsuki/Index2`、`Index3` … | Int32 | 2 枠目以降の番号（登録数が 255 を超えたアバターのみ） |
| `/avatar/parameters/Kaotsuki/Value2`、`Value3` … | Int32 | 2 枠目以降の値 |

255 本ごとに 1 組（枠）で、枠ごとの送り方は 1 枠目と同じです。異なる枠は同時に送れます。

値を送るときは、同じスロットの更新でも毎回 `Index`、`Value` の順に送信してください。
アバター側の `Index` は、メニューで OFF にしたときやアバターを読み込み直したときに 0 へ戻ります。
別のスロットへ切り替えるときは、前の送信から一定時間空けてください（送り手の既定の保持時間は 250ms です）。

マップは `%LOCALAPPDATA%\Kaotsuki\Maps\<アバター名>.json` に保存されます。
主なキーは `parameters.channels`、`slots[].channel`、`slots[].index`、`mesh`、`blendShape`、`defaultWeight` です。

## 送り手のビルド

Rust と `tauri-cli` 2 系を用意し、`Sender~/src-tauri` で次を実行します。

```powershell
cargo tauri build --no-bundle
```

生成された `target/release/kaotsuki-sender.exe` を `Bin~/KaotsukiSender.exe` に配置してください。

## ライセンス

MIT License. 詳細は [LICENSE](LICENSE) を参照してください。
