// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Export;

/// <summary>
/// #169 / #154: one embedded-image downloader for the Azure DevOps and TFS connectors. It fetches
/// only images hosted on the source endpoint, with the source endpoint's credentials, and only
/// accepts image responses (a sign-in page is not an image).
/// </summary>
[TestClass]
public class SourceEndpointEmbeddedImageDownloaderTests
{
    private const string SourceUrl = "https://dev.azure.com/source-org";

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static ISourceEndpointInfo Source(string? token) => Mock.Of<ISourceEndpointInfo>(s =>
        s.Url == SourceUrl &&
        s.ToOrganisationEndpoint() == new OrganisationEndpoint
        {
            ResolvedUrl = SourceUrl,
            Authentication = new OrganisationEndpointAuthentication { Type = AuthenticationType.AccessToken, ResolvedAccessToken = token }
        });

    private static HttpResponseMessage Image(string mediaType = "image/png") => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) }
        }
    };

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_SourceHostedImage_DownloadsWithTheSourceToken()
    {
        var handler = new RecordingHandler(_ => Image());
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        var result = await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc?fileName=e2e.png", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("png", result.Extension);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, result.Bytes);
        var auth = handler.Requests[0].Headers.Authorization!;
        Assert.AreEqual("Basic", auth.Scheme);
        Assert.AreEqual(":tok", Encoding.ASCII.GetString(Convert.FromBase64String(auth.Parameter!)));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_ExternalImage_IsNotFetched()
    {
        var handler = new RecordingHandler(_ => Image());
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        var result = await sut.TryDownloadAsync("https://example.com/logo.png", CancellationToken.None);

        Assert.IsNull(result);
        Assert.AreEqual(0, handler.Requests.Count, "Images not on the source endpoint keep their original link.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_NonImageResponse_IsRejected()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Sign in</html>", Encoding.UTF8, "text/html")
        });
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        Assert.IsNull(await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
    }

    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Found)
    {
        Headers = { Location = new Uri(location) }
    };

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_RedirectOffTheSource_IsNotFollowed()
    {
        // PR #222 review: a redirect must not carry the request (and its credentials) to another host.
        var handler = new RecordingHandler(_ => Redirect("https://evil.example.com/x.png"));
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        Assert.IsNull(await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_RedirectWithinTheSource_IsFollowed()
    {
        var handler = new RecordingHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/abc") ? Redirect($"{SourceUrl}/guid/_apis/wit/attachments/def") : Image());
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        Assert.IsNotNull(await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsNotNull(handler.Requests[1].Headers.Authorization);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_OctetStreamWithImageBytes_IsAccepted()
    {
        // PR #222 review: the attachments API may answer application/octet-stream for an image.
        var handler = new RecordingHandler(_ => Image("application/octet-stream"));
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        var result = await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("png", result.Extension);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_OctetStreamWithoutImageBytes_IsRejected()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("<html>Sign in</html>"))
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream") }
            }
        });
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        Assert.IsNull(await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_FailedResponse_ReturnsNull()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        Assert.IsNull(await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
    }
}
