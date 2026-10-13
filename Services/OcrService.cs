using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WoodStreamTimeMachine.Models;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// バックグラウンドで画面キャプチャ画像からWindows標準OCRエンジンを用いて
/// 文字列を抽出し、データベースへ保存するサービス
/// </summary>
public class OcrService : IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly OcrDatabaseService _databaseService;
    private readonly Channel<OcrQueueItem> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _workerTask;
    private OcrEngine? _ocrEngine;

    private record OcrQueueItem(string ImagePath, DateTime Timestamp);

    /// <summary>
    /// OCRで新しいテキストが抽出・保存されたときに発生するイベント
    /// </summary>
    public event Action<string, string>? TextExtracted;

    public OcrService(SettingsManager settingsManager, OcrDatabaseService databaseService)
    {
        _settingsManager = settingsManager;
        _databaseService = databaseService;
        _channel = Channel.CreateUnbounded<OcrQueueItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();

        InitializeOcrEngine();

        // バックグラウンドでキューを順次処理
        _workerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
    }

    /// <summary>
    /// Windows標準OCRエンジンを初期化します（日本語またはユーザー言語優先）
    /// </summary>
    private void InitializeOcrEngine()
    {
        try
        {
            // 利用可能な言語から日本語を優先検索
            var languages = OcrEngine.AvailableRecognizerLanguages;
            var jaLang = languages.FirstOrDefault(l => l.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase));

            if (jaLang != null)
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(jaLang);
            }

            // 日本語が利用できない場合はユーザーのプロファイル言語または利用可能な第1言語でフォールバック
            _ocrEngine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            if (_ocrEngine == null && languages.Count > 0)
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(languages[0]);
            }

            if (_ocrEngine != null)
            {
                Debug.WriteLine($"OcrEngine 初期化成功: 言語 = {_ocrEngine.RecognizerLanguage.LanguageTag}");
            }
            else
            {
                Debug.WriteLine("警告: 利用可能なOCR言語エンジンが見つかりませんでした。");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OcrEngine 初期化失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// キャプチャ画像をOCR処理キューに追加します
    /// </summary>
    public void Enqueue(string imagePath, DateTime timestamp)
    {
        if (!_settingsManager.Current.EnableOcr)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        _channel.Writer.TryWrite(new OcrQueueItem(imagePath, timestamp));
    }

    /// <summary>
    /// 指定フォルダ内の未処理画像を低優先度でキューに追加します（過去キャプチャのインデックス補完）
    /// </summary>
    public void EnqueueExistingImages(string folderPath)
    {
        if (!_settingsManager.Current.EnableOcr || string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var files = Directory.GetFiles(folderPath, "*.jpg")
                    .OrderByDescending(f => f) // 新しいものから優先してキューイング
                    .Take(200); // 起動直後の過度な負荷を避けるため直近200枚を対象

                foreach (var file in files)
                {
                    if (_cts.Token.IsCancellationRequested) break;
                    if (!await _databaseService.IsProcessedAsync(file))
                    {
                        var fi = new FileInfo(file);
                        _channel.Writer.TryWrite(new OcrQueueItem(file, fi.LastWriteTime));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"既存画像のキューイング処理エラー: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// キューから画像を取り出して順次OCR処理を実行するループ
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;

        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var item))
            {
                if (cancellationToken.IsCancellationRequested) return;

                if (!_settingsManager.Current.EnableOcr)
                {
                    continue;
                }

                if (!File.Exists(item.ImagePath))
                {
                    continue;
                }

                try
                {
                    string text = await RecognizeTextFromFileAsync(item.ImagePath);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        await _databaseService.UpsertRecordAsync(item.ImagePath, item.Timestamp, text);
                        TextExtracted?.Invoke(item.ImagePath, text);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"画像OCR処理エラー ({item.ImagePath}): {ex.Message}");
                }

                // CPU負荷軽減のため各画像処理間に微小ウェイトを設ける
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 画像ファイルからWindows OCRを用いて文字列を抽出します
    /// </summary>
    private async Task<string> RecognizeTextFromFileAsync(string imagePath)
    {
        if (_ocrEngine == null || !File.Exists(imagePath))
        {
            return string.Empty;
        }

        try
        {
            using var fileStream = File.OpenRead(imagePath);
            using var randomAccessStream = fileStream.AsRandomAccessStream();

            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            // OcrEngineが受け入れ可能なピクセルフォーマットに変換
            SoftwareBitmap convertedBitmap = softwareBitmap;
            bool needDisposeConverted = false;

            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                convertedBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                needDisposeConverted = true;
            }

            try
            {
                var ocrResult = await _ocrEngine.RecognizeAsync(convertedBitmap);
                return NormalizeOcrResult(ocrResult);
            }
            finally
            {
                if (needDisposeConverted)
                {
                    convertedBitmap.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OCR認識例外 ({imagePath}): {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// OCR結果を行単位で正規化し、日本語文字同士の間に挿入された余分な半角スペースを除去します
    /// </summary>
    private static string NormalizeOcrResult(OcrResult? ocrResult)
    {
        if (ocrResult == null || ocrResult.Lines.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();

        foreach (var line in ocrResult.Lines)
        {
            var words = line.Words;
            if (words.Count == 0) continue;

            for (int i = 0; i < words.Count; i++)
            {
                string current = words[i].Text;
                if (string.IsNullOrEmpty(current)) continue;

                sb.Append(current);

                if (i < words.Count - 1)
                {
                    string next = words[i + 1].Text;
                    if (string.IsNullOrEmpty(next)) continue;

                    char lastChar = current[^1];
                    char firstChar = next[0];

                    // 日本語文字同士の場合は半角スペースを挿入しない（英単語同士などはスペースを保持）
                    if (!IsJapaneseChar(lastChar) || !IsJapaneseChar(firstChar))
                    {
                        sb.Append(' ');
                    }
                }
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 指定文字が日本語（ひらがな・カタカナ・漢字・全角記号）かどうかを判定します
    /// </summary>
    private static bool IsJapaneseChar(char c)
    {
        return (c >= 0x3040 && c <= 0x309F) || // ひらがな
               (c >= 0x30A0 && c <= 0x30FF) || // カタカナ
               (c >= 0x4E00 && c <= 0x9FFF) || // CJK統合漢字
               (c >= 0x3400 && c <= 0x4DBF) || // CJK統合漢字拡張A
               (c >= 0xFF00 && c <= 0xFFEF);   // 全角英数・記号
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
