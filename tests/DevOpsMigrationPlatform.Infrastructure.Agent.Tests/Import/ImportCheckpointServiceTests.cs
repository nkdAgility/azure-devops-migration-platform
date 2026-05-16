// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using DevOpsMigrationPlatform.Abstractions.Storage;
using System.Text;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class ImportCheckpointServiceTests
{
    [TestMethod]
    public async Task ReadCursorAsync_WhenCursorExists_ReturnsDeserializedCursor()
    {
        var expected = new CursorEntry
        {
            LastProcessed = "WorkItems/2026-01-01/638712000000000000-42-1",
            Stage = CursorStage.AppliedFields,
            UpdatedAt = new DateTimeOffset(2026, 01, 01, 10, 11, 12, TimeSpan.Zero)
        };

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Kind == PackageContentKind.Artefact &&
                    c.Address != null &&
                    c.Address.RelativePath == ".migration/Checkpoints/workitems-import.cursor.json"),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackagePayload?>(new PackagePayload(
                new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(expected))), "application/json")));

        var sut = new ImportCheckpointService(package.Object);

        var actual = await sut.ReadCursorAsync(CancellationToken.None);

        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.LastProcessed, actual.LastProcessed);
        Assert.AreEqual(expected.Stage, actual.Stage);
        Assert.AreEqual(expected.UpdatedAt, actual.UpdatedAt);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task WriteCursorAsync_WritesSerializedCursorToExpectedKey()
    {
        var cursor = new CursorEntry
        {
            LastProcessed = "WorkItems/2026-01-01/638712000000000000-42-2",
            Stage = CursorStage.Completed,
            UpdatedAt = new DateTimeOffset(2026, 01, 01, 10, 11, 13, TimeSpan.Zero)
        };

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.PersistContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Kind == PackageContentKind.Artefact &&
                    c.Address != null &&
                    c.Address.RelativePath == ".migration/Checkpoints/workitems-import.cursor.json"),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var json = reader.ReadToEnd();
                var persisted = JsonSerializer.Deserialize<CursorEntry>(json);
                Assert.IsNotNull(persisted);
                Assert.AreEqual(cursor.LastProcessed, persisted.LastProcessed);
                Assert.AreEqual(cursor.Stage, persisted.Stage);
            })
            .Returns(ValueTask.CompletedTask);

        var sut = new ImportCheckpointService(package.Object);

        await sut.WriteCursorAsync(cursor, CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task SetWorkItemMappingAsync_ThenGetWorkItemMappingAsync_RoundTripsValue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<System.Data.Common.DbConnection>(connection));

        var sut = new ImportCheckpointService(package.Object);

        await sut.SetWorkItemMappingAsync(101, 1001, CancellationToken.None);
        var targetId = await sut.GetWorkItemMappingAsync(101, CancellationToken.None);

        Assert.AreEqual(1001, targetId);
    }

    [TestMethod]
    public async Task SetAttachmentMappingAsync_ThenGetAttachmentMappingAsync_RoundTripsValue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<System.Data.Common.DbConnection>(connection));

        var sut = new ImportCheckpointService(package.Object);

        await sut.SetAttachmentMappingAsync(42, 7, "attachments/a.bin", "target-attachment-1", CancellationToken.None);
        var targetAttachment = await sut.GetAttachmentMappingAsync(42, 7, "attachments/a.bin", CancellationToken.None);

        Assert.AreEqual("target-attachment-1", targetAttachment);
    }

    [TestMethod]
    public async Task SetEmbeddedImageMappingAsync_ThenGetEmbeddedImageMappingAsync_RoundTripsValue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<System.Data.Common.DbConnection>(connection));

        var sut = new ImportCheckpointService(package.Object);

        await sut.SetEmbeddedImageMappingAsync(42, 8, "images/abc.png", "target-image-1", CancellationToken.None);
        var targetImage = await sut.GetEmbeddedImageMappingAsync(42, 8, "images/abc.png", CancellationToken.None);

        Assert.AreEqual("target-image-1", targetImage);
    }

    [TestMethod]
    public async Task DisposeAsync_WhenIdMapIsInitialized_DisposesUnderlyingConnection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<DbConnection>(connection));

        var sut = new ImportCheckpointService(package.Object);
        await sut.SetWorkItemMappingAsync(11, 22, CancellationToken.None);
        Assert.AreEqual(ConnectionState.Open, connection.State);

        await sut.DisposeAsync();

        Assert.AreEqual(ConnectionState.Closed, connection.State);
    }

    [TestMethod]
    public async Task SetWorkItemMappingAsync_WhenInitializationFails_DisposesFailedConnection_AndRetriesWithNewConnection()
    {
        var failingConnection = new InitializationFailingDbConnection();
        await using var retryConnection = new SqliteConnection("Data Source=:memory:");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .SetupSequence(p => p.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<DbConnection>(failingConnection))
            .Returns(new ValueTask<DbConnection>(retryConnection));

        var sut = new ImportCheckpointService(package.Object);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.SetWorkItemMappingAsync(101, 1001, CancellationToken.None));
        Assert.IsTrue(failingConnection.WasDisposed);

        await sut.SetWorkItemMappingAsync(101, 1001, CancellationToken.None);
        var mapped = await sut.GetWorkItemMappingAsync(101, CancellationToken.None);
        Assert.AreEqual(1001, mapped);
    }

    private sealed class InitializationFailingDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString
        {
            get => string.Empty;
            set
            {
            }
        }

        public override string Database => "init-failure-db";

        public override string DataSource => "init-failure";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
            => new InitializationFailingDbCommand();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            _state = ConnectionState.Closed;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;
            _state = ConnectionState.Closed;
            return base.DisposeAsync();
        }
    }

    private sealed class InitializationFailingDbCommand : DbCommand
    {
        private readonly SqliteParameterCollection _parameters = new SqliteCommand().Parameters;

        [AllowNull]
        public override string CommandText
        {
            get => string.Empty;
            set
            {
            }
        }

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
            => throw new InvalidOperationException("Initialization failed.");

        public override object? ExecuteScalar()
            => throw new NotSupportedException();

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => Task.FromException<int>(new InvalidOperationException("Initialization failed."));

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            => Task.FromException<object?>(new NotSupportedException());

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
            => new SqliteParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException();
    }
}
