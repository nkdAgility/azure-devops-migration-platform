// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Abstractions.Tests.Options;

[TestClass]
public class EndpointAuthenticationOptionsTests
{
    [TestMethod]
    public void ResolvedAccessToken_HasJsonIgnoreAttribute()
    {
        var property = typeof(EndpointAuthenticationOptions).GetProperty(nameof(EndpointAuthenticationOptions.ResolvedAccessToken));

        Assert.IsNotNull(property);
        Assert.IsTrue(property.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false).Any());
    }

    [TestMethod]
    public void Serialize_DoesNotIncludeResolvedAccessToken()
    {
        var envName = "DMP_TEST_PAT";
        Environment.SetEnvironmentVariable(envName, "test-token");
        try
        {
            var options = new EndpointAuthenticationOptions
            {
                Type = AuthenticationType.AccessToken,
                AccessToken = $"$ENV:{envName}"
            };

            var json = JsonSerializer.Serialize(options);
            Assert.IsTrue(
                json.IndexOf(nameof(EndpointAuthenticationOptions.ResolvedAccessToken), StringComparison.Ordinal) < 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }
}
