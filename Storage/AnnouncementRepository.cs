using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;

namespace KariyerTakip.Storage;

public class AnnouncementRepository : IAnnouncementRepository
{
    private readonly string _connectionString;
    private readonly ILogger<AnnouncementRepository> _logger;

    public AnnouncementRepository(IOptions<AppConfig> config, ILogger<AnnouncementRepository> logger)
    {
        _logger = logger;
        var dbPath = config.Value.DatabasePath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            dbPath = "kariyertakip.db";
        }
        _connectionString = $"Data Source={dbPath}";
    }

    private SqliteConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task InitializeDatabaseAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        var ddl = @"
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
                RawContentHash TEXT,
                FirstSeenAt TEXT NOT NULL,
                LastCheckedAt TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Positions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AnnouncementGuid TEXT NOT NULL,
                Title TEXT NOT NULL,
                Unvan TEXT,
                Cities TEXT,
                Quota INTEGER NOT NULL DEFAULT 0,
                RawText TEXT,
                FOREIGN KEY (AnnouncementGuid) REFERENCES Announcements (Guid) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Evaluations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AnnouncementGuid TEXT NOT NULL,
                PositionId INTEGER,
                ProfileHash TEXT NOT NULL,
                Status TEXT NOT NULL,
                SummaryReason TEXT,
                DetailsJson TEXT,
                EvaluatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS NotificationOutbox (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
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
                Success INTEGER NOT NULL,
                TotalAnnouncementsFound INTEGER NOT NULL DEFAULT 0,
                EligibleCount INTEGER NOT NULL DEFAULT 0,
                NeedsReviewCount INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT
            );

            CREATE INDEX IF NOT EXISTS IX_Outbox_Status ON NotificationOutbox(Status);
            CREATE INDEX IF NOT EXISTS IX_Positions_Guid ON Positions(AnnouncementGuid);
            CREATE INDEX IF NOT EXISTS IX_Evaluations_Guid ON Evaluations(AnnouncementGuid);
        ";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("SQLite veritabanı başarıyla doğrulandı / oluşturuldu.");
    }

    public async Task<List<AnnouncementRecord>> GetAllAnnouncementsAsync()
    {
        var list = new List<AnnouncementRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Guid, InstitutionName, UnitName, Title, AnnouncementType, DetailUrl, ApplicationUrl,
                                   StartDate, EndDate, RawContentHash, FirstSeenAt, LastCheckedAt, IsActive
                            FROM Announcements ORDER BY EndDate ASC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new AnnouncementRecord
            {
                Guid = reader.GetString(0),
                InstitutionName = reader.GetString(1),
                UnitName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Title = reader.GetString(3),
                AnnouncementType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                DetailUrl = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                ApplicationUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                StartDate = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                EndDate = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                RawContentHash = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                FirstSeenAt = DateTime.Parse(reader.GetString(10)),
                LastCheckedAt = DateTime.Parse(reader.GetString(11)),
                IsActive = reader.GetInt32(12) == 1
            });
        }
        return list;
    }

    public async Task<AnnouncementRecord?> GetAnnouncementByGuidAsync(string guid)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Guid, InstitutionName, UnitName, Title, AnnouncementType, DetailUrl, ApplicationUrl,
                                   StartDate, EndDate, RawContentHash, FirstSeenAt, LastCheckedAt, IsActive
                            FROM Announcements WHERE Guid = @Guid";
        cmd.Parameters.AddWithValue("@Guid", guid);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
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
                StartDate = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                EndDate = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                RawContentHash = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                FirstSeenAt = DateTime.Parse(reader.GetString(10)),
                LastCheckedAt = DateTime.Parse(reader.GetString(11)),
                IsActive = reader.GetInt32(12) == 1
            };
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
                StartDate, EndDate, RawContentHash, FirstSeenAt, LastCheckedAt, IsActive
            ) VALUES (
                @Guid, @InstitutionName, @UnitName, @Title, @AnnouncementType, @DetailUrl, @ApplicationUrl,
                @StartDate, @EndDate, @RawContentHash, @FirstSeenAt, @LastCheckedAt, @IsActive
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
                RawContentHash = excluded.RawContentHash,
                LastCheckedAt = excluded.LastCheckedAt,
                IsActive = excluded.IsActive;
        ";

        cmd.Parameters.AddWithValue("@Guid", record.Guid);
        cmd.Parameters.AddWithValue("@InstitutionName", record.InstitutionName);
        cmd.Parameters.AddWithValue("@UnitName", (object?)record.UnitName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Title", record.Title);
        cmd.Parameters.AddWithValue("@AnnouncementType", (object?)record.AnnouncementType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DetailUrl", (object?)record.DetailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ApplicationUrl", (object?)record.ApplicationUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StartDate", record.StartDate.HasValue ? (object)record.StartDate.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@EndDate", record.EndDate.HasValue ? (object)record.EndDate.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@RawContentHash", (object?)record.RawContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FirstSeenAt", record.FirstSeenAt.ToString("o"));
        cmd.Parameters.AddWithValue("@LastCheckedAt", record.LastCheckedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@IsActive", record.IsActive ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SavePositionsAsync(string announcementGuid, List<PositionRecord> positions)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        using var deleteCmd = conn.CreateCommand();
        deleteCmd.Transaction = tx;
        deleteCmd.CommandText = "DELETE FROM Positions WHERE AnnouncementGuid = @Guid";
        deleteCmd.Parameters.AddWithValue("@Guid", announcementGuid);
        await deleteCmd.ExecuteNonQueryAsync();

        foreach (var pos in positions)
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = @"
                INSERT INTO Positions (AnnouncementGuid, Title, Unvan, Cities, Quota, RawText)
                VALUES (@AnnouncementGuid, @Title, @Unvan, @Cities, @Quota, @RawText);";
            insertCmd.Parameters.AddWithValue("@AnnouncementGuid", announcementGuid);
            insertCmd.Parameters.AddWithValue("@Title", pos.Title);
            insertCmd.Parameters.AddWithValue("@Unvan", (object?)pos.Unvan ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@Cities", (object?)pos.Cities ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@Quota", pos.Quota);
            insertCmd.Parameters.AddWithValue("@RawText", (object?)pos.RawText ?? DBNull.Value);
            await insertCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<List<PositionRecord>> GetPositionsByAnnouncementGuidAsync(string announcementGuid)
    {
        var list = new List<PositionRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, AnnouncementGuid, Title, Unvan, Cities, Quota, RawText
                            FROM Positions WHERE AnnouncementGuid = @Guid";
        cmd.Parameters.AddWithValue("@Guid", announcementGuid);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PositionRecord
            {
                Id = reader.GetInt64(0),
                AnnouncementGuid = reader.GetString(1),
                Title = reader.GetString(2),
                Unvan = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Cities = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Quota = reader.GetInt32(5),
                RawText = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
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
            INSERT INTO Evaluations (AnnouncementGuid, PositionId, ProfileHash, Status, SummaryReason, DetailsJson, EvaluatedAt)
            VALUES (@AnnouncementGuid, @PositionId, @ProfileHash, @Status, @SummaryReason, @DetailsJson, @EvaluatedAt);";

        cmd.Parameters.AddWithValue("@AnnouncementGuid", evaluation.AnnouncementGuid);
        cmd.Parameters.AddWithValue("@PositionId", evaluation.PositionId.HasValue ? (object)evaluation.PositionId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ProfileHash", evaluation.ProfileHash);
        cmd.Parameters.AddWithValue("@Status", evaluation.Status);
        cmd.Parameters.AddWithValue("@SummaryReason", (object?)evaluation.SummaryReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DetailsJson", (object?)evaluation.DetailsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@EvaluatedAt", evaluation.EvaluatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<EvaluationRecord>> GetEvaluationsByAnnouncementGuidAsync(string announcementGuid)
    {
        var list = new List<EvaluationRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, AnnouncementGuid, PositionId, ProfileHash, Status, SummaryReason, DetailsJson, EvaluatedAt
                            FROM Evaluations WHERE AnnouncementGuid = @Guid ORDER BY Id DESC";
        cmd.Parameters.AddWithValue("@Guid", announcementGuid);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new EvaluationRecord
            {
                Id = reader.GetInt64(0),
                AnnouncementGuid = reader.GetString(1),
                PositionId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                ProfileHash = reader.GetString(3),
                Status = reader.GetString(4),
                SummaryReason = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                DetailsJson = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                EvaluatedAt = DateTime.Parse(reader.GetString(7))
            });
        }
        return list;
    }

    public async Task QueueNotificationAsync(OutboxNotificationRecord notification)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO NotificationOutbox (AnnouncementGuid, NotificationType, MessagePayload, Status, RetryCount, CreatedAt)
            VALUES (@AnnouncementGuid, @NotificationType, @MessagePayload, 'Pending', 0, @CreatedAt);";

        cmd.Parameters.AddWithValue("@AnnouncementGuid", notification.AnnouncementGuid);
        cmd.Parameters.AddWithValue("@NotificationType", notification.NotificationType);
        cmd.Parameters.AddWithValue("@MessagePayload", notification.MessagePayload);
        cmd.Parameters.AddWithValue("@CreatedAt", notification.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<OutboxNotificationRecord>> GetPendingNotificationsAsync(int limit = 20)
    {
        var list = new List<OutboxNotificationRecord>();
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, AnnouncementGuid, NotificationType, MessagePayload, Status, RetryCount, ErrorMessage, CreatedAt, SentAt
                            FROM NotificationOutbox
                            WHERE Status = 'Pending' OR (Status = 'Failed' AND RetryCount < 5)
                            ORDER BY Id ASC LIMIT @Limit";
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new OutboxNotificationRecord
            {
                Id = reader.GetInt64(0),
                AnnouncementGuid = reader.GetString(1),
                NotificationType = reader.GetString(2),
                MessagePayload = reader.GetString(3),
                Status = reader.GetString(4),
                RetryCount = reader.GetInt32(5),
                ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = DateTime.Parse(reader.GetString(7)),
                SentAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8))
            });
        }
        return list;
    }

    public async Task MarkNotificationSentAsync(long id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE NotificationOutbox SET Status = 'Sent', SentAt = @SentAt WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@SentAt", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkNotificationFailedAsync(long id, string errorMessage)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE NotificationOutbox SET Status = 'Failed', RetryCount = RetryCount + 1, ErrorMessage = @Error WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Error", errorMessage);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> HasNotificationBeenSentAsync(string announcementGuid, string notificationType)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(1) FROM NotificationOutbox 
                            WHERE AnnouncementGuid = @Guid AND NotificationType = @Type AND Status = 'Sent'";
        cmd.Parameters.AddWithValue("@Guid", announcementGuid);
        cmd.Parameters.AddWithValue("@Type", notificationType);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    public async Task<long> RecordScanStartAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO ScanRuns (StartedAt, Success, TotalAnnouncementsFound, EligibleCount, NeedsReviewCount)
                            VALUES (@StartedAt, 0, 0, 0, 0);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@StartedAt", DateTime.UtcNow.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task RecordScanEndAsync(long scanId, bool success, int totalFound, int eligibleCount, int needsReviewCount, string? errorMessage)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE ScanRuns SET
                                FinishedAt = @FinishedAt,
                                Success = @Success,
                                TotalAnnouncementsFound = @Total,
                                EligibleCount = @Eligible,
                                NeedsReviewCount = @NeedsReview,
                                ErrorMessage = @Error
                            WHERE Id = @Id";

        cmd.Parameters.AddWithValue("@Id", scanId);
        cmd.Parameters.AddWithValue("@FinishedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@Success", success ? 1 : 0);
        cmd.Parameters.AddWithValue("@Total", totalFound);
        cmd.Parameters.AddWithValue("@Eligible", eligibleCount);
        cmd.Parameters.AddWithValue("@NeedsReview", needsReviewCount);
        cmd.Parameters.AddWithValue("@Error", (object?)errorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }
}
