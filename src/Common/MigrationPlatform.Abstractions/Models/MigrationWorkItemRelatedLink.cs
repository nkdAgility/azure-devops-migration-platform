namespace MigrationPlatform.Abstractions.Models
{
    public class MigrationWorkItemRelatedLink : MigrationWorkItemLinkBase
    {
        public string LinkTypeEnd { get; set; }
        public int RelatedWorkItemId { get; set; }

        public MigrationWorkItemRelatedLink(string artifactLinkType, string comment, string linkTypeEnd, int relatedWorkItemId) : base(artifactLinkType, comment)
        {
            this.LinkTypeEnd = linkTypeEnd;
            this.RelatedWorkItemId = relatedWorkItemId;
        }
    }
}