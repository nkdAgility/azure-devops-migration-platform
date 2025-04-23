namespace MigrationPlatform.CLI.Options
{
    public class MigrationPlatformOptions
    {
        public string Storage { get; set; } = string.Empty;

        public string ExpandedStorage
        {
            get
            {
                var expanded = Environment.ExpandEnvironmentVariables(Storage);

                if (string.IsNullOrWhiteSpace(expanded))
                    throw new InvalidOperationException("Storage path is not set or is empty.");

                if (!Path.IsPathRooted(expanded))
                    expanded = Path.GetFullPath(expanded); // Make relative paths absolute

                Directory.CreateDirectory(expanded); // Ensure the folder exists

                return expanded;
            }
        }
    }
}
