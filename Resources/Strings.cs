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
    public static string SetupTitle => IsJapanese ? "初回セットアップ - 保存先フォルダの選択" : "Initial Setup - Select Storage Folder";
    public static string SettingsTitle => IsJapanese ? "設定 - 保存先フォルダの変更" : "Settings - Change Storage Folder";
    public static string SetupDescription => IsJapanese 
        ? "画面キャプチャ画像を記録・保存するフォルダを選択してください。\n指定したフォルダに5秒ごとのスクリーンショットが保存されます。" 
        : "Please select the folder to store screen capture images.\nScreenshots will be saved every 5 seconds to this location.";
    public static string StorageFolderLabel => IsJapanese ? "画像保存先フォルダ:" : "Storage Folder:";
    public static string BrowseButton => IsJapanese ? "参照..." : "Browse...";
    public static string SaveAndStartButton => IsJapanese ? "保存して開始" : "Save & Start";
    public static string SaveButton => IsJapanese ? "保存" : "Save";
    public static string CancelButton => IsJapanese ? "キャンセル" : "Cancel";
    public static string FolderNotSelectedError => IsJapanese ? "有効なフォルダパスを指定してください。" : "Please select a valid folder path.";
    public static string FolderCreateFailedError => IsJapanese ? "指定されたフォルダの作成に失敗しました。" : "Failed to create the specified folder.";

    // ビューアー画面
    public static string NoCapturesFound => IsJapanese ? "キャプチャ画像が見つかりません。撮影開始をお待ちください。" : "No captures found. Waiting for new screenshots...";
    public static string LoadingImage => IsJapanese ? "読み込み中..." : "Loading...";
    public static string PlayButtonTooltip => IsJapanese ? "タイムライン再生 / 一時停止 (Space)" : "Play / Pause Timeline (Space)";
    public static string PrevButtonTooltip => IsJapanese ? "1コマ戻る (←)" : "Previous Frame (Left Arrow)";
    public static string NextButtonTooltip => IsJapanese ? "1コマ進む (→)" : "Next Frame (Right Arrow)";
    public static string RefreshButtonTooltip => IsJapanese ? "最新の画像を再読込 (F5)" : "Reload Images (F5)";
    public static string RunDeduplicationButton => IsJapanese ? "今すぐ最適化 (重複間引き)" : "Optimize Now (Deduplicate)";
    public static string OptimizationDoneMessage => IsJapanese ? "{0} 枚の重複・微小差分画像を間引きました。" : "Cleaned up {0} duplicate / nearly identical captures.";
}
