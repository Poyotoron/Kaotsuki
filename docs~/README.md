# ドキュメントサイト

このフォルダには、GitHub Pages で公開する Kaotsuki のドキュメント一式があります。MkDocs Material で作成しています。

以下のコマンドは、すべて PowerShell でリポジトリのルート（`package.json` がある階層）から実行してください。

## 仮想環境のアクティベート

```powershell
& "$env:USERPROFILE\.venvs\vpm-docs\Scripts\Activate.ps1"
```

成功すると、プロンプトの先頭に `(vpm-docs)` が付きます。

<details>
<summary>実行ポリシーによりアクティベートできない場合</summary>

現在の PowerShell プロセスだけ実行ポリシーを変更してから、もう一度アクティベートします。

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

または、仮想環境をアクティベートせずに MkDocs を直接実行できます。

```powershell
& "$env:USERPROFILE\.venvs\vpm-docs\Scripts\mkdocs.exe" serve -f "docs~/mkdocs.yml"
```

</details>

## プレビュー

```powershell
mkdocs serve -f "docs~/mkdocs.yml"
```

ブラウザで `http://127.0.0.1:8000/` を開きます。ファイルを保存するとページが自動で再読み込みされます。停止するには `Ctrl + C` を押してください。

## 公開前の確認（CI と同じ条件）

```powershell
$env:KAOTSUKI_VERSION = "0.3.0"
mkdocs build --strict -f "docs~/mkdocs.yml"
```

警告が 1 つでもあるとビルドは失敗します。`KAOTSUKI_VERSION` はフッタの「対応バージョン」に使われ、渡さなければ `dev` と表示されます。公開時は Actions が `package.json` の `version` を読み取って渡すため、ページ側にバージョン番号を書かないでください。

## 終了

```powershell
deactivate
```

## 目視で確認したいこと

| 見る場所 | 確認する内容 |
|---|---|
| 右上のテーマ切り替え | ライト／ダークの両方で見出し・リンクが読めるか |
| ヘッダ | ピンクの背景に白文字が乗って読めるか |
| 検索ボックス | 日本語（例:「区切り」「口パク」）で結果が出るか |
| 注記ブロック | 警告（橙）と補足（青）が色で区別できるか |
| フッタ | 「対応バージョン: …」が出ているか |

## 環境の作成（初回のみ）

ドキュメント用の環境は兄弟リポジトリと共用し、リポジトリごとには作りません。

```powershell
uv venv --python 3.13 "$env:USERPROFILE\.venvs\vpm-docs"
$env:VIRTUAL_ENV = "$env:USERPROFILE\.venvs\vpm-docs"
uv pip install -r "docs~/requirements.txt"
```

作成先を Unity プロジェクト内にすると Unity が取り込んでしまうため、ユーザー領域に作成します。Python 3.13 を指定するのは、Actions と実行環境を揃えるためです。

## 注意

- ビルド成果物の `docs~/site/` はコミットしないでください。
- **フォルダ名の末尾の `~` を消さないでください。** Unity は末尾が `~` のフォルダを取り込まないため、配下に `.meta` が作られません。改名すると `.meta` が大量に生成され、配布物にも混ざります。
- 画像は使わず、表と注記ブロックで説明してください。
