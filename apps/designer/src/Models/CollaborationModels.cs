using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Designer.Models;

/// <summary>
/// Represents real-time presence and awareness signals broadcast between co-authors.
/// </summary>
public record AwarenessState(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("userRole")] string UserRole,
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("cursorX")] double? CursorX = null,
    [property: JsonPropertyName("cursorY")] double? CursorY = null,
    [property: JsonPropertyName("activeSectionId")] string? ActiveSectionId = null,
    [property: JsonPropertyName("selectedFieldId")] string? SelectedFieldId = null,
    [property: JsonPropertyName("timestamp")] long Timestamp = 0);

/// <summary>
/// Result of an eCRF snapshot save and asynchronous compilation attempt.
/// </summary>
public record SnapshotSaveResult(
    [property: JsonPropertyName("formId")] string FormId,
    [property: JsonPropertyName("savedAt")] DateTimeOffset SavedAt,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("compilationResult")] object? CompilationResult = null,
    [property: JsonPropertyName("compilationError")] string? CompilationError = null);
