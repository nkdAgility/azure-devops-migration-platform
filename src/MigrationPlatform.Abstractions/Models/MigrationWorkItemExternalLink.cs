namespace MigrationPlatform.Abstractions.Models
{
    public class MigrationWorkItemExternalLink : MigrationWorkItemLinkBase
    {
        public string LinkedArtifactUri { get; set; }

        public MigrationWorkItemExternalLink(string artifactLinkType, string comment, string linkedArtifactUri) : base(artifactLinkType, comment)
        {
            this.LinkedArtifactUri = linkedArtifactUri;
        }
    }
}