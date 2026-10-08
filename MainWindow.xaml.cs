using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WoodStreamTimeMachine.Resources;
using WoodStreamTimeMachine.Services;

namespace WoodStreamTimeMachine;

/// <summary>
/// 画面キャプチャ履歴を時系列で閲覧できるタイムライン・ビューアー
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly DeduplicationService _deduplicationService;
    private readonly List<CaptureItem> _items = new();
    private readonly DispatcherTimer _playbackTimer;
    private bool _isExplicitExit = false;

    private record CaptureItem(string FilePath, DateTime Timestamp);

    public MainWindow(SettingsManager settingsManager, DeduplicationService deduplicationService)
    {
        InitializeComponent();
        _settingsManager = settingsManager;
        _deduplicationService = deduplicationService;

        // タイムライン再生用タイマー（約10fps相当の滑らかなパラパラ漫画再生）
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;

        ApplyLocalization();
        RestoreWindowPlacement();
        LoadCaptures();
    }

    /// <summary>
    /// システム表示言語に応じたテキストを適用
    /// </summary>
    private void ApplyLocalization()
    {
        Title = Strings.ViewerTitle;
        HeaderTitleTextBlock.Text = Strings.AppTitle;
        RefreshButtonText.Text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "更新" : "Refresh";
        OptimizeButtonText.Text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "重複間引き" : "Clean Up";
        EmptyStateTextBlock.Text = Strings.NoCapturesFound;
        PlayPauseButton.ToolTip = Strings.PlayButtonTooltip;
        PrevFrameButton.ToolTip = Strings.PrevButtonTooltip;
        NextFrameButton.ToolTip = Strings.NextButtonTooltip;
        RefreshButton.ToolTip = Strings.RefreshButtonTooltip;
        OptimizeButton.ToolTip = Strings.RunDeduplicationButton;
    }

    /// <summary>
    /// 保存されているウィンドウの位置とサイズを復元
    /// </summary>
    private void RestoreWindowPlacement()
    {
        var settings = _settingsManager.Current;

        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            // 仮想画面全体の有効領域内にあるかチェック（マルチモニタ変更対策）
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = SystemParameters.VirtualScreenWidth;
            double virtualHeight = SystemParameters.VirtualScreenHeight;

            if (settings.WindowLeft >= virtualLeft && 
                settings.WindowLeft + 100 < virtualLeft + virtualWidth &&
                settings.WindowTop >= virtualTop && 
                settings.WindowTop + 100 < virtualTop + virtualHeight)
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
            }
        }

        if (settings.WindowWidth >= MinWidth) Width = settings.WindowWidth;
        if (settings.WindowHeight >= MinHeight) Height = settings.WindowHeight;

        if (settings.IsWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// ウィンドウの位置・サイズを設定ファイルに保存
    /// </summary>
    private void SaveWindowPlacement()
    {
        var settings = _settingsManager.Current;

        if (WindowState == WindowState.Maximized)
        {
            settings.IsWindowMaximized = true;
        }
        else if (WindowState == WindowState.Normal)
        {
            settings.IsWindowMaximized = false;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.WindowWidth = ActualWidth;
            settings.WindowHeight = ActualHeight;
        }

        _settingsManager.Save();
    }

    /// <summary>
    /// 保存先フォルダ内の画像を読み込み、タイムラインを初期化
    /// </summary>
    public void LoadCaptures(bool preserveIndex = false)
    {
        int prevIndex = (int)TimelineSlider.Value;
        string folder = _settingsManager.Current.StorageFolderPath;

        _items.Clear();

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            var files = Directory.GetFiles(folder, "*.jpg");
            foreach (var file in files)
            {
                var fi = new FileInfo(file);
                // ファイル名形式 yyyyMMdd_HHmmss から日時を取得（失敗時はファイルの更新日時）
                DateTime timestamp = fi.LastWriteTime;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                if (nameWithoutExt.Length >= 15 &&
                    DateTime.TryParseExact(nameWithoutExt[..15], "yyyyMMdd_HHmmss", 
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    timestamp = parsed;
                }

                _items.Add(new CaptureItem(file, timestamp));
            }

            // 時系列順（古い順）に並べ替え
            _items.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        }

        if (_items.Count == 0)
        {
            TimelineSlider.Minimum = 0;
            TimelineSlider.Maximum = 0;
            TimelineSlider.Value = 0;
            TimelineSlider.IsEnabled = false;

            CaptureImage.Source = null;
            EmptyStateBorder.Visibility = Visibility.Visible;
            OverlayBadge.Visibility = Visibility.Collapsed;
            OldestTimeTextBlock.Text = "--:--:--";
            LatestTimeTextBlock.Text = "--:--:--";
            return;
        }

        EmptyStateBorder.Visibility = Visibility.Collapsed;
        OverlayBadge.Visibility = Visibility.Visible;
        TimelineSlider.IsEnabled = true;
        TimelineSlider.Minimum = 0;
        TimelineSlider.Maximum = _items.Count - 1;

        OldestTimeTextBlock.Text = _items[0].Timestamp.ToString("yyyy/MM/dd HH:mm:ss");
        LatestTimeTextBlock.Text = _items[^1].Timestamp.ToString("yyyy/MM/dd HH:mm:ss");

        if (preserveIndex && prevIndex >= 0 && prevIndex < _items.Count)
        {
            TimelineSlider.Value = prevIndex;
        }
        else
        {
            // デフォルトは最新画像を表示
            TimelineSlider.Value = _items.Count - 1;
        }

        DisplayImageAtIndex((int)TimelineSlider.Value);
    }

    /// <summary>
    /// 新規キャプチャが保存されたときに外部から呼ばれる通知メソッド
    /// </summary>
    public void OnNewCaptureSaved(string filePath, DateTime timestamp)
    {
        Dispatcher.Invoke(() =>
        {
            bool wasAtLatest = _items.Count > 0 && (int)TimelineSlider.Value == _items.Count - 1;
            _items.Add(new CaptureItem(filePath, timestamp));
            TimelineSlider.Maximum = _items.Count - 1;
            LatestTimeTextBlock.Text = timestamp.ToString("yyyy/MM/dd HH:mm:ss");

            // 最新を見ていた場合は自動で最新に追従
            if (wasAtLatest)
            {
                TimelineSlider.Value = _items.Count - 1;
            }
        });
    }

    /// <summary>
    /// 指定されたインデックスの画像を読み込んで表示
    /// </summary>
    private void DisplayImageAtIndex(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        var item = _items[index];
        try
        {
            if (File.Exists(item.FilePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(item.FilePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // ファイルロックを即座に解放
                bitmap.EndInit();
                bitmap.Freeze(); // スレッド共有および高速レンダリング

                CaptureImage.Source = bitmap;

                // オーバーレイ情報の更新
                TimestampOverlayTextBlock.Text = item.Timestamp.ToString("yyyy/MM/dd HH:mm:ss");
                IndexOverlayTextBlock.Text = $"{index + 1} / {_items.Count}  ({Path.GetFileName(item.FilePath)})";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"画像表示エラー: {ex.Message}");
        }
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int index = (int)Math.Round(e.NewValue);
        DisplayImageAtIndex(index);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    private void TogglePlayback()
    {
        if (_playbackTimer.IsEnabled)
        {
            _playbackTimer.Stop();
            PlayPauseIconText.Text = "▶";
        }
        else
        {
            if (_items.Count <= 1) return;

            // もし末尾（最新）にいる場合は先頭から再生
            if ((int)TimelineSlider.Value >= _items.Count - 1)
            {
                TimelineSlider.Value = 0;
            }
            _playbackTimer.Start();
            PlayPauseIconText.Text = "⏸";
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (TimelineSlider.Value < TimelineSlider.Maximum)
        {
            TimelineSlider.Value++;
        }
        else
        {
            _playbackTimer.Stop();
            PlayPauseIconText.Text = "▶";
        }
    }

    private void PrevFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineSlider.Value > TimelineSlider.Minimum)
        {
            TimelineSlider.Value--;
        }
    }

    private void NextFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineSlider.Value < TimelineSlider.Maximum)
        {
            TimelineSlider.Value++;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadCaptures(preserveIndex: true);
    }

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        OptimizeButton.IsEnabled = false;
        try
        {
            ShowStatusToast("最適化を実行中...");
            int deleted = await _deduplicationService.RunDeduplicationAsync();
            ShowStatusToast(string.Format(Strings.OptimizationDoneMessage, deleted));
            LoadCaptures(preserveIndex: false);
        }
        finally
        {
            OptimizeButton.IsEnabled = true;
        }
    }

    private async void ShowStatusToast(string message)
    {
        StatusToastText.Text = message;
        StatusToast.Visibility = Visibility.Visible;
        await Task.Delay(3500);
        StatusToast.Visibility = Visibility.Collapsed;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePlayback();
                e.Handled = true;
                break;
            case Key.Left:
                PrevFrameButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Right:
                NextFrameButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F5:
                RefreshButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// アプリケーション終了（トレイアイコンの「終了」選択時など）を通知
    /// </summary>
    public void ForceClose()
    {
        _isExplicitExit = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveWindowPlacement();

        if (!_isExplicitExit)
        {
            // ✕ボタン押下時はアプリ終了ではなく非表示にし、常駐を継続
            e.Cancel = true;
            _playbackTimer.Stop();
            PlayPauseIconText.Text = "▶";
            Hide();
        }
    }
}