// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Commands decorated with this attribute are hidden from <c>--help</c> output
/// when the current release channel is at or above <see cref="Channel"/>.
///
/// Lifecycle:
/// <list type="bullet">
///   <item>
///     Not yet implemented — <c>[HideFromChannel(ReleaseChannel.Preview)]</c><br/>
///     Visible in Local and Canary. Hidden in Preview and Release.
///   </item>
///   <item>
///     In progress / preview-only — <c>[HideFromChannel(ReleaseChannel.Release)]</c><br/>
///     Visible in Local, Canary, and Preview. Hidden in Release only.
///   </item>
///   <item>
///     Fully implemented — remove the attribute entirely.<br/>
///     Visible in every channel.
///   </item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class HideFromChannelAttribute : Attribute
{
    /// <summary>The first channel in which this command is hidden.</summary>
    public ReleaseChannel Channel { get; }

    public HideFromChannelAttribute(ReleaseChannel channel) => Channel = channel;
}
