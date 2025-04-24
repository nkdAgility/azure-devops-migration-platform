namespace MigrationPlatform.Abstractions.Models
{
    public class MigrationWorkItemAttachment
    {
        public string FileName { get; set; }
        public string Comment { get; set; }

        public MigrationWorkItemAttachment(string fileName, string comment)
        {
            FileName = fileName;
            Comment = comment;
        }

    }
}
