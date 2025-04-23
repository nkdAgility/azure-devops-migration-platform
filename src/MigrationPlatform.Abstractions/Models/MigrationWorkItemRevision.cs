namespace MigrationPlatform.Abstractions.Models
{
    public class MigrationWorkItemRevision
    {
        public int id;

        public int Index { get; set; }
        public List<MigrationWorkItemField> Fields { get; set; } = new List<MigrationWorkItemField>();
        public DateTimeOffset ChangedDate { get; set; }
    }
}
