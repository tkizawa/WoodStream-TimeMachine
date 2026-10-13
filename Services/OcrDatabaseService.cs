using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace WoodStreamTimeMachine.Services;

/// <summary>
/// OCR検索結果のデータモデル
/// </summary>
public class OcrSearchResult
{
    /// <summary>
    /// 画像ファイルのフルパス
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// 撮影日時
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 検索キーワード周辺のテキストスニペット
    /// </summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>
    /// 抽出されたテキスト全文
    /// </summary>
    public string FullText { get; set; } = string.Empty;
}

/// <summary>
/// キャプチャ画像のOCR抽出テキストをSQLiteデータベースで管理・検索するサービス
/// 保存先: スクリーンショット保存先フォルダ配下の "DB/timeline_ocr.db"
/// </summary>
public class OcrDatabaseService : IDisposable
{
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private string _currentDbPath = string.Empty;
    private string _connectionString = string.Empty;
    private bool _isInitialized = false;

    /// <summary>
    /// 指定された画像保存先フォルダ配下に DB サブフォルダを作成し、SQLite データベースを初期化します
    /// </summary>
    /// <param name="storageFolderPath">スクリーンショット保存先フォルダのパス</param>
    public void Initialize(string storageFolderPath)
    {
        if (string.IsNullOrWhiteSpace(storageFolderPath))
        {
            return;
        }

        try
        {
            // 保存先フォルダの直下に "DB" サブフォルダを作成
            string dbFolder = Path.Combine(storageFolderPath, "DB");
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }

            _currentDbPath = Path.Combine(dbFolder, "timeline_ocr.db");
            _connectionString = $"Data Source={_currentDbPath}";

            // テーブル初期化
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // WALモードを有効化（読み書き並行性能と信頼性の向上）
            using (var walCmd = connection.CreateCommand())
            {
                walCmd.CommandText = "PRAGMA journal_mode = WAL;";
                walCmd.ExecuteNonQuery();
            }

            // テーブルおよびインデックス作成
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS OcrRecords (
                        ImagePath TEXT PRIMARY KEY,
                        Timestamp TEXT NOT NULL,
                        CapturedText TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_ocr_timestamp ON OcrRecords(Timestamp);
                ";
                cmd.ExecuteNonQuery();
            }

            _isInitialized = true;
            Debug.WriteLine($"OcrDatabaseService 初期化完了: {_currentDbPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OcrDatabaseService 初期化エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// OCR抽出結果をデータベースに登録または更新（Upsert）します
    /// </summary>
    public async Task UpsertRecordAsync(string imagePath, DateTime timestamp, string capturedText)
    {
        if (!_isInitialized || string.IsNullOrWhiteSpace(capturedText))
        {
            return;
        }

        await _dbLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO OcrRecords (ImagePath, Timestamp, CapturedText)
                VALUES (@ImagePath, @Timestamp, @CapturedText)
                ON CONFLICT(ImagePath) DO UPDATE SET
                    Timestamp = excluded.Timestamp,
                    CapturedText = excluded.CapturedText;
            ";

            cmd.Parameters.AddWithValue("@ImagePath", imagePath);
            cmd.Parameters.AddWithValue("@Timestamp", timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            cmd.Parameters.AddWithValue("@CapturedText", capturedText);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OCRレコード登録エラー ({imagePath}): {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// キーワードでOCRテキストを部分一致検索し、一致した結果リストを返します
    /// </summary>
    public async Task<List<OcrSearchResult>> SearchAsync(string query, int limit = 100)
    {
        var results = new List<OcrSearchResult>();
        if (!_isInitialized || string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        string trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return results;
        }

        await _dbLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string noSpaceQuery = trimmed.Replace(" ", "").Replace("　", "");

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT ImagePath, Timestamp, CapturedText
                FROM OcrRecords
                WHERE CapturedText LIKE @pattern
                   OR replace(replace(CapturedText, ' ', ''), '　', '') LIKE @noSpacePattern
                ORDER BY Timestamp DESC
                LIMIT @Limit;
            ";

            cmd.Parameters.AddWithValue("@pattern", $"%{trimmed}%");
            cmd.Parameters.AddWithValue("@noSpacePattern", $"%{noSpaceQuery}%");
            cmd.Parameters.AddWithValue("@Limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string path = reader.GetString(0);
                string timeStr = reader.GetString(1);
                string text = reader.GetString(2);

                DateTime time = DateTime.TryParse(timeStr, out var parsed) ? parsed : DateTime.MinValue;
                string snippet = CreateSnippet(text, trimmed);

                results.Add(new OcrSearchResult
                {
                    ImagePath = path,
                    Timestamp = time,
                    Snippet = snippet,
                    FullText = text
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OCR検索エラー (query: {query}): {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }

        return results;
    }

    /// <summary>
    /// 既にOCR処理済みかどうかを判定します
    /// </summary>
    public async Task<bool> IsProcessedAsync(string imagePath)
    {
        if (!_isInitialized) return false;

        await _dbLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM OcrRecords WHERE ImagePath = @ImagePath LIMIT 1;";
            cmd.Parameters.AddWithValue("@ImagePath", imagePath);

            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }
        catch
        {
            return false;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// 指定された画像パスのレコードをデータベースから削除します
    /// </summary>
    public async Task DeleteRecordAsync(string imagePath)
    {
        if (!_isInitialized) return;

        await _dbLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM OcrRecords WHERE ImagePath = @ImagePath;";
            cmd.Parameters.AddWithValue("@ImagePath", imagePath);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OCRレコード削除エラー: {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// 画像実体が存在しない孤立したレコードをデータベースからクリーンアップします
    /// </summary>
    public async Task CleanMissingRecordsAsync()
    {
        if (!_isInitialized) return;

        await _dbLock.WaitAsync();
        try
        {
            var pathsToDelete = new List<string>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT ImagePath FROM OcrRecords;";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string path = reader.GetString(0);
                    if (!File.Exists(path))
                    {
                        pathsToDelete.Add(path);
                    }
                }
            }

            if (pathsToDelete.Count > 0)
            {
                using var tx = connection.BeginTransaction();
                using var deleteCmd = connection.CreateCommand();
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = "DELETE FROM OcrRecords WHERE ImagePath = @ImagePath;";
                var p = deleteCmd.Parameters.Add("@ImagePath", SqliteType.Text);

                foreach (var path in pathsToDelete)
                {
                    p.Value = path;
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                Debug.WriteLine($"孤立OCRレコードクリーンアップ完了: {pathsToDelete.Count} 件削除");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"孤立OCRレコードクリーンアップ例外: {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// 検索キーワードの前後の文脈を抽出したスニペット文字列を生成します（文字間の空白にも対応）
    /// </summary>
    private static string CreateSnippet(string fullText, string keyword, int contextLength = 35)
    {
        if (string.IsNullOrEmpty(fullText) || string.IsNullOrEmpty(keyword))
        {
            return string.Empty;
        }

        // 改行をスペースに置換して整形
        string normalized = fullText.Replace("\r", " ").Replace("\n", " ").Trim();

        // 1. そのまま完全一致で検索
        int index = normalized.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        int matchLength = keyword.Length;

        // 2. 一致しない場合、文字間の空白を許容する正規表現で検索（例: "ガンダム" -> "ガ\s*ン\s*タ\s*ム"）
        if (index < 0)
        {
            try
            {
                string nonSpaceKeyword = keyword.Replace(" ", "").Replace("　", "");
                if (!string.IsNullOrEmpty(nonSpaceKeyword))
                {
                    string pattern = string.Join(@"\s*", nonSpaceKeyword.Select(c => Regex.Escape(c.ToString())));
                    var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        index = match.Index;
                        matchLength = match.Length;
                    }
                }
            }
            catch
            {
                // 正規表現エラー時はフォールバック
            }
        }

        if (index < 0)
        {
            return normalized.Length > contextLength * 2 ? normalized[..(contextLength * 2)] + "..." : normalized;
        }

        int start = Math.Max(0, index - contextLength);
        int end = Math.Min(normalized.Length, index + matchLength + contextLength);

        string snippet = normalized[start..end];
        if (start > 0) snippet = "..." + snippet;
        if (end < normalized.Length) snippet += "...";

        return snippet;
    }

    public void Dispose()
    {
        _dbLock.Dispose();
    }
}
