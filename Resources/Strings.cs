using System.Globalization;

namespace WoodStreamTimeMachine.Resources;

/// <summary>
/// アプリケーション全体の多言語テキストを提供するローカライゼーションクラス。
/// システムの表示言語モード（日本語・英語）に応じて適切なテキストを返します。
/// </summary>
public static class Strings
{
    private static bool IsJapanese => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);

    // アプリ一般
    public static string AppTitle => IsJapanese ? "WoodStream TimeMachine" : "WoodStream TimeMachine";
    public static string ViewerTitle => IsJapanese ? "WoodStream TimeMachine - タイムラインビューアー" : "WoodStream TimeMachine - Timeline Viewer";

    // トレイメニュー
    public static string TrayOpenViewer => IsJapanese ? "ビューアーを開く" : "Open Viewer";
    public static string TraySettings => IsJapanese ? "設定（保存先変更）" : "Settings";
    public static string TrayExit => IsJapanese ? "終了" : "Exit";
    public static string TrayRunningTooltip => IsJapanese ? "WoodStream TimeMachine (動作中)" : "WoodStream TimeMachine (Running)";

    // セットアップ / 設定画面
    public static string SetupTitle => IsJapanese ? "初回セットアップ - 基本設定" : "Initial Setup - Preferences";
    public static string SettingsTitle => IsJapanese ? "設定" : "Settings";
    public static string SetupDescription => IsJapanese 
        ? "画面キャプチャ画像の保存先、記録間隔、最大保存容量を設定してください。\nタスクトレイに常駐し、指定した間隔で自動的にスクリーンショットを記録します。" 
        : "Configure the storage folder, capture interval, and maximum storage limit.\nRuns in the system tray and captures screenshots automatically.";
    public static string StorageFolderLabel => IsJapanese ? "画像保存先フォルダ:" : "Storage Folder:";
    public static string BrowseButton => IsJapanese ? "参照..." : "Browse...";
    public static string CaptureIntervalLabel => IsJapanese ? "記録間隔 (秒):" : "Capture Interval (sec):";
    public static string SecondsUnit => IsJapanese ? "秒" : "sec";
    public static string StorageLimitLabel => IsJapanese ? "保存容量上限 (GB):" : "Storage Limit (GB):";
    public static string StorageLimitDescription => IsJapanese 
        ? "※1時間ごとに容量を判定し、上限超過時は古い画像から自動削除します。" 
        : "* Checked hourly; oldest captures are deleted when exceeded.";
    public static string StartWithWindowsLabel => IsJapanese ? "Windows 起動時に自動起動する" : "Start automatically with Windows";
    public static string EnableOcrLabel => IsJapanese ? "OCR文字記録を有効にする (タイムライン検索用)" : "Enable OCR text capture (for timeline search)";
    public static string SaveAndStartButton => IsJapanese ? "保存して開始" : "Save & Start";
    public static string SaveButton => IsJapanese ? "保存" : "Save";
    public static string CancelButton => IsJapanese ? "キャンセル" : "Cancel";
    public static string FolderNotSelectedError => IsJapanese ? "有効なフォルダパスを指定してください。" : "Please select a valid folder path.";
    public static string FolderCreateFailedError => IsJapanese ? "指定されたフォルダの作成に失敗しました。" : "Failed to create the specified folder.";
    public static string InvalidIntervalError => IsJapanese ? "記録間隔には1以上の整数値を指定してください。" : "Please enter an integer of 1 or greater for the capture interval.";
    public static string InvalidStorageLimitError => IsJapanese ? "保存容量上限には1以上の整数値を指定してください。" : "Please enter an integer of 1 or greater for the storage limit.";

    // ビューアー画面
    public static string NoCapturesFound => IsJapanese ? "キャプチャ画像が見つかりません。撮影開始をお待ちください。" : "No captures found. Waiting for new screenshots...";
    public static string LoadingImage => IsJapanese ? "読み込み中..." : "Loading...";
    public static string PlayButtonTooltip => IsJapanese ? "タイムライン再生 / 一時停止 (Space)" : "Play / Pause Timeline (Space)";
    public static string PrevButtonTooltip => IsJapanese ? "1コマ戻る (←)" : "Previous Frame (Left Arrow)";
    public static string NextButtonTooltip => IsJapanese ? "1コマ進む (→)" : "Next Frame (Right Arrow)";
    public static string RefreshButtonTooltip => IsJapanese ? "最新の画像を再読込 (F5)" : "Reload Images (F5)";
    public static string RunDeduplicationButton => IsJapanese ? "今すぐ最適化 (重複間引き)" : "Optimize Now (Deduplicate)";
    public static string OptimizationDoneMessage => IsJapanese ? "{0} 枚の重複・微小差分画像を間引きました。" : "Cleaned up {0} duplicate / nearly identical captures.";
    public static string OptimizationResultTitle => IsJapanese ? "重複間引き" : "Clean Up";
    public static string OptimizationResultMessage => IsJapanese
        ? "重複間引きが完了しました。\n\n削除枚数: {0} 枚\n削減容量: {1}"
        : "Clean up completed.\n\nDeleted captures: {0}\nSpace freed: {1}";
    public static string OptimizationFailedMessage => IsJapanese ? "重複間引き中にエラーが発生しました。\n\n{0}" : "An error occurred during clean up.\n\n{0}";
    public static string CapturePausedBadge => IsJapanese ? "ビュアー表示中（キャプチャ停止中）" : "Recording paused while viewer is open";
    public static string SearchPlaceholder => IsJapanese ? "画面内の文字を検索 (Enter)..." : "Search screen text (Enter)...";
    public static string SearchButtonText => IsJapanese ? "検索" : "Search";
    public static string ClearSearchTooltip => IsJapanese ? "検索をクリア" : "Clear search";
    public static string SearchResultsCount => IsJapanese ? "{0} 件見つかりました" : "Found {0} matches";
    public static string NoSearchResults => IsJapanese ? "一致する画面が見つかりませんでした" : "No matching screens found";
    public static string SearchPanelTitle => IsJapanese ? "検索結果" : "Search Results";
    public static string CloseSearchPanelTooltip => IsJapanese ? "検索結果を閉じる (Esc)" : "Close search results (Esc)";
}
