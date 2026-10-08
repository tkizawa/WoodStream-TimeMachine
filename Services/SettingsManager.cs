using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using WoodStreamTimeMachine.Models;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// アプリケーション設定の読み込みおよび保存を管理するサービスクラス
/// </summary>
public class SettingsManager
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WoodStreamTimeMachine"
    );

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 日本語文字列をUnicodeエスケープせず、そのままUTF-8テキストとして保存
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 現在読み込まれている設定インスタンス
    /// </summary>
    public AppSettings Current { get; private set; }

    public SettingsManager()
    {
        Current = Load();
    }

    /// <summary>
    /// 既定の画像保存先フォルダパスを取得します（マイピクチャ\WoodStreamTimeMachineImage）
    /// </summary>
    public static string GetDefaultStoragePath()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Path.Combine(pictures, "WoodStreamTimeMachineImage");
    }

    /// <summary>
    /// 設定ファイルから設定を読み込みます。ファイルが存在しない場合は新規インスタンスを生成します。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.StorageFolderPath))
                    {
                        loaded.StorageFolderPath = GetDefaultStoragePath();
                    }
                    Current = loaded;
                    return Current;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの読み込みに失敗しました: {ex.Message}");
        }

        // デフォルト設定を生成
        Current = new AppSettings
        {
            StorageFolderPath = GetDefaultStoragePath(),
            IsFirstRun = true
        };
        return Current;
    }

    /// <summary>
    /// 現在の設定を AppData\Local\WoodStreamTimeMachine\settings.json に保存します。
    /// </summary>
    public void Save()
    {
        try
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの保存に失敗しました: {ex.Message}");
        }
    }
}
