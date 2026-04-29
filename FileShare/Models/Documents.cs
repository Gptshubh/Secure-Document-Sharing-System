namespace FileShare.Models
{

    public class Document
    {
        public Guid Id { get; set; }

        public string FileName { get; set; }

        public string FilePath { get; set; }

        public Guid UploadedBy { get; set; }

        public User User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; } = false;
    }
}
