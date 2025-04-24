namespace MigrationPlatform.Abstractions.Models
{
    public class MigrationWorkItemRevision
    {
        public int workItemId;

        public int Index { get; set; }
        public List<MigrationWorkItemField> Fields { get; set; } = new List<MigrationWorkItemField>();
        public DateTimeOffset ChangedDate { get; set; }
        public List<MigrationWorkItemExternalLink> ExternalLinks { get; set; } = new List<MigrationWorkItemExternalLink>();
        public List<MigrationWorkItemRelatedLink> RelatedLinks { get; set; } = new List<MigrationWorkItemRelatedLink>();
        public List<MigrationWorkItemHyperlink> Hyperlinks { get; set; } = new List<MigrationWorkItemHyperlink>();
        public List<MigrationWorkItemAttachment> Attachments { get; set; } = new List<MigrationWorkItemAttachment>();
    }
}
