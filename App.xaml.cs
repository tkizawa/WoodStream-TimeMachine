using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WoodStreamTimeMachine.Services;
using WoodStreamTimeMachine.Views;

namespace WoodStreamTimeMachine;

/// <summary>
/// アプリケーションのエントリポイントおよび常駐・ライフサイクル管理
/// </summary>
public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "WoodStreamTimeMachine_SingleInstance_Mutex";
    private const string ActivationEventName = "WoodStreamTimeMachine_Activation_Event";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCts;

    private SettingsManager? _settingsManager;
    private CaptureService? _captureService;
    private DeduplicationService? _deduplicationService;
    private StorageCleanupService? _storageCleanupService;
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. 重複起動（多重起動）防止チェック
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // 既に起動中のインスタンスが存在する場合、既存プロセスのビューアーを開くようシグナルを送信
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivationEventName, out var existingEvent))
                {
                    existingEvent.Set();
                }
            }
            catch
            {
                // シグナル送信失敗時は無視
            }

            // 新規プロセスは直ちに終了
            Shutdown();
            return;
        }

        // 重複起動時のシグナル待機用イベントを作成
        try
        {
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
            _activationCts = new CancellationTokenSource();
            StartActivationListener(_activationCts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"アクティベーションイベント作成失敗: {ex.Message}");
        }

        base.OnStartup(e);

        // ARM64環境（Snapdragon X / Adreno GPU等）におけるDirectXハードウェア描画の
        // テクスチャ色チャンネル（赤・青）スワップ不具合を回避するため、ソフトウェアレンダリングを使用
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // 2. 設定マネージャの初期化
        _settingsManager = new SettingsManager();

        // 2. 初回起動時のセットアップ判定
        if (_settingsManager.Current.IsFirstRun || 
            string.IsNullOrWhiteSpace(_settingsManager.Current.StorageFolderPath))
        {
            var setupWin = new SetupWindow(_settingsManager, isInitialSetup: true);
            bool? result = setupWin.ShowDialog();

            // 初回セットアップでキャンセルされた場合はアプリを終了
            if (result != true || string.IsNullOrWhiteSpace(_settingsManager.Current.StorageFolderPath))
            {
                Shutdown();
                return;
            }
        }

        // スタートアップ自動起動の設定同期
        if (_settingsManager.Current.StartWithWindows)
        {
            StartupService.SetStartup(true);
        }

        // 3. サービスの初期化
        _deduplicationService = new DeduplicationService(_settingsManager);
        _storageCleanupService = new StorageCleanupService(_settingsManager);
        _captureService = new CaptureService(_settingsManager);
        _mainWindow = new MainWindow(_settingsManager, _deduplicationService, _captureService);

        // 新規キャプチャ保存イベントをビューアーに接続
        _captureService.CaptureSaved += (path, time) =>
        {
            _mainWindow?.OnNewCaptureSaved(path, time);
        };

        // 容量制限クリーンアップ完了時のビューアー同期
        _storageCleanupService.CleanupCompleted += (count) =>
        {
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.LoadCaptures(preserveIndex: true));
        };

        // 4. タスクトレイ常駐アイコンの初期化
        _trayIconService = new TrayIconService(
            openViewerAction: ShowViewer,
            openSettingsAction: ShowSettings,
            exitAction: ExitApplication
        );

        // 5. バックグラウンドキャプチャ、重複最適化、容量監視を開始
        _captureService.Start();
        _deduplicationService.Start();
        _storageCleanupService.Start();

        // 6. 通常起動（Windows自動起動の --background 引数なし）の場合はビューアーを表示
        bool isBackground = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase) || 
                                           a.Equals("-background", StringComparison.OrdinalIgnoreCase));
        if (!isBackground)
        {
            ShowViewer();
        }
    }

    /// <summary>
    /// 重複起動時に既存プロセスのビューアーを前面化するためのシグナル受信ループ
    /// </summary>
    private void StartActivationListener(CancellationToken cancellationToken)
    {
        Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_activationEvent?.WaitOne() == true)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        // UIスレッドでビューアーを表示・前面化
                        Dispatcher.BeginInvoke(() =>
                        {
                            ShowViewer();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"アクティベーション待機エラー: {ex.Message}");
                    break;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// メインビューアーウィンドウを表示
    /// </summary>
    private void ShowViewer()
    {
        if (_mainWindow == null) return;

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        // 他ウィンドウに隠れている場合でも確実に最前面化
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
        _mainWindow.LoadCaptures(preserveIndex: true);
    }

    /// <summary>
    /// 設定画面を表示
    /// </summary>
    private void ShowSettings()
    {
        if (_settingsManager == null) return;

        var setupWin = new SetupWindow(_settingsManager, isInitialSetup: false);
        if (_mainWindow != null && _mainWindow.IsVisible)
        {
            setupWin.Owner = _mainWindow;
        }

        if (setupWin.ShowDialog() == true)
        {
            // キャプチャ間隔を即時更新
            _captureService?.UpdateInterval();

            // 容量制限クリーンアップを即時実行
            _ = _storageCleanupService?.RunCleanupAsync();

            // 保存先が変更された場合はビューアーを再読込
            _mainWindow?.LoadCaptures(preserveIndex: false);
        }
    }

    /// <summary>
    /// アプリケーションの安全な終了処理
    /// </summary>
    private async void ExitApplication()
    {
        // 多重起動リスナーの停止
        _activationCts?.Cancel();
        _activationEvent?.Set();
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        // バックグラウンドタスクの停止
        if (_captureService != null)
        {
            await _captureService.StopAsync();
            _captureService.Dispose();
        }

        if (_deduplicationService != null)
        {
            await _deduplicationService.StopAsync();
            _deduplicationService.Dispose();
        }

        if (_storageCleanupService != null)
        {
            await _storageCleanupService.StopAsync();
            _storageCleanupService.Dispose();
        }

        // トレイアイコンの破棄
        _trayIconService?.Dispose();

        // メインウィンドウの終了
        _mainWindow?.ForceClose();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCts?.Cancel();
        _activationEvent?.Dispose();

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        _trayIconService?.Dispose();
        _captureService?.Dispose();
        _deduplicationService?.Dispose();
        _storageCleanupService?.Dispose();
        base.OnExit(e);
    }
}
