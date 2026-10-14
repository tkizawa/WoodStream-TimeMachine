# WoodStream TimeMachine

Copilot+ PC の「Recall」機能にインスパイアされた、Windows 向けの軽量・高速な画面キャプチャ＆タイムライン閲覧アプリケーションです。

5秒ごとにバックグラウンドで画面を静かに記録し、タスクトレイからいつでも呼び出せるタイムラインビューアーを使って、過去の画面をパラパラ漫画のように瞬時に遡ることができます。

---

## 🌟 主な機能・特徴

1. **初回起動時の簡単セットアップ & 詳細設定**
   - 画像の保存先フォルダを指定（デフォルト: `マイピクチャ\WoodStreamTimeMachineImage`）。
   - 記録間隔（秒、デフォルト3秒）、保存容量上限（GB、デフォルト25GB、10GB〜150GBの10GB刻みで変更可能）、Windows起動時の自動起動を設定可能。
   - 設定ファイルは `AppData\Local\WoodStreamTimeMachine\settings.json` に可視UTF-8形式で安全に保存され、次回以降自動で読み込まれます。

2. **タスクトレイ（システムトレイ）常駐**
   - 起動時は邪魔にならないようタスクトレイに常駐。
   - アイコンのダブルクリックまたは右クリックメニューから「ビューアーを開く」「設定」「終了」を即座に実行可能。

3. **可変間隔の高速バックグラウンドキャプチャ（デフォルト3秒）**
   - メインUIを一切ブロックしない完全非同期ループ（`PeriodicTimer`）。
   - 設定画面で記録間隔を自由に変更可能（変更は即座に反映）。
   - ビュアー表示中（画面上に開いている間）はスクリーンショットの記録を自動停止し、無限再帰キャプチャを防止。
   - 高圧縮JPEG（品質80%）により、高画質を維持しながらファイルサイズを極小化。
   - マルチモニタ・高DPI（Per-Monitor V2）環境に対応し、画面スケーリングによるズレを防止。

4. **1時間ごとの保存容量制限チェック（デフォルト25GB）**
   - 1時間ごとに保存先フォルダの画像容量を判定。
   - 設定された容量上限（デフォルト25GB、10GB〜150GBまで10GB刻みで変更可能）を超過した場合、古いスクリーンショットから順に自動削除して容量内に収めます。

5. **1時間に1回の重複・微小差分自動間引き（ストレージ最適化）**
   - 過去1時間分の画像を時系列で比較し、放置状態や静止画面などの「ほぼ同じ画面」を自動検出して間引き削除。
   - 16×16のグレースケール縮小フィンガープリント比較により、マウスポインタの点滅や時計の秒数変化などのノイズを吸収しつつ、ユーザーの操作変化点のみを的確に残します。
   - ビューアー画面から「重複間引き」ボタンで手動実行も可能。

6. **Windows起動時の自動起動（スタートアップ設定）**
   - 設定画面から「Windows 起動時に自動起動する」をチェックするだけで、PC起動時にバックグラウンドで自動起動。

7. **パラパラ漫画のように滑らかなタイムラインビューアー**
   - タスクトレイからワンクリックで呼び出し可能。
   - 下部の横長スライダーをドラッグするだけで、保存された画像が時系列順に瞬時に切り替わります。
   - `BitmapCacheOption.OnLoad` と `Freeze()` により、ファイルロックを解放した上でUI描画を高速化。
   - 画面左上に撮影日時およびインデックス番号をオーバーレイ表示。
   - ビュアー表示中は上部に「ビュアー表示中（キャプチャ停止中）」のインジケーターを表示。
   - アプリ終了時のウィンドウ位置およびサイズを自動保存し、次回起動時に正確に復元。

8. **Windows標準ローカルOCR文字記録 ＆ タイムライン高速検索**
   - Windows 10/11 標準搭載の `Windows.Media.Ocr.OcrEngine` を活用し、画面上の文字を完全ローカル・通信不要・高精度にテキスト抽出。
   - キャプチャ画像を待たせない非同期キューイングにより、バックグラウンドで低負荷に順次テキスト化。
   - 抽出データは画像保存先フォルダ直下の `DB/timeline_ocr.db`（SQLite）に自動保存。
   - ビューアー上部の検索バーからキーワードを入力するだけで、該当する過去の画面を瞬時に検索・リストアップ。
   - 検索結果をクリックするとそのフレームへ一発ジャンプできるほか、タイムラインスライダー上にもヒット箇所が視覚的なマーカードットとして表示されます。
   - 設定画面で「OCR文字記録を有効にする」のON/OFF切り替えも可能。

9. **多言語対応**
   - Windows の表示言語設定に応じて、日本語と英語が自動的に切り替わります。

---

## ⌨️ ビューアーのショートカットキー

