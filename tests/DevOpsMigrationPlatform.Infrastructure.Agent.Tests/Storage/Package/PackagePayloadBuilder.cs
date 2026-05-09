// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

internal static class PackagePayloadBuilder
{
    public static byte[] Utf8(string content) => Encoding.UTF8.GetBytes(content);
}

