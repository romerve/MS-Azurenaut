namespace LlmInteractionLogger.Models;

/// <summary>The request payload is malformed. Maps to HTTP 400.</summary>
public sealed class InvalidPayloadException(string message) : Exception(message);

/// <summary>The request payload or an attachment exceeds a configured limit. Maps to HTTP 413.</summary>
public sealed class PayloadTooLargeException(string message) : Exception(message);