| キー | 動作 |
| :--- | :--- |
| <kbd>Space</kbd> | タイムラインの自動再生 / 一時停止 |
| <kbd>←</kbd> | 1コマ戻る（前のキャプチャへ） |
| <kbd>→</kbd> | 1コマ進む（次のキャプチャへ） |
| <kbd>F5</kbd> | 保存先フォルダ内の画像を再読み込み（更新） |
| <kbd>Ctrl</kbd> + <kbd>F</kbd> | 検索ボックスにフォーカス |
| <kbd>Enter</kbd> (検索中) | 入力したキーワードでOCR検索を実行 |
| <kbd>F3</kbd> / <kbd>Shift</kbd> + <kbd>F3</kbd> | 検索結果の次 / 前のフレームへジャンプ |
| <kbd>Esc</kbd> | 検索結果パネルを閉じる |

---

## 🛠️ 動作環境・技術スタック

- **OS**: Windows 10 / Windows 11 (x64, Arm64)
- **言語**: C#
- **フレームワーク**: .NET 10.0 (`net10.0-windows10.0.19041.0`)
- **UI フレームワーク**: WPF (Windows Presentation Foundation)
- **OCR エンジン**: Windows 標準 `Windows.Media.Ocr.OcrEngine`（完全ローカル実行）
- **データベース**: SQLite (`Microsoft.Data.Sqlite`)（保存先: `StorageFolder\DB\timeline_ocr.db`）
- **高DPI対応**: Per-Monitor V2

---

## 📂 プロジェクト構成

```plaintext
WoodStream-TimeMachine/
├── Assets/
│   └── app.ico                  # アプリケーションおよびタスクトレイ用アイコン
├── Models/
│   └── AppSettings.cs          # アプリケーション設定データモデル
├── Resources/
│   └── Strings.cs              # 多言語ローカライゼーションテキスト
├── Services/
│   ├── CaptureService.cs       # 画面自動キャプチャサービス（可変間隔）
│   ├── DeduplicationService.cs # 1時間ごとの重複間引き・類似度判定サービス
│   ├── StorageCleanupService.cs # 1時間ごとのフォルダ容量制限監視・クリーンアップサービス
│   ├── OcrDatabaseService.cs   # SQLiteによるOCRテキスト保存・検索サービス（DBサブフォルダ内）
│   ├── OcrService.cs           # Windows.Media.Ocrを用いたバックグラウンドテキスト抽出サービス
│   ├── StartupService.cs       # Windows起動時自動起動管理サービス
│   ├── SettingsManager.cs      # 設定ファイルの入出力・永続化管理
│   └── TrayIconService.cs      # タスクトレイアイコンおよびコンテキストメニュー管理
├── Views/
│   ├── SetupWindow.xaml        # 初回セットアップ・設定ダイアログ
│   └── SetupWindow.xaml.cs
├── App.xaml / App.xaml.cs       # アプリケーションエントリポイント・常駐ライフサイクル管理
├── MainWindow.xaml / .cs        # タイムラインビューアー＆テキスト検索画面
├── WoodStreamTimeMachine.csproj # プロジェクトファイル (.NET 10)
├── app.manifest                 # アプリケーションマニフェスト (Per-Monitor V2)
└── README.md                    # 本ドキュメント
```

---

## 🚀 ビルドと実行方法

### 前提条件
- [.NET 10 SDK](https://dotnet.microsoft.com/download) がインストールされていること。

### ビルド
```powershell
dotnet build -c Release -r win-x64
```

### 実行
```powershell
dotnet run
```

起動すると、初回時は保存先フォルダの確認ダイアログが表示されます。「保存して開始」を押すとタスクトレイに格納され、自動キャプチャが開始されます。

### スタンドアロンインストーラー（x64 / Arm64）

.NET 10 SDK と Inno Setup 6（6.3 以降）が必要です。次のコマンドでランタイム同梱のアプリを発行し、アーキテクチャ別の EXE インストーラーを生成します。`$iscc` は実際のインストール先に合わせて変更してください。

```powershell
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
foreach ($arch in @("x64", "arm64")) {
    dotnet publish .\WoodStreamTimeMachine.csproj -c Release -r "win-$arch" --self-contained true -p:PublishSingleFile=true -o ".\publish\$arch"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $arch" }
    & $iscc "/DMyAppArch=$arch" .\installer.iss
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed: $arch" }
}
```

出力先は `Installer\WoodStreamTimeMachine-Setup-<バージョン>-x64.exe` および `Installer\WoodStreamTimeMachine-Setup-<バージョン>-arm64.exe` です。バージョンは発行したアプリのファイルバージョンから取得します。利用先での .NET ランタイムの別途インストールは不要です。Arm64 版は Arm64 Windows、x64 版は x64 アプリを実行できる Windows が対象です。
