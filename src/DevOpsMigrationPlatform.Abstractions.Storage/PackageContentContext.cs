// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

public sealed record PackageContentContext(
    PackageContentKind Kind,
    string? Organisation = null,
    string? Project = null,
    string? Module = null,
    IPackageContentAddress? Address = null,
    bool IsCollectionRequest = false)
{
    public PackageContentContext(
        PackageContentKind Kind,
        IReadOnlyList<string> RouteSegments,
        bool IsCollectionRequest = false)
        : this(
            Kind,
            Address: new RouteSegmentPackageAddress(RouteSegments),
            IsCollectionRequest: IsCollectionRequest)
    {
    }

    private sealed class RouteSegmentPackageAddress : IPackageContentAddress
    {
        public RouteSegmentPackageAddress(IReadOnlyList<string> routeSegments)
        {
            RelativePath = string.Join("/", routeSegments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        public string RelativePath { get; }
    }
}
