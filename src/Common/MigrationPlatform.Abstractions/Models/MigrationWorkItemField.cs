namespace MigrationPlatform.Abstractions
{
    public class MigrationWorkItemField
    {
        public MigrationWorkItemField(string name, string referenceName, object value)
        {
            Name = name;
            ReferenceName = referenceName;
            Value = value;
        }

        public string Name { get; set; }
        public string ReferenceName { get; set; }
        public object Value { get; set; }
    }
}