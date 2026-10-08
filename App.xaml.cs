using System.Windows;
using WoodStreamTimeMachine.Services;
using WoodStreamTimeMachine.Views;

namespace WoodStreamTimeMachine;

/// <summary>
/// アプリケーションのエントリポイントおよび常駐・ライフサイクル管理
/// </summary>
public partial class App : System.Windows.Application
{
    private SettingsManager? _settingsManager;
    private CaptureService? _captureService;
    private DeduplicationService? _deduplicationService;
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 設定マネージャの初期化
        _settingsManager = new SettingsManager();

        // 2. 初回起動時のセットアップ判定
        if (_settingsManager.Current.IsFirstRun || 
            string.IsNullOrWhiteSpace(_settingsManager.Current.StorageFolderPath))
        {
            var setupWin = new SetupWindow(_settingsManager, isInitialSetup: true);
            bool? result = setupWin.ShowDialog();

            // 初回セットアップでキャンセルされた場合はアプリを終了
            if (result != true || !_settingsManager.Current.IsFirstRun == false && string.IsNullOrWhiteSpace(_settingsManager.Current.StorageFolderPath))
            {
                Shutdown();
                return;
            }
        }

        // 3. サービスの初期化
        _deduplicationService = new DeduplicationService(_settingsManager);
        _captureService = new CaptureService(_settingsManager);
        _mainWindow = new MainWindow(_settingsManager, _deduplicationService);

        // 新規キャプチャ保存イベントをビューアーに接続
        _captureService.CaptureSaved += (path, time) =>
        {
            _mainWindow?.OnNewCaptureSaved(path, time);
        };

        // 4. タスクトレイ常駐アイコンの初期化
        _trayIconService = new TrayIconService(
            openViewerAction: ShowViewer,
            openSettingsAction: ShowSettings,
            exitAction: ExitApplication
        );

        // 5. バックグラウンドキャプチャおよび重複最適化を開始
        _captureService.Start();
        _deduplicationService.Start();
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
            // 保存先が変更された場合はビューアーを再読込
            _mainWindow?.LoadCaptures(preserveIndex: false);
        }
    }

    /// <summary>
    /// アプリケーションの安全な終了処理
    /// </summary>
    private async void ExitApplication()
    {
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

        // トレイアイコンの破棄
        _trayIconService?.Dispose();

        // メインウィンドウの終了
        _mainWindow?.ForceClose();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        _captureService?.Dispose();
        _deduplicationService?.Dispose();
        base.OnExit(e);
    }
}
