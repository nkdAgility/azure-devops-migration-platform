namespace MigrationPlatform.Abstractions.Models
{
    public class AttachmentDownloadResult
    {
        public bool Success { get; }
        public string? FilePath { get; }
        public Exception? Error { get; }

        private AttachmentDownloadResult(bool success, string? filePath, Exception? error)
        {
            Success = success;
            FilePath = filePath;
            Error = error;
        }

        public static AttachmentDownloadResult Succeeded(string filePath) =>
            new AttachmentDownloadResult(true, filePath, null);

        public static AttachmentDownloadResult Failed(Exception error) =>
            new AttachmentDownloadResult(false, null, error);
    }

}
