using System.Security.Cryptography;
using System.Text;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Creates stable identities for exact result rows and their ordered schemas.
/// </summary>
public static class KustoRecordedRowCanonicalizer
{
    /// <summary>
    /// Creates an exact row key from ordered columns and values.
    /// </summary>
    /// <param name="columns">The ordered result columns.</param>
    /// <param name="row">The result row.</param>
    /// <returns>A lowercase SHA-256 row key.</returns>
    public static string CreateKey(IReadOnlyList<KustoResultColumn> columns, KustoResultRow row)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(row);
        if (columns.Count != row.ResultValues.Count)
        {
            throw new ArgumentException("The result row must match the ordered column count.", nameof(row));
        }

        StringBuilder builder = new();
        for (int index = 0; index < columns.Count; index++)
        {
            KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(
                columns[index].TypeName,
                row.ResultValues[index]);
            builder.Append(columns[index].Name)
                .Append('\0')
                .Append(identity.TypeName)
                .Append('\0')
                .Append(identity.IsNull ? '1' : '0')
                .Append('\0')
                .Append(identity.CanonicalValue)
                .Append('\u001e');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
