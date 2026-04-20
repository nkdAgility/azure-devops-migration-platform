using System.Diagnostics;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Static factory for building <see cref="TagList"/> values with mandatory dimension tags.
/// Every OTel instrument measurement MUST carry <c>job.id</c>, <c>operation</c>, and <c>module</c>.
/// </summary>
public static class MigrationTagList
{
    /// <summary>
    /// Creates a <see cref="TagList"/> with the three mandatory dimension tags.
    /// </summary>
    public static TagList Create(string jobId, string operation, string module)
    {
        var tags = new TagList
        {
            { "job.id", jobId },
            { "operation", operation },
            { "module", module }
        };
        return tags;
    }

    /// <summary>
    /// Creates a <see cref="TagList"/> with mandatory tags plus the optional <c>source.type</c> tag.
    /// </summary>
    public static TagList Create(string jobId, string operation, string module, string sourceType)
    {
        var tags = Create(jobId, operation, module);
        tags.Add("source.type", sourceType);
        return tags;
    }
}
