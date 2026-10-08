using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using WoodStreamTimeMachine.Services;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// 画面の自動キャプチャとファイル保存を行うバックグラウンドサービス
/// </summary>
public class CaptureService : IDisposable
{
    private readonly SettingsManager _settingsManager;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private readonly ImageCodecInfo? _jpegEncoder;
    private readonly EncoderParameters _encoderParams;

    /// <summary>
    /// キャプチャが保存されたときに発生するイベント
    /// </summary>
    public event Action<string, DateTime>? CaptureSaved;

    public CaptureService(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        // 高圧縮JPEGエンコーダの設定（品質80%で高画質と容量削減を両立）
        _jpegEncoder = GetEncoder(ImageFormat.Jpeg);
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 80L);
    }

    /// <summary>
    /// キャプチャループを開始します
    /// </summary>
    public void Start()
    {
        if (_captureTask != null && !_captureTask.IsCompleted)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
    }

    /// <summary>
    /// キャプチャループを停止します
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_captureTask != null)
            {
                try
                {
                    await _captureTask;
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
    /// 指定された間隔（デフォルト5秒）でスクリーンショットを撮り続けるループ
    /// </summary>
    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        int intervalSeconds = Math.Max(1, _settingsManager.Current.CaptureIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                await CaptureAndSaveAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"キャプチャ処理エラー: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// メイン画面のスクリーンショットを取得し、設定フォルダへ保存します
    /// </summary>
    public async Task CaptureAndSaveAsync()
    {
        string folder = _settingsManager.Current.StorageFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch
            {
                return;
            }
        }

        DateTime now = DateTime.Now;
        string timestampStr = now.ToString("yyyyMMdd_HHmmss");
        string filePath = Path.Combine(folder, $"{timestampStr}.jpg");

        // 1秒間に複数回発生した場合のファイル名重複回避
        if (File.Exists(filePath))
        {
            filePath = Path.Combine(folder, $"{now:yyyyMMdd_HHmmss_fff}.jpg");
        }

        // バックグラウンドスレッドで画面キャプチャと画像保存を実行
        await Task.Run(() =>
        {
            // メインディスプレイの解像度範囲を取得
            Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? Screen.AllScreens[0].Bounds;

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            // JPEG形式で保存
            if (_jpegEncoder != null)
            {
                bitmap.Save(filePath, _jpegEncoder, _encoderParams);
            }
            else
            {
                bitmap.Save(filePath, ImageFormat.Jpeg);
            }
        });

        CaptureSaved?.Invoke(filePath, now);
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _encoderParams.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
