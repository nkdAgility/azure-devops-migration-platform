namespace MigrationPlatform.Abstractions.Models
{
    public class MigrationWorkItemHyperlink : MigrationWorkItemLinkBase
    {
        public string Location { get; set; }

        public MigrationWorkItemHyperlink(string artifactLinkType, string comment, string location) : base(artifactLinkType, comment)
        {
            this.Location = location;
        }

    }
}