// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Creates the <see cref="IArtefactStore"/> and <see cref="IStateStore"/> pair
/// for a given package URI.  Abstracts storage backend selection from the agent worker.
/// </summary>
internal interface IPackageStoreFactory
{
    /// <summary>
    /// Resolves the package URI and returns matched store instances.
    /// </summary>
    /// <param name="packageUri">
    /// The <c>PackageUri</c> from <see cref="JobPackage"/>, e.g.
    /// <c>file:///C:/output/my-package</c>.
    /// </param>
    (IArtefactStore ArtefactStore, IStateStore StateStore) Create(string packageUri);
}
