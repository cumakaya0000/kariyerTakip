using KariyerTakip.Models;
using KariyerTakip.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KariyerTakip.Tests;

public class RepositoryMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyOutbox_IsMigratedWithoutLosingHistory_AndCanQueueNotifications(bool hasKeyColumn)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kariyertakip-migration-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    CREATE TABLE NotificationOutbox (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        {(hasKeyColumn ? "DeduplicationKey TEXT," : "")}
                        AnnouncementGuid TEXT NOT NULL,
                        NotificationType TEXT NOT NULL,
                        MessagePayload TEXT NOT NULL,
                        Status TEXT NOT NULL DEFAULT 'Pending',
                        RetryCount INTEGER NOT NULL DEFAULT 0,
                        ErrorMessage TEXT,
                        CreatedAt TEXT NOT NULL,
                        SentAt TEXT
                    );
                    INSERT INTO NotificationOutbox (AnnouncementGuid, NotificationType, MessagePayload, Status, CreatedAt)
                    VALUES ('ann', 'New', 'pending history', 'Pending', '2026-10-03'),
                           ('ann', 'New', 'sent history', 'Sent', '2026-10-03'),
                           ('ann', 'New', 'failed history', 'Failed', '2026-10-03');
                ";
                await cmd.ExecuteNonQueryAsync();
                if (hasKeyColumn)
                {
                    cmd.CommandText = @"
                        UPDATE NotificationOutbox SET DeduplicationKey = 'duplicate-key';
                        CREATE INDEX IX_Outbox_Dedup ON NotificationOutbox(DeduplicationKey);
                    ";
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            var repo = new AnnouncementRepository(Options.Create(new AppConfig { DatabasePath = path }),
                NullLogger<AnnouncementRepository>.Instance);
            await repo.InitializeDatabaseAsync();
            await repo.InitializeDatabaseAsync();
            if (hasKeyColumn)
            {
                Assert.True(await repo.HasNotificationBeenSentAsync("duplicate-key"));
                Assert.False(await repo.QueueNotificationAsync(Notification("duplicate-key")));
                Assert.Empty(await repo.GetPendingNotificationsAsync());
            }
            Assert.True(await repo.QueueNotificationAsync(Notification("new-key")));
            Assert.False(await repo.QueueNotificationAsync(Notification("new-key")));
            using var verify = new SqliteConnection(connectionString);
            await verify.OpenAsync();
            using var check = verify.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM NotificationOutbox;";
            Assert.Equal(4L, await check.ExecuteScalarAsync());
            check.CommandText = "SELECT COUNT(DISTINCT DeduplicationKey) FROM NotificationOutbox;";
            Assert.Equal(4L, await check.ExecuteScalarAsync());
            check.CommandText = "SELECT COUNT(*) FROM NotificationOutbox WHERE MessagePayload IN ('pending history', 'sent history', 'failed history');";
            Assert.Equal(3L, await check.ExecuteScalarAsync());
            check.CommandText = "SELECT MessagePayload FROM NotificationOutbox WHERE Status = 'Sent';";
            Assert.Equal("sent history", await check.ExecuteScalarAsync());
        }
        finally
        {
            using var conn = new SqliteConnection($"Data Source={path}");
            SqliteConnection.ClearPool(conn);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task FreshOutbox_RejectsDuplicateNotification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kariyertakip-fresh-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new AnnouncementRepository(Options.Create(new AppConfig { DatabasePath = path }),
                NullLogger<AnnouncementRepository>.Instance);
            await repo.InitializeDatabaseAsync();
            Assert.True(await repo.QueueNotificationAsync(Notification("same-key")));
            Assert.False(await repo.QueueNotificationAsync(Notification("same-key")));
            Assert.Single(await repo.GetPendingNotificationsAsync());
        }
        finally
        {
            using var conn = new SqliteConnection($"Data Source={path}");
            SqliteConnection.ClearPool(conn);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }

    private static OutboxNotificationRecord Notification(string key) => new()
    {
        DeduplicationKey = key, AnnouncementGuid = "ann", NotificationType = "New",
        MessagePayload = "Test", CreatedAt = DateTime.UtcNow
    };
}
