// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

public sealed record PackageLogPayload(
    Stream Content,
    string ContentType = "application/x-ndjson");

