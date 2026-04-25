using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Creates a concrete <see cref="IFieldTransform"/> from a rule options bag.
/// Each registered factory handles exactly one <c>Type</c> discriminator.
/// </summary>
public interface IFieldTransformFactory
{
    /// <summary>
    /// Constructs the transform described by <paramref name="options"/> as the
    /// <paramref name="ordinal"/>-th rule inside <paramref name="groupName"/>.
    /// </summary>
    IFieldTransform Create(FieldTransformRuleOptions options, string groupName, int ordinal);
}
