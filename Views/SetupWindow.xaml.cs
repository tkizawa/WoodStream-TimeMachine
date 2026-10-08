using System.IO;
using System.Windows;
using Microsoft.Win32;
using WoodStreamTimeMachine.Resources;
using WoodStreamTimeMachine.Services;

namespace WoodStreamTimeMachine.Views;

/// <summary>
/// キャプチャ保存先フォルダの設定を行うウィンドウ（初回起動時またはトレイメニューから呼出）
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly bool _isInitialSetup;

    public bool IsConfirmed { get; private set; }

    public SetupWindow(SettingsManager settingsManager, bool isInitialSetup = false)
    {
        InitializeComponent();
        _settingsManager = settingsManager;
        _isInitialSetup = isInitialSetup;

        ApplyLocalization();
        LoadCurrentSettings();
    }

    /// <summary>
    /// 言語設定に応じたUIテキストを適用
    /// </summary>
    private void ApplyLocalization()
    {
        Title = _isInitialSetup ? Strings.SetupTitle : Strings.SettingsTitle;
        TitleTextBlock.Text = _isInitialSetup ? Strings.SetupTitle : Strings.SettingsTitle;
        DescriptionTextBlock.Text = Strings.SetupDescription;
        FolderLabelTextBlock.Text = Strings.StorageFolderLabel;
        BrowseButton.Content = Strings.BrowseButton;
        CancelButton.Content = Strings.CancelButton;
        SaveButton.Content = _isInitialSetup ? Strings.SaveAndStartButton : Strings.SaveButton;
    }

    /// <summary>
    /// 現在の設定またはデフォルト値をテキストボックスに設定
    /// </summary>
    private void LoadCurrentSettings()
    {
        string currentPath = _settingsManager.Current.StorageFolderPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            currentPath = SettingsManager.GetDefaultStoragePath();
        }
        FolderPathTextBox.Text = currentPath;
    }

    /// <summary>
    /// フォルダ参照ダイアログを表示
    /// </summary>
    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.StorageFolderLabel,
            InitialDirectory = Directory.Exists(FolderPathTextBox.Text) 
                ? FolderPathTextBox.Text 
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderPathTextBox.Text = dialog.FolderName;
            ErrorMessageTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 設定保存処理
    /// </summary>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string targetPath = FolderPathTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            ShowError(Strings.FolderNotSelectedError);
            return;
        }

        try
        {
            // ディレクトリが存在しない場合は自動作成
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
        }
        catch (Exception ex)
        {
            ShowError($"{Strings.FolderCreateFailedError} ({ex.Message})");
            return;
        }

        // 設定の更新
        _settingsManager.Current.StorageFolderPath = targetPath;
        _settingsManager.Current.IsFirstRun = false;
        _settingsManager.Save();

        IsConfirmed = true;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// キャンセル処理
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorMessageTextBlock.Text = message;
        ErrorMessageTextBlock.Visibility = Visibility.Visible;
    }
}
