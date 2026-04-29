namespace FileShare.Models
{
    public class DownloadLog
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public Guid DownloadedBy { get; set; }
        public DateTime Timestamp { get; set; }
        public string IPAddress { get; set; }
    }
}
