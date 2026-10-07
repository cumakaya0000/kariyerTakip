namespace KariyerTakip.Models;

public sealed class SourceSnapshotResult
{
    public bool IsSuspicious { get; set; }
    public int PreviousCount { get; set; }
    public string WarningEpisode { get; set; } = "";
    public List<AnnouncementRecord> RemovedAnnouncements { get; set; } = new();
}
