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
                MigrationErrorCategory.Authentication,
                "Authentication failed",
                "Please verify your credentials"
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
                MigrationErrorCategory.ValidationError,
                "Validation failed"
            );

            Assert.AreEqual(MigrationErrorCategory.ValidationError, ex.Category);
            Assert.AreEqual("Validation failed", ex.Message);
            Assert.IsNull(ex.Guidance);
            Assert.AreEqual(4, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_UnknownCategory_Returns1()
        {
            var ex = new MigrationException(MigrationErrorCategory.Unknown, "An error occurred");
            Assert.AreEqual(1, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_AuthenticationCategory_Returns2()
        {
            var ex = new MigrationException(MigrationErrorCategory.Authentication, "Auth failed");
            Assert.AreEqual(2, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_RateLimitedCategory_Returns3()
        {
            var ex = new MigrationException(MigrationErrorCategory.RateLimited, "Rate limit exceeded");
            Assert.AreEqual(3, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_ValidationErrorCategory_Returns4()
        {
            var ex = new MigrationException(MigrationErrorCategory.ValidationError, "Bad data");
            Assert.AreEqual(4, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_TransientCategory_Returns5()
        {
            var ex = new MigrationException(MigrationErrorCategory.Transient, "Network error");
            Assert.AreEqual(5, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_ResourceCapacityCategory_Returns6()
        {
            var ex = new MigrationException(MigrationErrorCategory.ResourceCapacity, "Out of space");
            Assert.AreEqual(6, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_RemoteServerErrorCategory_Returns7()
        {
            var ex = new MigrationException(MigrationErrorCategory.RemoteServerError, "Server error");
            Assert.AreEqual(7, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_DataIntegrityCategory_Returns8()
        {
            var ex = new MigrationException(MigrationErrorCategory.DataIntegrity, "Data corrupt");
            Assert.AreEqual(8, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_NotSupportedCategory_Returns9()
        {
            var ex = new MigrationException(MigrationErrorCategory.NotSupported, "Feature not available");
            Assert.AreEqual(9, ex.ExitCode);
        }

        [TestMethod]
        public void ExitCode_CanceledCategory_Returns128()
        {
            var ex = new MigrationException(MigrationErrorCategory.Canceled, "Operation canceled");
            Assert.AreEqual(128, ex.ExitCode);
        }

        [TestMethod]
        public void IsRetryable_AuthenticationFalse()
        {
            var ex = new MigrationException(MigrationErrorCategory.Authentication, "Auth failed");
            Assert.IsFalse(ex.IsRetryable);
        }

        [TestMethod]
        public void IsRetryable_RateLimitedTrue()
        {
            var ex = new MigrationException(MigrationErrorCategory.RateLimited, "Rate limit");
            Assert.IsTrue(ex.IsRetryable);
        }

        [TestMethod]
        public void IsRetryable_TransientTrue()
        {
            var ex = new MigrationException(MigrationErrorCategory.Transient, "Network error");
            Assert.IsTrue(ex.IsRetryable);
        }

        [TestMethod]
        public void IsRetryable_ValidationErrorFalse()
        {
            var ex = new MigrationException(MigrationErrorCategory.ValidationError, "Bad data");
            Assert.IsFalse(ex.IsRetryable);
        }

        [TestMethod]
        public void IsRetryable_NotSupportedFalse()
        {
            var ex = new MigrationException(MigrationErrorCategory.NotSupported, "Not supported");
            Assert.IsFalse(ex.IsRetryable);
        }

        [TestMethod]
        public void ToString_IncludesCategoryAndMessage()
        {
            var ex = new MigrationException(
                MigrationErrorCategory.ValidationError,
                "Invalid workspace ID"
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
                MigrationErrorCategory.Authentication,
                "PAT token expired",
                "Generate a new PAT with appropriate scopes"
            );

            string result = ex.ToString();
            Assert.IsTrue(result.Contains("Generate a new PAT"), "Should contain guidance");
        }

        [TestMethod]
        public void Constructor_WithInnerException_PreservesStackTrace()
        {
            var inner = new InvalidOperationException("Inner error");
            var ex = new MigrationException(
                MigrationErrorCategory.Transient,
                "Wrapper exception",
                innerException: inner
            );

            Assert.AreEqual(inner, ex.InnerException);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullMessage_ThrowsArgumentNullException()
        {
            _ = new MigrationException(MigrationErrorCategory.Unknown, null!);
        }

        [TestMethod]
        public void BaseException_InheritancePreserved()
        {
            var ex = new MigrationException(MigrationErrorCategory.ValidationError, "Test error");
            Assert.IsInstanceOfType(ex, typeof(Exception));
        }

        [TestMethod]
        public void MultipleInstances_WithSameCategory_HaveSameExitCode()
        {
            var ex1 = new MigrationException(MigrationErrorCategory.RateLimited, "First");
            var ex2 = new MigrationException(MigrationErrorCategory.RateLimited, "Second");

            Assert.AreEqual(ex1.ExitCode, ex2.ExitCode);
            Assert.AreEqual(3, ex1.ExitCode);
            Assert.AreEqual(3, ex2.ExitCode);
        }
    }
}
