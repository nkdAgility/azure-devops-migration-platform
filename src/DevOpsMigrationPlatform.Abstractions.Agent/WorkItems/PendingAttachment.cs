// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// An attachment to add when a work item is created from a revision (#169). The binary is opened
/// on demand from the package, so the connector decides how and when to upload it.
/// </summary>
/// <param name="FileName">The attachment's original file name.</param>
/// <param name="OpenAsync">Opens the attachment binary, or returns null when it is missing.</param>
public sealed record PendingAttachment(string FileName, Func<CancellationToken, Task<Stream?>> OpenAsync);
