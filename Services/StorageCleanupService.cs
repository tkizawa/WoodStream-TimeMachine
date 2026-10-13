using System.Diagnostics;
using System.IO;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// キャプチャ保存先フォルダの容量を監視し、設定された上限容量（デフォルト150MB）を超過した場合に
/// 古いスクリーンショットから自動削除するクリーンアップサービス（1時間ごとに判定）
/// </summary>
public class StorageCleanupService : IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly OcrDatabaseService? _ocrDatabaseService;
    private CancellationTokenSource? _cts;
    private Task? _cleanupTask;

    /// <summary>
    /// 容量制限によるファイル削除処理が完了したときに発生するイベント（削除されたファイル数を通知）
    /// </summary>
    public event Action<int>? CleanupCompleted;

    public StorageCleanupService(SettingsManager settingsManager, OcrDatabaseService? ocrDatabaseService = null)
    {
        _settingsManager = settingsManager;
        _ocrDatabaseService = ocrDatabaseService;
    }

    /// <summary>
    /// 定期判定タスクを開始します（1時間間隔）
    /// </summary>
    public void Start()
    {
        if (_cleanupTask != null && !_cleanupTask.IsCompleted)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _cleanupTask = Task.Run(() => CleanupLoopAsync(_cts.Token));
    }

    /// <summary>
    /// 定期判定タスクを停止します
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_cleanupTask != null)
            {
                try
                {
                    await _cleanupTask;
                }
                catch (OperationCanceledException)
                {
                    // 正常キャンセル
                }
            }
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 1時間ごとにフォルダ容量を判定・クリーンアップするバックグラウンドループ
    /// </summary>
    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        // 起動時にも初回チェックを一度実行
        try
        {
            await RunCleanupAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"初回容量チェックエラー: {ex.Message}");
        }

        // 1時間ごとのタイマー
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                await RunCleanupAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"定期容量クリーンアップエラー: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 保存先フォルダのファイル総容量を計測し、上限（MaxStorageSizeMB）を超えている場合は古い画像から順に削除します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>削除されたファイル数</returns>
    public async Task<int> RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        string folder = _settingsManager.Current.StorageFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return 0;
        }

        int maxStorageMB = _settingsManager.Current.MaxStorageSizeMB;
        if (maxStorageMB <= 0)
        {
            return 0;
        }

        long maxBytes = (long)maxStorageMB * 1024 * 1024;

        return await Task.Run(() =>
        {
            try
            {
                var dirInfo = new DirectoryInfo(folder);
                // 画像ファイル（.jpg, .jpeg, .png）を取得
                var files = dirInfo.GetFiles("*.*")
                    .Where(f => f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name) // yyyyMMdd_HHmmss 形式のファイル名順（時系列の昇順＝古い順）
                    .ToList();

                long totalBytes = files.Sum(f => f.Length);
                if (totalBytes <= maxBytes)
                {
                    // 上限以内であれば削除の必要なし
                    return 0;
                }

                int deletedCount = 0;
                long bytesFreed = 0;

                // 上限以下に収まるまで、古い画像ファイルから順に削除
                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (totalBytes <= maxBytes) break;

                    try
                    {
                        long fileLength = file.Length;
                        string fullName = file.FullName;
                        file.Delete();
                        totalBytes -= fileLength;
                        bytesFreed += fileLength;
                        deletedCount++;

                        _ocrDatabaseService?.DeleteRecordAsync(fullName);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ファイル削除失敗: {file.FullName}, エラー: {ex.Message}");
                    }
                }

                if (deletedCount > 0)
                {
                    Debug.WriteLine($"容量制限（{maxStorageMB}MB）超過を検知: 古いキャプチャ {deletedCount} 枚を削除し、{bytesFreed / 1024 / 1024}MB を解放しました。");
                    CleanupCompleted?.Invoke(deletedCount);
                }

                return deletedCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"クリーンアップ処理中の例外: {ex.Message}");
                return 0;
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
