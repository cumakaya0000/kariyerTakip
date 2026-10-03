using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Common;
using KariyerTakip.Models;

namespace KariyerTakip.Storage;

public class AnnouncementRepository : IAnnouncementRepository
{
    private readonly string _connectionString;
    private readonly ILogger<AnnouncementRepository> _logger;

    public AnnouncementRepository(IOptions<AppConfig> config, ILogger<AnnouncementRepository> logger)
    {
        _logger = logger;
        var dbPath = !string.IsNullOrWhiteSpace(config.Value.DatabasePath)
            ? (Path.IsPathFullyQualified(config.Value.DatabasePath) ? Path.GetFullPath(config.Value.DatabasePath) : Path.GetFullPath(config.Value.DatabasePath, AppPaths.BaseDirectory))
            : AppPaths.DatabaseFile;

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = $"Data Source={dbPath}";
    }

    private SqliteConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task InitializeDatabaseAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        var ddl = @"
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS Announcements (
                Guid TEXT PRIMARY KEY,
                InstitutionName TEXT NOT NULL,
                UnitName TEXT,
                Title TEXT NOT NULL,
                AnnouncementType TEXT,
                DetailUrl TEXT,
                ApplicationUrl TEXT,
                StartDate TEXT,
                EndDate TEXT,
                RawGeneralText TEXT,
                RawContentHash TEXT,
                FirstSeenAt TEXT NOT NULL,
                LastCheckedAt TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                LastScanStatus TEXT NOT NULL DEFAULT 'Success'
            );

            CREATE TABLE IF NOT EXISTS Positions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PositionKey TEXT NOT NULL UNIQUE,
                AnnouncementGuid TEXT NOT NULL,
                Title TEXT NOT NULL,
                Unvan TEXT,
                Cities TEXT,
                Quota INTEGER NOT NULL DEFAULT 0,
                RawText TEXT,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (AnnouncementGuid) REFERENCES Announcements (Guid) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Evaluations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AnnouncementGuid TEXT NOT NULL,
                PositionKey TEXT NOT NULL,
                ProfileHash TEXT NOT NULL,
                Status TEXT NOT NULL,
                SummaryReason TEXT,
                DetailsJson TEXT,
                EvaluatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS NotificationOutbox (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeduplicationKey TEXT NOT NULL UNIQUE,
                AnnouncementGuid TEXT NOT NULL,
                NotificationType TEXT NOT NULL,
                MessagePayload TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                RetryCount INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT,
                CreatedAt TEXT NOT NULL,
                SentAt TEXT
            );

