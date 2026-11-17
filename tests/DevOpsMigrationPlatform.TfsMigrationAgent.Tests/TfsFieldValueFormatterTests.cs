// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Globalization;
using System.Threading;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// #169 live E2E: TFS revisions were written with culture-formatted values ("16/11/2026 16:47:37"),
/// which Azure DevOps rejects (TF401326 InvalidType) and which mean different things on different
/// machines. Package values must be culture-invariant; dates are ISO 8601 UTC.
/// </summary>
[TestClass]
public class TfsFieldValueFormatterTests
{
    private static T InCulture<T>(string culture, Func<T> act)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
        try { return act(); }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Format_UtcDate_IsIso8601RoundTrip_InAnyCulture()
    {
        var value = new DateTime(2026, 11, 16, 16, 47, 37, DateTimeKind.Utc);

        var formatted = InCulture("en-GB", () => TfsFieldValueFormatter.Format(value));

        Assert.AreEqual("2026-11-16T16:47:37.0000000Z", formatted);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Format_LocalDate_IsConvertedToUtc()
    {
        var local = new DateTime(2026, 11, 16, 16, 47, 37, DateTimeKind.Local);

        var formatted = TfsFieldValueFormatter.Format(local);

        Assert.AreEqual(local.ToUniversalTime(), DateTime.Parse(formatted!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        StringAssert.EndsWith(formatted, "Z");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Format_Double_IsCultureInvariant()
    {
        Assert.AreEqual("1.5", InCulture("de-DE", () => TfsFieldValueFormatter.Format(1.5d)));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Format_StringAndNull_AreUnchanged()
    {
        Assert.AreEqual("E2E169 story", TfsFieldValueFormatter.Format("E2E169 story"));
        Assert.IsNull(TfsFieldValueFormatter.Format(null));
    }
}
