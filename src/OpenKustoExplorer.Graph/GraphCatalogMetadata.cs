using System.Text;

namespace OpenKustoExplorer.Graph;

/// <summary>
/// Validates and normalizes analyst-owned graph catalog metadata.
/// </summary>
public static class GraphCatalogMetadata
{
    /// <summary>
    /// Gets the maximum graph description length.
    /// </summary>
    public const int MaximumDescriptionLength = 2_000;

    /// <summary>
    /// Gets the maximum graph name length.
    /// </summary>
    public const int MaximumNameLength = 120;

    /// <summary>
    /// Normalizes an optional graph description for storage.
    /// </summary>
    /// <param name="description">The analyst-entered description.</param>
    /// <returns>The trimmed description, or an empty string.</returns>
    public static string NormalizeDescription(string? description)
    {
        string normalized = description?.Trim() ?? string.Empty;

        if (normalized.Length > MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"Graph descriptions cannot exceed {MaximumDescriptionLength:N0} characters.",
                nameof(description));
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes a graph name for display and storage.
    /// </summary>
    /// <param name="name">The analyst-entered graph name.</param>
    /// <returns>The trimmed compatibility-normalized name.</returns>
    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim().Normalize(NormalizationForm.FormKC);

        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"Graph names cannot exceed {MaximumNameLength:N0} characters.",
                nameof(name));
        }

        return normalized;
    }

    /// <summary>
    /// Creates a case-insensitive uniqueness key for a graph name.
    /// </summary>
    /// <param name="name">The graph name.</param>
    /// <returns>The invariant normalized catalog key.</returns>
    public static string NormalizeNameKey(string name) => NormalizeName(name).ToUpperInvariant();
}
