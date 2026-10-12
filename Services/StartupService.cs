using System.Diagnostics;
using Microsoft.Win32;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// Windows起動時の自動起動（スタートアップ登録）を管理するサービスクラス
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WoodStreamTimeMachine";

    /// <summary>
    /// 現在のユーザー環境において、Windows起動時の自動起動が有効になっているかを確認します。
    /// </summary>
    /// <returns>自動起動が有効な場合は true、それ以外は false</returns>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"スタートアップ状態確認エラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Windows起動時の自動起動（スタートアップ）の有効・無効を切り替えます。
    /// </summary>
    /// <param name="enable">有効にする場合は true、無効にする場合は false</param>
    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                // 現在の実行可能ファイルパスを取得して登録（バックグラウンド常駐モードとして起動）
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --background");
                }
            }
            else
            {
                // レジストリから登録キーを削除
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"スタートアップ登録更新エラー: {ex.Message}");
        }
    }
}
