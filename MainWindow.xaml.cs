using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Forms;
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
    private readonly CaptureService? _captureService;
    private readonly OcrDatabaseService? _ocrDatabaseService;
    private readonly List<CaptureItem> _items = new();
    private readonly List<SearchResultDisplayItem> _searchResults = new();
    private readonly List<MonitorPreviewItem> _monitorPreviews = new();
    private readonly DispatcherTimer _playbackTimer;
    private int _currentSearchResultIndex = -1;
    private int _selectedMonitorIndex = -1;
    private bool _isExplicitExit = false;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    private double _imageZoom = 1.0;
    private bool _fitToWindowEnabled = true;

    private record CaptureItem(string FilePath, DateTime Timestamp);

    public MainWindow(
        SettingsManager settingsManager,
        DeduplicationService deduplicationService,
        CaptureService? captureService = null,
        OcrDatabaseService? ocrDatabaseService = null)
    {
        InitializeComponent();
        _settingsManager = settingsManager;
        _deduplicationService = deduplicationService;
        _captureService = captureService;
        _ocrDatabaseService = ocrDatabaseService;

        // ビュアーの表示・非表示・最小化状態の変化を監視し、表示中はキャプチャを一時停止
        Loaded += (s, e) => UpdateViewerVisibility();
        IsVisibleChanged += (s, e) => UpdateViewerVisibility();
        StateChanged += (s, e) => UpdateViewerVisibility();
        Closed += (s, e) =>
        {
            if (_captureService != null)
            {
                _captureService.IsViewerVisible = false;
            }
        };

        // タイムライン再生用タイマー（約10fps相当の滑らかなパラパラ漫画再生）
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;

        ApplyLocalizationLabels();
        RestoreWindowPlacement();
        LoadCaptures();
    }

    /// <summary>
    /// ビュアーの表示状態（表示中かつ最小化されていない状態）を判定し、
    /// キャプチャサービスへ一時停止フラグを反映します。
    /// </summary>
    private void UpdateViewerVisibility()
    {
        if (_captureService == null) return;

        // ウィンドウが表示されており、かつ最小化されていない場合は「表示中」とみなす
        bool isVisibleOnScreen = IsVisible && WindowState != WindowState.Minimized;
        _captureService.IsViewerVisible = isVisibleOnScreen;
    }

    /// <summary>
    /// システム表示言語に応じたテキストを適用
    /// </summary>
    private void ApplyLocalizationLabels()
    {
        Title = Strings.ViewerTitle;
        HeaderTitleTextBlock.Text = Strings.AppTitle;
        RefreshButtonText.Text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "更新" : "Refresh";
        OptimizeButtonText.Text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "重複間引き" : "Clean Up";
        EmptyStateTextBlock.Text = Strings.NoCapturesFound;
        CapturePausedText.Text = Strings.CapturePausedBadge;
        PlayPauseButton.ToolTip = Strings.PlayButtonTooltip;
        PrevFrameButton.ToolTip = Strings.PrevButtonTooltip;
        NextFrameButton.ToolTip = Strings.NextButtonTooltip;
        RefreshButton.ToolTip = Strings.RefreshButtonTooltip;
        OptimizeButton.ToolTip = Strings.RunDeduplicationButton;

        // 検索UIの多言語テキスト
        SearchPlaceholderTextBlock.Text = Strings.SearchPlaceholder;
        SearchButtonText.Text = Strings.SearchButtonText;
        SearchButton.ToolTip = Strings.SearchButtonText;
        ClearSearchButton.ToolTip = Strings.ClearSearchTooltip;
        SearchPanelTitleTextBlock.Text = Strings.SearchPanelTitle;
        CloseSearchPanelButton.ToolTip = Strings.CloseSearchPanelTooltip;
        NoResultsTextBlock.Text = Strings.NoSearchResults;
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

        // 検索結果が表示されている場合はフレームインデックスとマーカーを再計算
        if (_searchResults.Count > 0)
        {
            foreach (var r in _searchResults)
            {
                r.FrameIndex = _items.FindIndex(item => item.FilePath.Equals(r.ImagePath, StringComparison.OrdinalIgnoreCase));
            }
            UpdateSearchTicks();
        }
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
                UpdateSelectedMonitorView(item.FilePath, index);

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

    private void UpdateSelectedMonitorView(string filePath, int frameIndex)
    {
        _selectedMonitorIndex = -1;
        _monitorPreviews.Clear();
        MonitorThumbnailsPanel.Visibility = Visibility.Collapsed;

        LoadBitmapIntoViewer(filePath);
    }

    private void SyncCaptureImageToSelectedMonitor(string filePath)
    {
        LoadBitmapIntoViewer(filePath);
    }

    private void LoadBitmapIntoViewer(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        CaptureImage.Source = bitmap;
        ApplyCurrentZoom();
    }

    private void ApplyCurrentZoom()
    {
        if (CaptureImage.Source is not BitmapSource source)
        {
            return;
        }

        double zoom = _fitToWindowEnabled ? ComputeFitZoom(source.PixelWidth, source.PixelHeight) : _imageZoom;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);

        _imageZoom = zoom;
        CaptureImage.LayoutTransform = new System.Windows.Media.ScaleTransform(zoom, zoom);
        ZoomSlider.ValueChanged -= ZoomSlider_ValueChanged;
        ZoomSlider.Value = zoom * 100.0;
        ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
    }

    private double ComputeFitZoom(int pixelWidth, int pixelHeight)
    {
        if (ImageScrollViewer.ActualWidth <= 0 || ImageScrollViewer.ActualHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
        {
            return _imageZoom;
        }

        double widthScale = ImageScrollViewer.ActualWidth / pixelWidth;
        double heightScale = ImageScrollViewer.ActualHeight / pixelHeight;
        double fitScale = Math.Min(widthScale, heightScale) * 0.95;
        return Math.Clamp(fitScale, MinZoom, MaxZoom);
    }

    private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitToWindowEnabled)
        {
            ApplyCurrentZoom();
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CaptureImage.Source is null) return;

        if (Math.Abs(e.NewValue - e.OldValue) < 0.01)
        {
            return;
        }

        _fitToWindowEnabled = false;
        _imageZoom = Math.Clamp(e.NewValue / 100.0, MinZoom, MaxZoom);
        CaptureImage.LayoutTransform = new System.Windows.Media.ScaleTransform(_imageZoom, _imageZoom);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        _fitToWindowEnabled = false;
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 10);
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        _fitToWindowEnabled = false;
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 10);
    }

    private void FitToWindowButton_Click(object sender, RoutedEventArgs e)
    {
        _fitToWindowEnabled = true;
        ApplyCurrentZoom();
    }

    private static List<Rectangle> LoadMonitorLayout(string filePath)
    {
        string metadataPath = filePath + ".meta.json";
        if (File.Exists(metadataPath))
        {
            try
            {
                string json = File.ReadAllText(metadataPath);
                using var document = System.Text.Json.JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("Monitors", out var monitorsElement) && monitorsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var rectangles = new List<Rectangle>();
                    foreach (var item in monitorsElement.EnumerateArray())
                    {
                        int x = item.TryGetProperty("X", out var xProp) ? xProp.GetInt32() : 0;
                        int y = item.TryGetProperty("Y", out var yProp) ? yProp.GetInt32() : 0;
                        int width = item.TryGetProperty("Width", out var wProp) ? wProp.GetInt32() : 0;
                        int height = item.TryGetProperty("Height", out var hProp) ? hProp.GetInt32() : 0;
                        rectangles.Add(new Rectangle(x, y, width, height));
                    }

                    if (rectangles.Count > 0)
                    {
                        return rectangles;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"モニタ配置メタデータ読み込みエラー: {ex.Message}");
            }
        }

        var screens = Screen.AllScreens
            .OrderBy(screen => screen.Bounds.X)
            .ThenBy(screen => screen.Bounds.Y)
            .ToList();

        if (screens.Count <= 1)
        {
            return new List<Rectangle>();
        }

        var virtualBounds = new Rectangle(
            screens.Min(s => s.Bounds.Left),
            screens.Min(s => s.Bounds.Top),
            screens.Max(s => s.Bounds.Right) - screens.Min(s => s.Bounds.Left),
            screens.Max(s => s.Bounds.Bottom) - screens.Min(s => s.Bounds.Top));

        var layout = new List<Rectangle>();
        foreach (var screen in screens)
        {
            int x = Math.Max(0, screen.Bounds.X - virtualBounds.X);
            int y = Math.Max(0, screen.Bounds.Y - virtualBounds.Y);
            layout.Add(new Rectangle(x, y, screen.Bounds.Width, screen.Bounds.Height));
        }

        return layout;
    }

    private static Bitmap CropBitmap(Bitmap sourceBitmap, Rectangle cropRegion)
    {
        if (cropRegion.Width <= 0 || cropRegion.Height <= 0 || cropRegion.X < 0 || cropRegion.Y < 0 ||
            cropRegion.Right > sourceBitmap.Width || cropRegion.Bottom > sourceBitmap.Height)
        {
            return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        }

        var cropped = new Bitmap(cropRegion.Width, cropRegion.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(sourceBitmap, new Rectangle(0, 0, cropRegion.Width, cropRegion.Height), cropRegion, GraphicsUnit.Pixel);
        return cropped;
    }

    private static BitmapImage CreateBitmapSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void MonitorThumbList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MonitorThumbList.SelectedItem is not MonitorPreviewItem selected)
        {
            return;
        }

        _selectedMonitorIndex = selected.Index;

        int currentIndex = (int)TimelineSlider.Value;
        if (currentIndex >= 0 && currentIndex < _items.Count)
        {
            var currentItem = _items[currentIndex];
            if (File.Exists(currentItem.FilePath))
            {
                SyncCaptureImageToSelectedMonitor(currentItem.FilePath);
            }
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
            case Key.F:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    SearchTextBox.Focus();
                    SearchTextBox.SelectAll();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                if (SearchResultsPanel.Visibility == Visibility.Visible)
                {
                    CloseSearchPanel();
                    e.Handled = true;
                }
                break;
            case Key.F3:
                if (_searchResults.Count > 0)
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        _currentSearchResultIndex = (_currentSearchResultIndex - 1 + _searchResults.Count) % _searchResults.Count;
                    }
                    else
                    {
                        _currentSearchResultIndex = (_currentSearchResultIndex + 1) % _searchResults.Count;
                    }
                    SearchResultsListBox.SelectedIndex = _currentSearchResultIndex;
                    e.Handled = true;
                }
                break;
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

    #region OCR検索関連ハンドラ・メソッド

    private void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSearchAsync();
            e.Handled = true;
        }
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        bool hasText = !string.IsNullOrEmpty(SearchTextBox.Text);
        SearchPlaceholderTextBlock.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteSearchAsync();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Text = string.Empty;
        CloseSearchPanel();
    }

    private void CloseSearchPanelButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSearchPanel();
    }

    private void CloseSearchPanel()
    {
        SearchResultsPanel.Visibility = Visibility.Collapsed;
        SearchTicksCanvas.Children.Clear();
        _searchResults.Clear();
        _currentSearchResultIndex = -1;
    }

    /// <summary>
    /// 入力されたキーワードでOCRテキスト検索を実行し、結果を表示
    /// </summary>
    private async void ExecuteSearchAsync()
    {
        string query = SearchTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            CloseSearchPanel();
            return;
        }

        if (_ocrDatabaseService == null)
        {
            return;
        }

        SearchButton.IsEnabled = false;
        try
        {
            var rawResults = await _ocrDatabaseService.SearchAsync(query);

            _searchResults.Clear();
            foreach (var r in rawResults)
            {
                int frameIdx = _items.FindIndex(item => item.FilePath.Equals(r.ImagePath, StringComparison.OrdinalIgnoreCase));
                if (frameIdx >= 0)
                {
                    _searchResults.Add(new SearchResultDisplayItem
                    {
                        ImagePath = r.ImagePath,
                        Timestamp = r.Timestamp,
                        Snippet = r.Snippet,
                        FrameIndex = frameIdx
                    });
                }
            }

            SearchResultsListBox.ItemsSource = null;
            SearchResultsListBox.ItemsSource = _searchResults;

            SearchResultCountTextBlock.Text = string.Format(Strings.SearchResultsCount, _searchResults.Count);
            NoResultsTextBlock.Visibility = _searchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchResultsPanel.Visibility = Visibility.Visible;

            UpdateSearchTicks();

            if (_searchResults.Count > 0)
            {
                _currentSearchResultIndex = 0;
                SearchResultsListBox.SelectedIndex = 0;
                JumpToFrame(_searchResults[0].FrameIndex);
            }
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void SearchResultsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SearchResultsListBox.SelectedItem is SearchResultDisplayItem selectedItem)
        {
            _currentSearchResultIndex = SearchResultsListBox.SelectedIndex;
            JumpToFrame(selectedItem.FrameIndex);
        }
    }

    private void JumpToFrame(int frameIndex)
    {
        if (frameIndex >= 0 && frameIndex < _items.Count)
        {
            _playbackTimer.Stop();
            PlayPauseIconText.Text = "▶";
            TimelineSlider.Value = frameIndex;
        }
    }

    /// <summary>
    /// 検索ヒットしたコマ位置をタイムラインスライダートラック上にマーカーとして描画
    /// </summary>
    private void UpdateSearchTicks()
    {
        SearchTicksCanvas.Children.Clear();
        if (_items.Count <= 1 || _searchResults.Count == 0) return;

        double canvasWidth = SearchTicksCanvas.ActualWidth;
        if (canvasWidth <= 0) return;

        double max = _items.Count - 1;
        var solidBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38BDF8"));
        solidBrush.Freeze();

        foreach (var result in _searchResults)
        {
            if (result.FrameIndex < 0) continue;

            double ratio = result.FrameIndex / max;
            double x = ratio * canvasWidth;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = 3,
                Height = 8,
                RadiusX = 1,
                RadiusY = 1,
                Fill = solidBrush,
                Opacity = 0.95
            };

            System.Windows.Controls.Canvas.SetLeft(rect, Math.Max(0, x - 1.5));
            System.Windows.Controls.Canvas.SetTop(rect, 0);
            SearchTicksCanvas.Children.Add(rect);
        }
    }

    private void SliderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSearchTicks();
    }

    #endregion

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
            UpdateViewerVisibility();
        }
    }
}

/// <summary>
/// 検索結果一覧パネルにバインドする表示用モデル
/// </summary>
public class SearchResultDisplayItem
{
    public string ImagePath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FormattedTimestamp => Timestamp.ToString("yyyy/MM/dd HH:mm:ss");
    public string Snippet { get; set; } = string.Empty;
    public int FrameIndex { get; set; } = -1;
}

public class MonitorPreviewItem
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public BitmapSource Preview { get; set; } = null!;
}