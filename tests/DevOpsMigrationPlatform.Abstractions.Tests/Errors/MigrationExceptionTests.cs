// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Abstractions.Tests.Errors
{
    [TestClass]
    public class MigrationExceptionTests
    {
        [TestMethod]
        public void Constructor_WithValidCategory_SetsProperties()
        {
            var ex = new MigrationException(
                "Authentication failed",
                MigrationErrorCategory.Authentication,
                guidance: "Please verify your credentials"
            );

            Assert.AreEqual(MigrationErrorCategory.Authentication, ex.Category);
            Assert.AreEqual("Authentication failed", ex.Message);
            Assert.AreEqual("Please verify your credentials", ex.Guidance);
            Assert.AreEqual(2, ex.ExitCode);
        }

        [TestMethod]
        public void Constructor_WithoutGuidance_CreatesExceptionWithNullGuidance()
        {
            var ex = new MigrationException(
                "Validation failed",
                MigrationErrorCategory.ValidationError
            );

            Assert.AreEqual(MigrationErrorCategory.ValidationError, ex.Category);
            Assert.AreEqual("Validation failed", ex.Message);
            Assert.IsNull(ex.Guidance);
            Assert.AreEqual(4, ex.ExitCode);
        }

        [TestMethod]
        public void AllCategories_HaveExpectedExitCodes()
        {
            var expected = new Dictionary<MigrationErrorCategory, int>
            {
                { MigrationErrorCategory.Unknown,           1   },
                { MigrationErrorCategory.Authentication,    2   },
                { MigrationErrorCategory.RateLimited,       3   },
                { MigrationErrorCategory.ValidationError,   4   },
                { MigrationErrorCategory.Transient,         5   },
                { MigrationErrorCategory.ResourceCapacity,  6   },
                { MigrationErrorCategory.RemoteServerError, 7   },
                { MigrationErrorCategory.DataIntegrity,     8   },
                { MigrationErrorCategory.NotSupported,      9   },
                { MigrationErrorCategory.Canceled,          128 },
            };

            foreach (var (category, expectedCode) in expected)
            {
                var ex = new MigrationException("test", category);
                Assert.AreEqual(expectedCode, ex.ExitCode,
                    $"Category {category} should have exit code {expectedCode}");
            }
        }

        [TestMethod]
        public void IsRetryable_DefaultsToFalse()
        {
            var ex = new MigrationException("Auth failed", MigrationErrorCategory.Authentication);
            Assert.IsFalse(ex.IsRetryable);
        }

        [TestMethod]
        public void IsRetryable_WhenExplicitlySetTrue_IsTrue()
        {
            var ex = new MigrationException("Rate limit", MigrationErrorCategory.RateLimited, isRetryable: true);
            Assert.IsTrue(ex.IsRetryable);
        }

        [TestMethod]
        public void IsRetryable_WhenExplicitlySetFalse_IsFalse()
        {
            var ex = new MigrationException("Validation failed", MigrationErrorCategory.ValidationError, isRetryable: false);
            Assert.IsFalse(ex.IsRetryable);
        }

        [TestMethod]
        public void ToString_IncludesCategoryAndMessage()
        {
            var ex = new MigrationException(
                "Invalid workspace ID",
                MigrationErrorCategory.ValidationError
            );

            string result = ex.ToString();
            Assert.IsTrue(result.Contains("ValidationError"), "Should contain category");
            Assert.IsTrue(result.Contains("Invalid workspace ID"), "Should contain message");
            Assert.IsTrue(result.Contains("4"), "Should contain exit code");
        }

        [TestMethod]
        public void ToString_IncludesGuidanceWhenProvided()
        {
            var ex = new MigrationException(
                "PAT token expired",
                MigrationErrorCategory.Authentication,
                guidance: "Generate a new PAT with appropriate scopes"
            );

            string result = ex.ToString();
            Assert.IsTrue(result.Contains("Generate a new PAT"), "Should contain guidance");
        }

        [TestMethod]
        public void Constructor_WithInnerException_PreservesStackTrace()
        {
            var inner = new InvalidOperationException("Inner error");
            var ex = new MigrationException(
                "Wrapper exception",
                MigrationErrorCategory.Transient,
                innerException: inner
            );

            Assert.AreEqual(inner, ex.InnerException);
        }

        [TestMethod]
        public void MultipleInstances_WithSameCategory_HaveSameExitCode()
        {
            var ex1 = new MigrationException("First", MigrationErrorCategory.RateLimited);
            var ex2 = new MigrationException("Second", MigrationErrorCategory.RateLimited);

            Assert.AreEqual(ex1.ExitCode, ex2.ExitCode);
            Assert.AreEqual(3, ex1.ExitCode);
            Assert.AreEqual(3, ex2.ExitCode);
        }
    }
}
