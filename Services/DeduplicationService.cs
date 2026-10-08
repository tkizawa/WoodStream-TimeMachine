using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// バックグラウンドで定期的に過去のキャプチャ画像を比較し、重複・微小差分の画像を間引く最適化サービス
/// </summary>
public class DeduplicationService : IDisposable
{
    private readonly SettingsManager _settingsManager;
    private CancellationTokenSource? _cts;
    private Task? _timerTask;

    /// <summary>
    /// 最適化タスク完了時に発生するイベント（削除されたファイル数を通知）
    /// </summary>
    public event Action<int>? OptimizationCompleted;

    public DeduplicationService(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    /// <summary>
    /// 定期実行タスクを開始
    /// </summary>
    public void Start()
    {
        if (_timerTask != null && !_timerTask.IsCompleted)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _timerTask = Task.Run(() => OptimizationLoopAsync(_cts.Token));
    }

    /// <summary>
    /// 定期実行タスクを停止
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_timerTask != null)
            {
                try
                {
                    await _timerTask;
                }
                catch (OperationCanceledException)
                {
                    // 正常終了
                }
            }
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 1時間に1回実行される最適化ループ
    /// </summary>
    private async Task OptimizationLoopAsync(CancellationToken cancellationToken)
    {
        int intervalMinutes = Math.Max(5, _settingsManager.Current.OptimizationIntervalMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                // 過去1時間分の画像を対象に間引きを実行
                await RunDeduplicationAsync(TimeSpan.FromHours(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"間引き処理エラー: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 指定期間のキャプチャ画像から連続する重複・微細差分画像を削除して間引きます
    /// </summary>
    /// <param name="targetPeriod">対象期間（nullの場合は過去全期間）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>削除された画像数</returns>
    public async Task<int> RunDeduplicationAsync(TimeSpan? targetPeriod = null, CancellationToken cancellationToken = default)
    {
        string folder = _settingsManager.Current.StorageFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return 0;
        }

        return await Task.Run(() =>
        {
            DateTime cutoff = targetPeriod.HasValue 
                ? DateTime.Now.Subtract(targetPeriod.Value) 
                : DateTime.MinValue;

            // フォルダ内のJPG画像を取得し、ファイル名（時系列タイムスタンプ）順にソート
            var files = Directory.GetFiles(folder, "*.jpg")
                .Select(f => new FileInfo(f))
                .Where(fi => fi.CreationTime >= cutoff || fi.LastWriteTime >= cutoff)
                .OrderBy(fi => fi.Name)
                .ToList();

            if (files.Count < 2)
            {
                return 0;
            }

            int deletedCount = 0;
            byte[]? baseFingerprint = null;

            for (int i = 0; i < files.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var file = files[i];
                byte[]? currentFingerprint = ComputeImageFingerprint(file.FullName);

                if (currentFingerprint == null)
                {
                    continue;
                }

                if (baseFingerprint == null)
                {
                    // 比較の起点となる最初の画像
                    baseFingerprint = currentFingerprint;
                    continue;
                }

                // 2つの画像の平均ピクセル差分を計算
                double difference = CalculateDifferenceRatio(baseFingerprint, currentFingerprint);

                // 差分が極めて少ない（画面に有意な変化がない）場合は削除
                // 0.02 (2%未満の差分: マウスポインタや時計のわずかな更新のみ) を重複とみなす
                if (difference < 0.025)
                {
                    try
                    {
                        File.Delete(file.FullName);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ファイル削除スキップ ({file.Name}): {ex.Message}");
                    }
                }
                else
                {
                    // 変化があった場合は新たな基準画像とする
                    baseFingerprint = currentFingerprint;
                }
            }

            OptimizationCompleted?.Invoke(deletedCount);
            return deletedCount;
        }, cancellationToken);
    }

    /// <summary>
    /// 画像を16x16のグレースケールに縮小し、比較用フィンガープリント（256バイト配列）を生成します
    /// </summary>
    private static byte[]? ComputeImageFingerprint(string filePath)
    {
        try
        {
            const int size = 16;
            using var original = (Bitmap)Image.FromFile(filePath);
            using var thumb = new Bitmap(size, size, PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(original, 0, 0, size, size);
            }

            byte[] fingerprint = new byte[size * size];
            var data = thumb.LockBits(
                new Rectangle(0, 0, size, size), 
                ImageLockMode.ReadOnly, 
                PixelFormat.Format24bppRgb
            );

            try
            {
                unsafe
                {
                    byte* scan0 = (byte*)data.Scan0.ToPointer();
                    int stride = data.Stride;

                    for (int y = 0; y < size; y++)
                    {
                        byte* row = scan0 + (y * stride);
                        for (int x = 0; x < size; x++)
                        {
                            // RGBをグレースケール輝度に変換 (0.299R + 0.587G + 0.114B)
                            byte b = row[x * 3];
                            byte gVal = row[x * 3 + 1];
                            byte r = row[x * 3 + 2];
                            fingerprint[y * size + x] = (byte)(0.299 * r + 0.587 * gVal + 0.114 * b);
                        }
                    }
                }
            }
            finally
            {
                thumb.UnlockBits(data);
            }

            return fingerprint;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 2つのフィンガープリント間の平均絶対差率（0.0〜1.0）を算出します
    /// </summary>
    private static double CalculateDifferenceRatio(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return 1.0;

        long totalDiff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            totalDiff += Math.Abs(a[i] - b[i]);
        }

        // 最大可能差分は 255 * 配列長
        return (double)totalDiff / (255.0 * a.Length);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
