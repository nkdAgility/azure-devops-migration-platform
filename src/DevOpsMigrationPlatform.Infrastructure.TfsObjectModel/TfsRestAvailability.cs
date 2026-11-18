// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// The TFS agent is built as if there may be no REST API (Martin, 2026-11-18): TFS 2008 has none,
/// and older servers lack newer resources. This recognises a failure that means "the server does
/// not serve this REST resource", as opposed to a temporary or permission failure.
/// </summary>
internal static class TfsRestAvailability
{
    /// <summary>
    /// True when the server positively lacks the resource: no such resource or API version range
    /// (the SDK negotiates down to the server's highest version, so these mean it is not served),
    /// or a plain 404 from a server with no REST API at all.
    /// </summary>
    public static bool IsMissing(Exception ex) =>
        ex is Microsoft.VisualStudio.Services.WebApi.VssResourceNotFoundException
           or Microsoft.VisualStudio.Services.WebApi.VssVersionOutOfRangeException
        || ex is Microsoft.VisualStudio.Services.WebApi.VssServiceResponseException { HttpStatusCode: System.Net.HttpStatusCode.NotFound }
        // On net481 HttpRequestException carries no status code, so the standard message is matched too.
        || ex is System.Net.Http.HttpRequestException http
           && (http.Data["StatusCode"] is System.Net.HttpStatusCode.NotFound
               || http.Message.IndexOf("404 (Not Found)", StringComparison.OrdinalIgnoreCase) >= 0);
}
