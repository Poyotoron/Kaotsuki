# インストール

## 動作環境

| 項目 | 必要なもの |
|---|---|
| Unity | 2022.3 |
| VRChat SDK - Avatars | 3.7.0 以降 |
| Modular Avatar | 1.17.0 以上、2.0.0 未満 |
| NDMF | 1.14.0 以上、2.0.0 未満 |
| 送り手 | Windows 10 / 11、Microsoft Edge WebView2 ランタイム（Windows 11 は標準搭載） |

任意で、次のツールが入っていれば連携します（入っていなくても使えます）。

| ツール | 連携内容 |
|---|---|
| AAO Avatar Optimizer | Freeze BlendShapes で固定するブレンドシェイプを操作の対象から外します |
| Avatar Blink Fix | 顔のメッシュの修正が終わった後で Kaotsuki を組み込みます |

詳しくは [他のツールとの併用](other-tools.md) を参照してください。

## VCC / ALCOM から入れる（おすすめ）

1. VCC（または ALCOM）にこのパッケージのリポジトリを追加します。
2. アバターのプロジェクトの **Manage Project** を開き、`Kaotsuki` を追加します。

VRChat SDK・Modular Avatar・NDMF は依存パッケージとして自動で追加されます。
**送り手アプリ（`KaotsukiSender.exe`）もパッケージに同梱されている**ので、別途ダウンロードする必要はありません。

## `.unitypackage` から入れる

VCC を使わない場合は、[Releases](https://github.com/Poyotoron/Kaotsuki/releases) から `.unitypackage` をダウンロードして Unity にインポートします。

!!! warning "依存パッケージと送り手は別途用意してください"
    - VRChat SDK・Modular Avatar・NDMF は自動で入りません。先に導入してください。
    - `.unitypackage` には送り手アプリが含まれません。同じ Release に添付されている `KaotsukiSender.exe` をダウンロードし、好きな場所に置いて使ってください。

## 送り手アプリについて

送り手はインストール不要の単体アプリです。起動方法は次の 3 つがあります。

| 起動方法 | 開くマップ |
|---|---|
| 受け手の Inspector の `送り手を起動` | そのアバターのマップ |
| Unity のメニュー `Tools > Kaotsuki > Open Sender` | 前回開いたマップ |
| `KaotsukiSender.exe` を直接ダブルクリック | 前回開いたマップ |

VCC から入れた場合、exe はパッケージの中の `Bin~/KaotsukiSender.exe` にあります。
Unity を開かずに使うときは、この exe をデスクトップなどにショートカットしておくと便利です。
