namespace MigrationPlatform.Abstractions.Models
{
    public class MigrationWorkItemLinkBase
    {
        public string ArtifactLinkType { get; set; }
        public string Comment { get; set; }

        public MigrationWorkItemLinkBase(string artifactLinkType, string comment)
        {
            ArtifactLinkType = artifactLinkType;
            Comment = comment;
        }

    }
}
