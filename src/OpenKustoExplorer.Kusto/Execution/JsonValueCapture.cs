using System.Text;
using System.Text.Json;

namespace OpenKustoExplorer.Kusto.Execution;

/// <summary>
/// Captures one nested JSON row value token by token without materializing the surrounding response.
/// </summary>
internal sealed class JsonValueCapture : IDisposable
{
    private readonly int startDepth;
    private readonly MemoryStream stream = new();
    private readonly Utf8JsonWriter writer;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonValueCapture"/> class from a container start token.
    /// </summary>
    /// <param name="reader">The reader positioned on a start-object or start-array token.</param>
    internal JsonValueCapture(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not JsonTokenType.StartObject and not JsonTokenType.StartArray)
        {
            throw new ArgumentException("A nested JSON capture must begin on a container token.", nameof(reader));
        }

        startDepth = reader.CurrentDepth;
        writer = new Utf8JsonWriter(stream);
        WriteToken(ref reader);
    }

    /// <summary>
    /// Gets a value indicating whether the complete nested value has been captured.
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <inheritdoc />
    void IDisposable.Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            writer.Dispose();
            stream.Dispose();
        }
    }

    /// <summary>
    /// Gets the compact JSON value after capture completes.
    /// </summary>
    /// <returns>The exact semantic JSON value in compact form.</returns>
    internal string GetValue()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!IsComplete)
        {
            throw new InvalidOperationException("The nested JSON value is not complete.");
        }

        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Appends the current nested token.
    /// </summary>
    /// <param name="reader">The streaming reader positioned on the next nested token.</param>
    internal void Append(ref Utf8JsonReader reader)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (IsComplete)
        {
            throw new InvalidOperationException("The nested JSON value is already complete.");
        }

        WriteToken(ref reader);
        IsComplete = reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray
            && reader.CurrentDepth == startDepth;
    }

    private void WriteToken(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                writer.WriteStartObject();
                break;
            case JsonTokenType.EndObject:
                writer.WriteEndObject();
                break;
            case JsonTokenType.StartArray:
                writer.WriteStartArray();
                break;
            case JsonTokenType.EndArray:
                writer.WriteEndArray();
                break;
            case JsonTokenType.PropertyName:
                writer.WritePropertyName(reader.GetString() ?? string.Empty);
                break;
            case JsonTokenType.String:
                writer.WriteStringValue(reader.GetString());
                break;
            case JsonTokenType.Number:
                writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                break;
            case JsonTokenType.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonTokenType.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonTokenType.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException($"Unsupported nested JSON token '{reader.TokenType}'.");
        }
    }
}