            CREATE TABLE IF NOT EXISTS ScanRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAt TEXT NOT NULL,
                FinishedAt TEXT,
                Status TEXT NOT NULL DEFAULT 'Running',
                TotalAnnouncementsFound INTEGER NOT NULL DEFAULT 0,
                ProcessedCount INTEGER NOT NULL DEFAULT 0,
                FailedCount INTEGER NOT NULL DEFAULT 0,
                EligibleCount INTEGER NOT NULL DEFAULT 0,
                NeedsReviewCount INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT
            );

        ";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync();

        // Auto-migration for existing databases: Ensure newly added columns exist
        await EnsureColumnExistsAsync(conn, "Announcements", "RawGeneralText", "TEXT");
        await EnsureColumnExistsAsync(conn, "Announcements", "LastScanStatus", "TEXT NOT NULL DEFAULT 'Success'");
        await EnsureColumnExistsAsync(conn, "Announcements", "ApplicationStatus", "TEXT NOT NULL DEFAULT 'None'");
        await EnsureColumnExistsAsync(conn, "Announcements", "ApplicationNotes", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(conn, "Positions", "PositionKey", "TEXT");
        await EnsureColumnExistsAsync(conn, "Positions", "UpdatedAt", "TEXT NOT NULL DEFAULT '2026-01-01'");
        await EnsureColumnExistsAsync(conn, "Positions", "IsCurrent", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnExistsAsync(conn, "Evaluations", "PositionKey", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(conn, "NotificationOutbox", "DeduplicationKey", "TEXT");
        await EnsureColumnExistsAsync(conn, "NotificationOutbox", "NextChunkIndex", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(conn, "NotificationOutbox", "RetryAfterUtc", "TEXT");
        await EnsureColumnExistsAsync(conn, "ScanRuns", "Status", "TEXT NOT NULL DEFAULT 'Success'");
        await EnsureColumnExistsAsync(conn, "ScanRuns", "ProcessedCount", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(conn, "ScanRuns", "FailedCount", "INTEGER NOT NULL DEFAULT 0");
        await MigrateLegacyScanRunsAsync(conn);
        using var backfillCmd = conn.CreateCommand();
        backfillCmd.CommandText = @"
            UPDATE Positions SET PositionKey = AnnouncementGuid || '_pos_' || Id WHERE PositionKey IS NULL OR PositionKey = '';
        ";
        await backfillCmd.ExecuteNonQueryAsync();
        await MigrateLegacyOutboxAsync(conn);

        // Create indexes after legacy columns and keys have been migrated.
        using var idxCmd = conn.CreateCommand();
        idxCmd.CommandText = @"
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Positions_Key_Unique ON Positions(PositionKey);
            CREATE INDEX IF NOT EXISTS IX_Outbox_Status ON NotificationOutbox(Status);
            CREATE INDEX IF NOT EXISTS IX_Outbox_Dedup ON NotificationOutbox(DeduplicationKey);
            CREATE INDEX IF NOT EXISTS IX_Positions_Guid ON Positions(AnnouncementGuid);
            CREATE INDEX IF NOT EXISTS IX_Positions_Key ON Positions(PositionKey);
            CREATE INDEX IF NOT EXISTS IX_Evaluations_Guid_Key ON Evaluations(AnnouncementGuid, PositionKey);
            CREATE INDEX IF NOT EXISTS IX_Evaluations_Profile ON Evaluations(ProfileHash);
        ";
        await idxCmd.ExecuteNonQueryAsync();

        _logger.LogInformation("SQLite veritabanı şeması doğrulandı.");
    }

    private static async Task MigrateLegacyOutboxAsync(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE NotificationOutbox
            SET DeduplicationKey = AnnouncementGuid || ':' || NotificationType || ':' || Id
            WHERE DeduplicationKey IS NULL OR DeduplicationKey = '';

            -- Keep a sent record as the canonical key so it cannot be sent again.
            -- Preserve duplicate history under separate keys and stop duplicate retries.
            WITH Ranked AS (
                SELECT Id, ROW_NUMBER() OVER (
                    PARTITION BY DeduplicationKey
                    ORDER BY CASE WHEN Status = 'Sent' THEN 0 ELSE 1 END, Id
                ) AS Rank
                FROM NotificationOutbox
            )
            UPDATE NotificationOutbox
            SET DeduplicationKey = 'legacy-duplicate:' || Id || ':' || hex(randomblob(16)),
                Status = CASE WHEN Status IN ('Pending', 'Failed') THEN 'Disabled' ELSE Status END
            WHERE Id IN (SELECT Id FROM Ranked WHERE Rank > 1);

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Outbox_Dedup_Unique
                ON NotificationOutbox(DeduplicationKey);
        ";
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private static async Task EnsureColumnExistsAsync(SqliteConnection conn, string table, string column, string columnType)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = await cmd.ExecuteReaderAsync();
        var exists = false;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (name.Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();

        if (!exists)
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnType};";
            await alterCmd.ExecuteNonQueryAsync();
        }
    }
    // Migration for legacy ScanRuns schema (removes old Success column if present)
    private static async Task MigrateLegacyScanRunsAsync(SqliteConnection conn)
    {
        // Check if the old 'Success' column exists
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(ScanRuns);";
        using var reader = await checkCmd.ExecuteReaderAsync();
        var hasSuccess = false;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (name.Equals("Success", StringComparison.OrdinalIgnoreCase))
            {
                hasSuccess = true;
                break;
            }
        }
        reader.Close();

        if (!hasSuccess)
            return; // nothing to migrate

        // Perform migration: rebuild table without the Success column
        using var tx = conn.BeginTransaction();
        // Create new table with correct schema
        var createCmd = conn.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE ScanRuns_new (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAt TEXT NOT NULL,
                FinishedAt TEXT,
                Status TEXT NOT NULL DEFAULT 'Running',
                TotalAnnouncementsFound INTEGER NOT NULL DEFAULT 0,
                ProcessedCount INTEGER NOT NULL DEFAULT 0,
                FailedCount INTEGER NOT NULL DEFAULT 0,
                EligibleCount INTEGER NOT NULL DEFAULT 0,
                NeedsReviewCount INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT
            );";
        await createCmd.ExecuteNonQueryAsync();

        // Copy data (ignore the old Success column)
        var copyCmd = conn.CreateCommand();
        copyCmd.CommandText = @"
            INSERT INTO ScanRuns_new (Id, StartedAt, FinishedAt, Status,
                TotalAnnouncementsFound, ProcessedCount, FailedCount,
                EligibleCount, NeedsReviewCount, ErrorMessage)
            SELECT Id, StartedAt, FinishedAt, Status,
                TotalAnnouncementsFound, ProcessedCount, FailedCount,
                EligibleCount, NeedsReviewCount, ErrorMessage
            FROM ScanRuns;";
        await copyCmd.ExecuteNonQueryAsync();

        // Drop old table and rename new
        var dropCmd = conn.CreateCommand();
        dropCmd.CommandText = "DROP TABLE ScanRuns;";
        await dropCmd.ExecuteNonQueryAsync();

        var renameCmd = conn.CreateCommand();
        renameCmd.CommandText = "ALTER TABLE ScanRuns_new RENAME TO ScanRuns;";
        await renameCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }

    public async Task<List<AnnouncementRecord>> GetAllAnnouncementsAsync(bool activeOnly = false)
    {
        var list = new List<AnnouncementRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Guid, InstitutionName, UnitName, Title, AnnouncementType, DetailUrl, ApplicationUrl,
                                   StartDate, EndDate, RawGeneralText, RawContentHash, FirstSeenAt, LastCheckedAt, IsActive, LastScanStatus, ApplicationStatus, ApplicationNotes
                            FROM Announcements" + (activeOnly ? " WHERE IsActive = 1" : "") + " ORDER BY EndDate ASC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadAnnouncementRecord(reader));
        }
        return list;
    }

    public async Task<AnnouncementRecord?> GetAnnouncementByGuidAsync(string guid)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Guid, InstitutionName, UnitName, Title, AnnouncementType, DetailUrl, ApplicationUrl,
                                   StartDate, EndDate, RawGeneralText, RawContentHash, FirstSeenAt, LastCheckedAt, IsActive, LastScanStatus, ApplicationStatus, ApplicationNotes
                            FROM Announcements WHERE Guid = @Guid";
        cmd.Parameters.AddWithValue("@Guid", guid);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadAnnouncementRecord(reader);
        }

        return null;
    }

    public async Task UpsertAnnouncementAsync(AnnouncementRecord record)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Announcements (
                Guid, InstitutionName, UnitName, Title, AnnouncementType, DetailUrl, ApplicationUrl,
                StartDate, EndDate, RawGeneralText, RawContentHash, FirstSeenAt, LastCheckedAt, IsActive, LastScanStatus
            ) VALUES (
                @Guid, @InstitutionName, @UnitName, @Title, @AnnouncementType, @DetailUrl, @ApplicationUrl,
                @StartDate, @EndDate, @RawGeneralText, @RawContentHash, @FirstSeenAt, @LastCheckedAt, @IsActive, @LastScanStatus
            )
            ON CONFLICT(Guid) DO UPDATE SET
                InstitutionName = excluded.InstitutionName,
                UnitName = excluded.UnitName,
                Title = excluded.Title,
                AnnouncementType = excluded.AnnouncementType,
                DetailUrl = excluded.DetailUrl,
                ApplicationUrl = excluded.ApplicationUrl,
                StartDate = excluded.StartDate,
                EndDate = excluded.EndDate,
                RawGeneralText = excluded.RawGeneralText,
                RawContentHash = excluded.RawContentHash,
                LastCheckedAt = excluded.LastCheckedAt,
                IsActive = excluded.IsActive,
                LastScanStatus = excluded.LastScanStatus;
        ";

        cmd.Parameters.AddWithValue("@Guid", record.Guid);
        cmd.Parameters.AddWithValue("@InstitutionName", record.InstitutionName);
        cmd.Parameters.AddWithValue("@UnitName", (object?)record.UnitName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Title", record.Title);
        cmd.Parameters.AddWithValue("@AnnouncementType", (object?)record.AnnouncementType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DetailUrl", (object?)record.DetailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ApplicationUrl", (object?)record.ApplicationUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StartDate", record.StartDate.HasValue ? (object)AppTime.ToUtc(record.StartDate.Value).ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@EndDate", record.EndDate.HasValue ? (object)AppTime.ToUtc(record.EndDate.Value).ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@RawGeneralText", (object?)record.RawGeneralText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RawContentHash", (object?)record.RawContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FirstSeenAt", record.FirstSeenAt.ToString("o"));
        cmd.Parameters.AddWithValue("@LastCheckedAt", record.LastCheckedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@IsActive", record.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@LastScanStatus", record.LastScanStatus);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertPositionsAsync(string announcementGuid, List<PositionRecord> positions)
    {
        if (positions == null) throw new ArgumentNullException(nameof(positions));

        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        using var deactivate = conn.CreateCommand();
        deactivate.Transaction = tx;
        deactivate.CommandText = "UPDATE Positions SET IsCurrent = 0 WHERE AnnouncementGuid = @Guid";
        deactivate.Parameters.AddWithValue("@Guid", announcementGuid);
        await deactivate.ExecuteNonQueryAsync();

        foreach (var pos in positions)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO Positions (PositionKey, AnnouncementGuid, Title, Unvan, Cities, Quota, RawText, UpdatedAt, IsCurrent)
                VALUES (@PositionKey, @AnnouncementGuid, @Title, @Unvan, @Cities, @Quota, @RawText, @UpdatedAt, 1)
                ON CONFLICT(PositionKey) DO UPDATE SET
                    Title = excluded.Title,
                    Unvan = excluded.Unvan,
                    Cities = excluded.Cities,
                    Quota = excluded.Quota,
                    RawText = excluded.RawText,
                    UpdatedAt = excluded.UpdatedAt,
                    IsCurrent = 1;
            ";

            cmd.Parameters.AddWithValue("@PositionKey", pos.PositionKey);
            cmd.Parameters.AddWithValue("@AnnouncementGuid", announcementGuid);
            cmd.Parameters.AddWithValue("@Title", pos.Title);
            cmd.Parameters.AddWithValue("@Unvan", (object?)pos.Unvan ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cities", (object?)pos.Cities ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quota", pos.Quota);
            cmd.Parameters.AddWithValue("@RawText", (object?)pos.RawText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<List<PositionRecord>> GetPositionsByAnnouncementGuidAsync(string announcementGuid)
    {
        var list = new List<PositionRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, PositionKey, AnnouncementGuid, Title, Unvan, Cities, Quota, RawText, UpdatedAt
                            FROM Positions WHERE AnnouncementGuid = @Guid AND IsCurrent = 1 ORDER BY Id ASC";
        cmd.Parameters.AddWithValue("@Guid", announcementGuid);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PositionRecord
            {
                Id = reader.GetInt64(0),
                PositionKey = reader.GetString(1),
                AnnouncementGuid = reader.GetString(2),
                Title = reader.GetString(3),
                Unvan = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Cities = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Quota = reader.GetInt32(6),
                RawText = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                UpdatedAt = AppTime.ParseUtc(reader.GetString(8))
            });
        }
        return list;
    }

    public async Task SaveEvaluationAsync(EvaluationRecord evaluation)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Evaluations (AnnouncementGuid, PositionKey, ProfileHash, Status, SummaryReason, DetailsJson, EvaluatedAt)
            VALUES (@AnnouncementGuid, @PositionKey, @ProfileHash, @Status, @SummaryReason, @DetailsJson, @EvaluatedAt);";

        cmd.Parameters.AddWithValue("@AnnouncementGuid", evaluation.AnnouncementGuid);
        cmd.Parameters.AddWithValue("@PositionKey", evaluation.PositionKey);
        cmd.Parameters.AddWithValue("@ProfileHash", evaluation.ProfileHash);
        cmd.Parameters.AddWithValue("@Status", evaluation.Status);
        cmd.Parameters.AddWithValue("@SummaryReason", (object?)evaluation.SummaryReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DetailsJson", (object?)evaluation.DetailsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@EvaluatedAt", evaluation.EvaluatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<EvaluationRecord>> GetLatestEvaluationsByAnnouncementAsync(string announcementGuid, string? profileHash = null)
    {
        var list = new List<EvaluationRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        // Query latest evaluation per PositionKey for this announcement and profile
        if (!string.IsNullOrEmpty(profileHash))
        {
            cmd.CommandText = @"
                SELECT e.Id, e.AnnouncementGuid, e.PositionKey, e.ProfileHash, e.Status, e.SummaryReason, e.DetailsJson, e.EvaluatedAt
                FROM Evaluations e
                INNER JOIN (
                    SELECT PositionKey, MAX(Id) as MaxId
                    FROM Evaluations
                    WHERE AnnouncementGuid = @Guid AND ProfileHash = @ProfileHash
                    GROUP BY PositionKey
                ) latest ON e.Id = latest.MaxId
                ORDER BY e.Id ASC;";
            cmd.Parameters.AddWithValue("@ProfileHash", profileHash);
        }
        else
        {
            cmd.CommandText = @"
                SELECT e.Id, e.AnnouncementGuid, e.PositionKey, e.ProfileHash, e.Status, e.SummaryReason, e.DetailsJson, e.EvaluatedAt
                FROM Evaluations e
                INNER JOIN (
                    SELECT PositionKey, MAX(Id) as MaxId
                    FROM Evaluations
                    WHERE AnnouncementGuid = @Guid
                    GROUP BY PositionKey
                ) latest ON e.Id = latest.MaxId
                ORDER BY e.Id ASC;";
        }
        cmd.Parameters.AddWithValue("@Guid", announcementGuid);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadEvaluationRecord(reader));
        }
        return list;
    }

    public async Task<Dictionary<string, List<EvaluationRecord>>> GetLatestEvaluationsMapAsync(string? profileHash = null)
    {
        var map = new Dictionary<string, List<EvaluationRecord>>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrEmpty(profileHash))
        {
            cmd.CommandText = @"
                SELECT e.Id, e.AnnouncementGuid, e.PositionKey, e.ProfileHash, e.Status, e.SummaryReason, e.DetailsJson, e.EvaluatedAt
                FROM Evaluations e
                INNER JOIN (
                    SELECT AnnouncementGuid, PositionKey, MAX(Id) as MaxId
                    FROM Evaluations
                    WHERE ProfileHash = @ProfileHash
                    GROUP BY AnnouncementGuid, PositionKey
                ) latest ON e.Id = latest.MaxId;";
            cmd.Parameters.AddWithValue("@ProfileHash", profileHash);
        }
        else
        {
            cmd.CommandText = @"
                SELECT e.Id, e.AnnouncementGuid, e.PositionKey, e.ProfileHash, e.Status, e.SummaryReason, e.DetailsJson, e.EvaluatedAt
                FROM Evaluations e
                INNER JOIN (
                    SELECT AnnouncementGuid, PositionKey, MAX(Id) as MaxId
                    FROM Evaluations
                    GROUP BY AnnouncementGuid, PositionKey
                ) latest ON e.Id = latest.MaxId;";
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = ReadEvaluationRecord(reader);
            if (!map.TryGetValue(item.AnnouncementGuid, out var list))
            {
                list = new List<EvaluationRecord>();
                map[item.AnnouncementGuid] = list;
            }
            list.Add(item);
        }

        return map;
    }

    public async Task<bool> QueueNotificationAsync(OutboxNotificationRecord notification)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO NotificationOutbox (DeduplicationKey, AnnouncementGuid, NotificationType, MessagePayload, Status, RetryCount, CreatedAt)
            VALUES (@DeduplicationKey, @AnnouncementGuid, @NotificationType, @MessagePayload, 'Pending', 0, @CreatedAt)
            ON CONFLICT(DeduplicationKey) DO NOTHING;";

        cmd.Parameters.AddWithValue("@DeduplicationKey", notification.DeduplicationKey);
        cmd.Parameters.AddWithValue("@AnnouncementGuid", notification.AnnouncementGuid);
        cmd.Parameters.AddWithValue("@NotificationType", notification.NotificationType);
        cmd.Parameters.AddWithValue("@MessagePayload", notification.MessagePayload);
        cmd.Parameters.AddWithValue("@CreatedAt", notification.CreatedAt.ToString("o"));

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<List<OutboxNotificationRecord>> GetPendingNotificationsAsync(int limit = 50)
    {
        var list = new List<OutboxNotificationRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, DeduplicationKey, AnnouncementGuid, NotificationType, MessagePayload, Status, RetryCount, ErrorMessage, CreatedAt, SentAt, NextChunkIndex, RetryAfterUtc
                            FROM NotificationOutbox
                            WHERE (Status = 'Pending' OR (Status = 'Failed' AND RetryCount < 5))
                              AND (RetryAfterUtc IS NULL OR julianday(RetryAfterUtc) <= julianday(@Now))
                            ORDER BY Id ASC LIMIT @Limit";
        cmd.Parameters.AddWithValue("@Limit", limit);
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new OutboxNotificationRecord
            {
                Id = reader.GetInt64(0),
                DeduplicationKey = reader.GetString(1),
                AnnouncementGuid = reader.GetString(2),
                NotificationType = reader.GetString(3),
                MessagePayload = reader.GetString(4),
                Status = reader.GetString(5),
                RetryCount = reader.GetInt32(6),
                ErrorMessage = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = AppTime.ParseUtc(reader.GetString(8)),
                SentAt = reader.IsDBNull(9) ? null : AppTime.ParseUtc(reader.GetString(9)),
                NextChunkIndex = reader.GetInt32(10),
                RetryAfterUtc = reader.IsDBNull(11) ? null : AppTime.ParseUtc(reader.GetString(11))
            });
        }
        return list;
    }

    public async Task MarkNotificationSentAsync(long id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE NotificationOutbox SET Status = 'Sent', SentAt = @SentAt, ErrorMessage = NULL WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@SentAt", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkNotificationFailedAsync(long id, string errorMessage, DateTime? retryAfterUtc = null, bool permanent = false)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE NotificationOutbox SET Status = @Status, RetryCount = RetryCount + 1, ErrorMessage = @Error, RetryAfterUtc = @RetryAfter WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Error", errorMessage);
        cmd.Parameters.AddWithValue("@Status", permanent ? "PermanentFailure" : "Failed");
        cmd.Parameters.AddWithValue("@RetryAfter", (object?)retryAfterUtc?.ToString("o") ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveNotificationProgressAsync(long id, int nextChunkIndex)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE NotificationOutbox SET NextChunkIndex = MAX(NextChunkIndex, @Next) WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Next", nextChunkIndex);
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkNotificationDisabledAsync(long id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE NotificationOutbox SET Status = 'Disabled' WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> HasNotificationBeenSentAsync(string deduplicationKey)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(1) FROM NotificationOutbox 
                            WHERE DeduplicationKey = @Key AND Status = 'Sent'";
        cmd.Parameters.AddWithValue("@Key", deduplicationKey);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    public async Task<long> RecordScanStartAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO ScanRuns (StartedAt, Status, TotalAnnouncementsFound, ProcessedCount, FailedCount, EligibleCount, NeedsReviewCount)
                            VALUES (@StartedAt, 'Running', 0, 0, 0, 0, 0);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@StartedAt", DateTime.UtcNow.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task RecordScanEndAsync(
        long scanId,
        ScanStatus status,
        int totalFound,
        int processedCount,
        int failedCount,
        int eligibleCount,
        int needsReviewCount,
        string? errorMessage)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE ScanRuns SET
                                FinishedAt = @FinishedAt,
                                Status = @Status,
                                TotalAnnouncementsFound = @Total,
                                ProcessedCount = @Processed,
                                FailedCount = @Failed,
                                EligibleCount = @Eligible,
                                NeedsReviewCount = @NeedsReview,
                                ErrorMessage = @Error
                            WHERE Id = @Id";

        cmd.Parameters.AddWithValue("@Id", scanId);
        cmd.Parameters.AddWithValue("@FinishedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@Total", totalFound);
        cmd.Parameters.AddWithValue("@Processed", processedCount);
        cmd.Parameters.AddWithValue("@Failed", failedCount);
        cmd.Parameters.AddWithValue("@Eligible", eligibleCount);
        cmd.Parameters.AddWithValue("@NeedsReview", needsReviewCount);
        cmd.Parameters.AddWithValue("@Error", (object?)errorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ScanRunRecord?> GetLastSuccessfulScanAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, StartedAt, FinishedAt, Status, TotalAnnouncementsFound, ProcessedCount, FailedCount, EligibleCount, NeedsReviewCount, ErrorMessage
                            FROM ScanRuns WHERE Status = 'Success' ORDER BY Id DESC LIMIT 1";

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ScanRunRecord
            {
                Id = reader.GetInt64(0),
                StartedAt = AppTime.ParseUtc(reader.GetString(1)),
                FinishedAt = reader.IsDBNull(2) ? null : AppTime.ParseUtc(reader.GetString(2)),
                Status = reader.GetString(3),
                TotalAnnouncementsFound = reader.GetInt32(4),
                ProcessedCount = reader.GetInt32(5),
                FailedCount = reader.GetInt32(6),
                EligibleCount = reader.GetInt32(7),
                NeedsReviewCount = reader.GetInt32(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
        }

        return null;
    }

    private static AnnouncementRecord ReadAnnouncementRecord(SqliteDataReader reader)
    {
        return new AnnouncementRecord
        {
            Guid = reader.GetString(0),
            InstitutionName = reader.GetString(1),
            UnitName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            Title = reader.GetString(3),
            AnnouncementType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            DetailUrl = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            ApplicationUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            StartDate = reader.IsDBNull(7) ? null : AppTime.ParseUtc(reader.GetString(7), portalDate: true),
            EndDate = reader.IsDBNull(8) ? null : AppTime.ParseUtc(reader.GetString(8), portalDate: true),
            RawGeneralText = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            RawContentHash = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            FirstSeenAt = AppTime.ParseUtc(reader.GetString(11)),
            LastCheckedAt = AppTime.ParseUtc(reader.GetString(12)),
            IsActive = reader.GetInt32(13) == 1,
            LastScanStatus = reader.IsDBNull(14) ? "Success" : reader.GetString(14),
            ApplicationStatus = Enum.TryParse<ApplicationStatus>(reader.GetString(15), out var status) ? status : ApplicationStatus.None,
            ApplicationNotes = reader.GetString(16)
        };
    }

    public async Task SaveApplicationTrackingAsync(string guid, ApplicationStatus status, string notes)
    {
        using var conn = CreateConnection(); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Announcements SET ApplicationStatus = @Status, ApplicationNotes = @Notes WHERE Guid = @Guid";
        cmd.Parameters.AddWithValue("@Status", status.ToString()); cmd.Parameters.AddWithValue("@Notes", notes);
        cmd.Parameters.AddWithValue("@Guid", guid);
        if (await cmd.ExecuteNonQueryAsync() == 0) throw new InvalidOperationException("İlan bulunamadı.");
    }

    public async Task DeferPendingNotificationsAsync(DateTime retryAfterUtc)
    {
        using var conn = CreateConnection(); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE NotificationOutbox SET RetryAfterUtc = @RetryAfter
            WHERE Status IN ('Pending', 'Failed') AND (RetryAfterUtc IS NULL OR julianday(RetryAfterUtc) < julianday(@RetryAfter))";
        cmd.Parameters.AddWithValue("@RetryAfter", retryAfterUtc.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkExpiredAnnouncementsAsync()
    {
        // Read through the UTC converter so legacy portal-local dates are handled correctly too.
        var announcements = await GetAllAnnouncementsAsync(activeOnly: true);
        using var conn = CreateConnection(); await conn.OpenAsync();
        foreach (var announcement in announcements.Where(a => a.EndDate.HasValue && a.EndDate < DateTime.UtcNow))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Announcements SET IsActive = 0 WHERE Guid = @Guid";
            cmd.Parameters.AddWithValue("@Guid", announcement.Guid);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static EvaluationRecord ReadEvaluationRecord(SqliteDataReader reader)
    {
        return new EvaluationRecord
        {
            Id = reader.GetInt64(0),
            AnnouncementGuid = reader.GetString(1),
            PositionKey = reader.GetString(2),
            ProfileHash = reader.GetString(3),
            Status = reader.GetString(4),
            SummaryReason = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            DetailsJson = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            EvaluatedAt = AppTime.ParseUtc(reader.GetString(7))
        };
    }
}
