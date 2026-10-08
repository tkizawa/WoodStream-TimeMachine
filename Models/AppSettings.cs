namespace WoodStreamTimeMachine.Models;

/// <summary>
/// アプリケーションの設定情報を保持するデータモデル
/// </summary>
public class AppSettings
{
    /// <summary>
    /// キャプチャ画像の保存先ディレクトリパス
    /// </summary>
    public string StorageFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// キャプチャ間隔（秒） デフォルト5秒
    /// </summary>
    public int CaptureIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// 重複画像の最適化間隔（分） デフォルト60分（1時間）
    /// </summary>
    public int OptimizationIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 初回起動フラグ（保存先フォルダ初期設定ダイアログの表示判定）
    /// </summary>
    public bool IsFirstRun { get; set; } = true;

    /// <summary>
    /// ビューアーウィンドウの位置（X座標）
    /// </summary>
    public double WindowLeft { get; set; } = double.NaN;

    /// <summary>
    /// ビューアーウィンドウの位置（Y座標）
    /// </summary>
    public double WindowTop { get; set; } = double.NaN;

    /// <summary>
    /// ビューアーウィンドウの幅
    /// </summary>
    public double WindowWidth { get; set; } = 1100;

    /// <summary>
    /// ビューアーウィンドウの高さ
    /// </summary>
    public double WindowHeight { get; set; } = 750;

    /// <summary>
    /// ウィンドウが最大化されているかどうか
    /// </summary>
    public bool IsWindowMaximized { get; set; } = false;
}
