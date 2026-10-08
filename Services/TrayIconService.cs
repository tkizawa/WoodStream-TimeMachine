using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WoodStreamTimeMachine.Resources;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// タスクトレイ（システムトレイ）のアイコンとコンテキストメニューを管理するサービス
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _openViewerAction;
    private readonly Action _openSettingsAction;
    private readonly Action _exitAction;

    public TrayIconService(
        Action openViewerAction,
        Action openSettingsAction,
        Action exitAction)
    {
        _openViewerAction = openViewerAction;
        _openSettingsAction = openSettingsAction;
        _exitAction = exitAction;

        _notifyIcon = new NotifyIcon
        {
            Text = Strings.TrayRunningTooltip,
            Visible = true
        };

        // アイコンの設定
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            _notifyIcon.Icon = new Icon(iconPath);
        }
        else
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        // コンテキストメニューの作成
        var contextMenu = new ContextMenuStrip();

        var openViewerItem = new ToolStripMenuItem(Strings.TrayOpenViewer);
        openViewerItem.Font = new Font(openViewerItem.Font, FontStyle.Bold);
        openViewerItem.Click += (s, e) => _openViewerAction();
        contextMenu.Items.Add(openViewerItem);

        var settingsItem = new ToolStripMenuItem(Strings.TraySettings);
        settingsItem.Click += (s, e) => _openSettingsAction();
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem(Strings.TrayExit);
        exitItem.Click += (s, e) => _exitAction();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        // アイコンのダブルクリック時にもビューアーを開く
        _notifyIcon.DoubleClick += (s, e) => _openViewerAction();
    }

    /// <summary>
    /// トレイ通知バルーンを表示
    /// </summary>
    public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
