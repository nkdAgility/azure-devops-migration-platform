using System;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.CLI.Migration.Utilities;

/// <summary>
/// Sanitizes exception messages to mask sensitive credentials (PAT tokens, basic auth, API keys, etc.)
/// before displaying them to users or logging them.
/// Prevents accidental credential exposure in CLI output, logs, and error messages.
/// </summary>
public static class ExceptionSanitizer
{
    /// <summary>
    /// Sanitizes an exception message by masking sensitive credentials.
    /// </summary>
    /// <param name="exception">The exception to sanitize.</param>
    /// <returns>A sanitized exception with masked credentials in the message and stack trace.</returns>
    public static Exception SanitizeException(Exception exception)
    {
        if (exception == null)
            return null!;

        try
        {
            var sanitizedMessage = SanitizeMessage(exception.Message);

            // Create a new exception with the same type and sanitized message
            var sanitized = new Exception(sanitizedMessage, exception.InnerException != null
                ? SanitizeException(exception.InnerException)
                : null);

            return sanitized;
        }
        catch
        {
            // If sanitization fails, return the original exception
            return exception;
        }
    }

    /// <summary>
    /// Sanitizes a string message by masking sensitive information.
    /// </summary>
    /// <param name="message">The message to sanitize.</param>
    /// <returns>The sanitized message with credentials masked.</returns>
    public static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return message ?? string.Empty;

        var sanitized = message;

        try
        {
            // Pattern 1: password=value or passwd=value or pwd=value (with lookahead to find delimiter)
            sanitized = Regex.Replace(sanitized, @"(?i)(password|passwd|pwd)\s*=\s*\S+",
                "$1=***", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Pattern 2: api_key, apikey, secret, token patterns
            sanitized = Regex.Replace(sanitized, @"(?i)(api[_-]?key|apikey|secret|token|access_token|refresh_token)\s*[:=]\s*\S+",
                "$1=***", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Pattern 3: Basic auth with base64
            sanitized = Regex.Replace(sanitized, @"(?i)\b(basic)\s+[A-Za-z0-9+/=]{10,}",
                "$1 ***", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Pattern 4: Bearer tokens
            sanitized = Regex.Replace(sanitized, @"(?i)\b(bearer)\s+[A-Za-z0-9_\-\.]+",
                "$1 ***", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Pattern 5: URLs with embedded user:password
            sanitized = Regex.Replace(sanitized, @"(https?://)[^:/@]+:[^@/]+@",
                "$1***:***@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            // If any pattern fails, return the original message
            // Better to show the message than to fail completely
            return message;
        }

        return sanitized;
    }
}
