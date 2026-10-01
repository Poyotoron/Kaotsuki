# 他のツールとの併用

Kaotsuki は次のツールと一緒に使えます。どれも**必須ではなく**、入っていれば自動で連携します。

## AAO Avatar Optimizer

AAO Avatar Optimizer の **Freeze BlendShapes** で固定するブレンドシェイプは、ビルド時にメッシュへ焼き込まれて消えるため、動かせません。

Kaotsuki は固定されるブレンドシェイプを自動で見つけ、操作の対象から外します。

- 受け手の一覧に `（固定）` と表示され、チェックは操作できません。
- 登録数・マップ・送り手のスライダーにも入りません。
- Freeze BlendShapes から外すと、元の ON / OFF の状態で一覧に戻ります。

!!! tip "顔改変で使ったブレンドシェイプを固定している場合"
    顔の形を変えるために値を入れたブレンドシェイプを固定していても、Kaotsuki がそれを動かすことはありません。
    固定するものを変えたら、Kaotsuki の Inspector を開き直すと表示に反映されます。

## Avatar Blink Fix

Avatar Blink Fix は、ビルド時に顔のメッシュを修正します。
Kaotsuki は、**Avatar Blink Fix の修正が終わった後で**組み込まれるので、修正後の顔のまま表情を操作できます。

## Modular Avatar のコンポーネントで既定値を変えている場合

Modular Avatar の Shape Changer などで、ブレンドシェイプの値をビルド時に変えている場合、
Kaotsuki はその変更を既定値として扱えません（Kaotsuki が先に組み込まれるため）。
Kaotsuki を ON にすると、そのブレンドシェイプは変更前の値になります。

!!! warning "ビルド時に値を変えているブレンドシェイプは OFF にしてください"
    表情として動かす必要が無ければ、受け手の一覧で OFF にしてください。Kaotsuki が触らなくなります。
