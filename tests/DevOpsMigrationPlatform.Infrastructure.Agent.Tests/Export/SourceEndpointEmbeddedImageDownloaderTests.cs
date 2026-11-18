// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
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

    // A complete 1x1 PNG: signature, IHDR, IDAT and IEND chunks.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    // The 8-byte PNG signature alone: a response cut off before any image data.
    private static readonly byte[] PngSignatureOnly = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static HttpResponseMessage Image(string mediaType = "image/png", byte[]? bytes = null) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes ?? PngBytes)
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
        CollectionAssert.AreEqual(PngBytes, result.Bytes);
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
    public async Task TryDownloadAsync_OctetStreamWithTruncatedPngSignature_IsRejected()
    {
        var handler = new RecordingHandler(_ => Image("application/octet-stream", [0x89, 0x50, 0x4E, 0x47]));
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        Assert.IsNull(await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    [DataRow("application/octet-stream")]
    [DataRow("image/png")]
    public async Task TryDownloadAsync_PngTruncatedAfterItsSignature_IsRejected(string mediaType)
    {
        // PR #222 review: a response cut off after the signature would become a broken image.
        var handler = new RecordingHandler(_ => Image(mediaType, PngSignatureOnly));
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler);

        Assert.IsNull(await sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
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

    private static SourceEndpointEmbeddedImageDownloader WithoutDelay(HttpMessageHandler handler) =>
        new(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance, () => handler, retryDelay: _ => TimeSpan.Zero);

    /// <summary>A body that delivers nothing until cancelled, or fails once with an I/O error.</summary>
    private sealed class BodyStream(bool stall) : System.IO.Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!stall)
                throw new System.IO.IOException("The response ended prematurely.");
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private static HttpResponseMessage Body(bool stall)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BodyStream(stall)) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return response;
    }

    // PR #222 review: with ResponseHeadersRead the client timeout ends at the headers, so a body
    // that stops sending held the export open; each attempt now has a deadline covering the body.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_BodyStopsSending_TimesOutEachAttemptThenFailsTheRevision()
    {
        var handler = new RecordingHandler(_ => Body(stall: true));
        var sut = new SourceEndpointEmbeddedImageDownloader(Source("tok"), NullLogger<SourceEndpointEmbeddedImageDownloader>.Instance,
            () => handler, retryDelay: _ => TimeSpan.Zero, attemptTimeout: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsExactlyAsync<EmbeddedImageDownloadException>(
            () => sut.TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
        Assert.AreEqual(3, handler.Requests.Count);
    }

    // PR #222 review: on .NET 10 a connection that closes mid-body throws an IOException
    // (HttpIOException), which the retry filter did not cover.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_ConnectionClosesDuringTheBody_IsRetried()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls == 1 ? Body(stall: false) : Image());

        Assert.IsNotNull(await WithoutDelay(handler).TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_TemporaryFailureThenSuccess_IsRetried()
    {
        // Martin, 2026-11-17: images that fail temporarily must be retried.
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Image());

        var result = await WithoutDelay(handler).TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_TemporaryFailureThatPersists_FailsSoTheRevisionIsRetried()
    {
        // Returning null would let export record the revision as done without its image; failing
        // keeps the revision pending, so the next run downloads it.
        var handler = new RecordingHandler(_ => new HttpResponseMessage((HttpStatusCode)429));

        await Assert.ThrowsExactlyAsync<DevOpsMigrationPlatform.Abstractions.Agent.Attachments.EmbeddedImageDownloadException>(
            () => WithoutDelay(handler).TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
        Assert.AreEqual(3, handler.Requests.Count);
    }

    private static async Task<EmbeddedImageDownloadResultOrNull> Download(byte[] bytes, string mediaType = "application/octet-stream")
    {
        var handler = new RecordingHandler(_ => Image(mediaType, bytes));
        return new(await WithoutDelay(handler).TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
    }

    private sealed record EmbeddedImageDownloadResultOrNull(DevOpsMigrationPlatform.Abstractions.Agent.Attachments.EmbeddedImageDownloadResult? Value);

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_PngWithoutImageData_IsRejected()
    {
        // PR #222 review: signature, IHDR and IEND but no IDAT chunk is not an image.
        var noIdat = PngBytes.Take(33).Concat(PngBytes.Skip(PngBytes.Length - 12)).ToArray();
        Assert.IsNull((await Download(noIdat, "image/png")).Value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_JpegWithPaddingAfterItsEndMarker_IsAccepted()
    {
        // A decodable 1x1 JPEG (encoded and decoded by GDI+), then padding (PR #222 review).
        var jpeg = Convert.FromBase64String(DecodableJpeg).Concat(new byte[] { 0x00, 0x00, 0x00 }).ToArray();
        Assert.AreEqual("jpg", (await Download(jpeg)).Value?.Extension);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_BmpWithZeroSizeField_IsAccepted()
    {
        Assert.AreEqual("bmp", (await Download(Bmp1x1(withPixels: true))).Value?.Extension);
    }

    // PR #222 review: a zero size field is allowed, but the header's pixel extent must still be
    // present; a bitmap cut off before its pixels is a broken image.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_BmpWithZeroSizeFieldButNoPixels_IsRejected()
    {
        Assert.IsNull((await Download(Bmp1x1(withPixels: false))).Value);
    }

    /// <summary>A 1x1 24-bit bitmap with its size field 0: 54-byte header, one 4-byte pixel row.</summary>
    private static byte[] Bmp1x1(bool withPixels)
    {
        var b = new byte[withPixels ? 58 : 54];
        b[0] = (byte)'B'; b[1] = (byte)'M';
        BitConverter.GetBytes(54).CopyTo(b, 10);      // pixel data offset
        BitConverter.GetBytes(40).CopyTo(b, 14);      // BITMAPINFOHEADER
        BitConverter.GetBytes(1).CopyTo(b, 18);       // width
        BitConverter.GetBytes(1).CopyTo(b, 22);       // height
        BitConverter.GetBytes((short)1).CopyTo(b, 26);  // planes
        BitConverter.GetBytes((short)24).CopyTo(b, 28); // bits per pixel
        if (withPixels) { b[54] = 0x00; b[55] = 0x00; b[56] = 0xFF; }
        return b;
    }

    private const string DecodableJpeg = "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD50ooor8MP9Uz/2Q==";

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_ServerError500Persists_IsRetriedThenFailsTheRevision()
    {
        // Martin, 2026-11-17: a 500 is retried; if it persists the work item stops and is flagged,
        // rather than the image silently keeping its source link.
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsExactlyAsync<EmbeddedImageDownloadException>(
            () => WithoutDelay(handler).TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
        StringAssert.Contains(ex.Message, "HTTP 500");
        Assert.AreEqual(3, handler.Requests.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_ServerError500ThenSuccess_DownloadsTheImage()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Image());

        Assert.IsNotNull(await WithoutDelay(handler).TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TryDownloadAsync_ResponseLargerThanTheCap_IsRefusedUnread()
    {
        // PR #222 review: stream, never materialise. A huge response is refused from its headers.
        // The body fails the test if anything reads it (PR #222 review).
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnreadableStream()) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        response.Content.Headers.ContentLength = SourceEndpointEmbeddedImageDownloader.MaxImageBytes + 1;
        var handler = new RecordingHandler(_ => response);

        Assert.IsNull(await WithoutDelay(handler).TryDownloadAsync($"{SourceUrl}/guid/_apis/wit/attachments/abc", CancellationToken.None));
    }

    private sealed class UnreadableStream : System.IO.Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new AssertFailedException("The oversized body was read.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new AssertFailedException("The oversized body was read.");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new AssertFailedException("The oversized body was read.");
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
