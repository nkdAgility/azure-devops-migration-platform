namespace MigrationPlatform.Abstractions
{
    public class MigrationWorkItemField
    {
        public MigrationWorkItemField()
        {
        }

        public string Name { get; set; }
        public string ReferenceName { get; set; }
        public object Value { get; set; }
    }
}