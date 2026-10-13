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

    private record ComboBoxOption(int Value, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

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
        CaptureIntervalLabelTextBlock.Text = Strings.CaptureIntervalLabel;
        StorageLimitLabelTextBlock.Text = Strings.StorageLimitLabel;
        StorageLimitNoteTextBlock.Text = Strings.StorageLimitDescription;
        StartWithWindowsTextBlock.Text = Strings.StartWithWindowsLabel;
        EnableOcrTextBlock.Text = Strings.EnableOcrLabel;
        BrowseButton.Content = Strings.BrowseButton;
        CancelButton.Content = Strings.CancelButton;
        SaveButton.Content = _isInitialSetup ? Strings.SaveAndStartButton : Strings.SaveButton;

        // ドロップダウンリストの選択肢を生成
        PopulateDropdownOptions();
    }

    /// <summary>
    /// 記録間隔（1〜60秒）および保存容量上限（10〜1000MB）の選択肢を生成
    /// </summary>
    private void PopulateDropdownOptions()
    {
        // 1. 記録間隔ドロップダウン（1秒〜60秒、1秒刻み）
        CaptureIntervalComboBox.Items.Clear();
        string secUnit = Strings.SecondsUnit;
        for (int i = 1; i <= 60; i++)
        {
            CaptureIntervalComboBox.Items.Add(new ComboBoxOption(i, $"{i} {secUnit}"));
        }

        // 2. 容量制限ドロップダウン（10MB〜1000MB、10MB刻み）
        StorageLimitComboBox.Items.Clear();
        for (int i = 10; i <= 1000; i += 10)
        {
            StorageLimitComboBox.Items.Add(new ComboBoxOption(i, $"{i} MB"));
        }
    }

    /// <summary>
    /// 現在の設定またはデフォルト値を各コントロールに反映
    /// </summary>
    private void LoadCurrentSettings()
    {
        string currentPath = _settingsManager.Current.StorageFolderPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            currentPath = SettingsManager.GetDefaultStoragePath();
        }
        FolderPathTextBox.Text = currentPath;

        // 記録間隔の復元（デフォルト3秒、1〜60秒）
        int currentInterval = _settingsManager.Current.CaptureIntervalSeconds <= 0 ? 3 : _settingsManager.Current.CaptureIntervalSeconds;
        var intervalOption = CaptureIntervalComboBox.Items
            .OfType<ComboBoxOption>()
            .FirstOrDefault(opt => opt.Value == currentInterval);

        if (intervalOption != null)
        {
            CaptureIntervalComboBox.SelectedItem = intervalOption;
        }
        else
        {
            var customOption = new ComboBoxOption(currentInterval, $"{currentInterval} {Strings.SecondsUnit}");
            CaptureIntervalComboBox.Items.Add(customOption);
            CaptureIntervalComboBox.SelectedItem = customOption;
        }

        // 保存容量上限の復元（デフォルト150MB、10〜1000MB）
        int currentLimit = _settingsManager.Current.MaxStorageSizeMB <= 0 ? 150 : _settingsManager.Current.MaxStorageSizeMB;
        var limitOption = StorageLimitComboBox.Items
            .OfType<ComboBoxOption>()
            .FirstOrDefault(opt => opt.Value == currentLimit);

        if (limitOption != null)
        {
            StorageLimitComboBox.SelectedItem = limitOption;
        }
        else
        {
            var customOption = new ComboBoxOption(currentLimit, $"{currentLimit} MB");
            StorageLimitComboBox.Items.Add(customOption);
            StorageLimitComboBox.SelectedItem = customOption;
        }

        StartWithWindowsCheckBox.IsChecked = _settingsManager.Current.StartWithWindows || StartupService.IsStartupEnabled();
        EnableOcrCheckBox.IsChecked = _settingsManager.Current.EnableOcr;
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

        // ドロップダウンからの選択値取得
        int interval = (CaptureIntervalComboBox.SelectedItem as ComboBoxOption)?.Value ?? 3;
        int limitMB = (StorageLimitComboBox.SelectedItem as ComboBoxOption)?.Value ?? 150;

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

        // Windows自動起動の登録・解除
        bool startWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        StartupService.SetStartup(startWithWindows);

        // 設定の更新と保存
        _settingsManager.Current.StorageFolderPath = targetPath;
        _settingsManager.Current.CaptureIntervalSeconds = interval;
        _settingsManager.Current.MaxStorageSizeMB = limitMB;
        _settingsManager.Current.StartWithWindows = startWithWindows;
        _settingsManager.Current.EnableOcr = EnableOcrCheckBox.IsChecked == true;
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
