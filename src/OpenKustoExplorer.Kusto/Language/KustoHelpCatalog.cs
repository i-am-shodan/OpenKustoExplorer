namespace OpenKustoExplorer.Kusto.Language;

/// <summary>
/// Supplies concise explanations for KQL grammar that is not described by SDK quick info.
/// </summary>
internal static class KustoHelpCatalog
{
    private static readonly Dictionary<string, KustoHelpCatalogEntry> Entries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["as"] = Operator("T | as Name", "Names a tabular expression so it can be referenced later in the query."),
            ["consume"] = Operator("T | consume", "Consumes the input without returning rows, which is useful for measuring query execution."),
            ["count"] = new("count", "Query operator or aggregate function", "T | count; count()", "Counts input rows, either as a terminal operator or within a summarize expression."),
            ["datatable"] = Source("datatable(Column: Type, ...) [ Values ]", "Creates an inline table whose schema and values are written directly in the query."),
            ["distinct"] = Operator("T | distinct Column, ...", "Returns one row for each distinct combination of the selected columns."),
            ["evaluate"] = Operator("T | evaluate Plugin(...) ", "Invokes a query-language plugin against the input table."),
            ["extend"] = Operator("T | extend Column = Expression", "Adds calculated columns or replaces existing columns without removing the other input columns."),
            ["facet"] = Operator("T | facet by Column", "Builds multiple aggregated views over the same input data."),
            ["find"] = Operator("find in (Tables) where Predicate", "Searches a set of tables for rows that satisfy a predicate."),
            ["fork"] = Operator("T | fork (Subquery) ...", "Runs multiple query branches over the same input and returns each branch as a result table."),
            ["getschema"] = Operator("T | getschema", "Returns the schema of the input table rather than its rows."),
            ["invoke"] = Operator("T | invoke Function(...) ", "Invokes a tabular function with the current pipeline as its first argument."),
            ["join"] = Operator("Left | join kind=Kind (Right) on Keys", "Combines rows from two tables by matching one or more key columns."),
            ["lookup"] = Operator("Left | lookup kind=leftouter (Right) on Keys", "Enriches a large input table with columns from a smaller dimension table."),
            ["make-graph"] = Operator("Edges | make-graph Source --> Target", "Creates a graph from edge rows and optional node tables."),
            ["make-series"] = Operator("T | make-series Aggregate on Axis step Step by Group", "Creates aligned arrays of aggregated values over an ordered axis, commonly a time range."),
            ["mv-apply"] = Operator("T | mv-apply Item = Array on (Subquery)", "Expands a dynamic array, applies a subquery to each expanded subtable, and combines the results."),
            ["mv-expand"] = Operator("T | mv-expand Item = Array", "Expands each element of a dynamic array or property bag into a separate row."),
            ["order"] = Operator("T | order by Expression asc|desc", "Sorts all input rows by one or more expressions."),
            ["parse"] = Operator("T | parse Column with Pattern", "Extracts structured values from a string using a pattern while retaining nonmatching rows."),
            ["parse-kv"] = Operator("T | parse-kv Column as (Name: Type, ...)", "Extracts key-value pairs from a string into typed columns."),
            ["parse-where"] = Operator("T | parse-where Column with Pattern", "Extracts values from matching strings and discards rows that do not match the pattern."),
            ["partition"] = Operator("T | partition by Key (Subquery)", "Runs a subquery independently for each distinct value of a partition key."),
            ["print"] = Source("print Name = Expression, ...", "Returns one row containing the specified scalar expressions."),
            ["project"] = new(
                "project",
                "Query operator",
                "T | project Expression, ...",
                "Selects, renames, or calculates output columns and removes unlisted columns.",
                CreateMicrosoftLearnUri("project-operator")),
            ["project-away"] = Operator("T | project-away Column, ...", "Removes selected columns from the output."),
            ["project-keep"] = Operator("T | project-keep Column, ...", "Keeps selected columns and removes all other columns."),
            ["project-rename"] = Operator("T | project-rename NewName = OldName", "Renames columns without changing their values or order."),
            ["project-reorder"] = Operator("T | project-reorder Column, ...", "Moves selected columns to the requested output positions."),
            ["range"] = Source("range Name from Start to Stop step Step", "Generates one row for each value in an evenly stepped numeric, datetime, or timespan range."),
            ["reduce"] = Operator("T | reduce by Expression", "Groups similar strings into representative patterns."),
            ["render"] = Operator("T | render Visualization", "Adds visualization instructions for the tabular result."),
            ["sample"] = Operator("T | sample NumberOfRows", "Returns a random sample of rows from the input."),
            ["sample-distinct"] = Operator("T | sample-distinct NumberOfValues of Column", "Returns a random sample of distinct values from a column."),
            ["scan"] = Operator("T | scan declare (...) with (step ...)", "Processes serialized rows in order with stateful steps, enabling sequence and session analysis."),
            ["search"] = Operator("search in (Tables) SearchExpression", "Searches selected tabular sources for matching terms or predicates."),
            ["serialize"] = Operator("T | serialize", "Marks the current row order as serialized for functions that depend on adjacent rows."),
            ["sort"] = Operator("T | sort by Expression asc|desc", "Sorts all input rows by one or more expressions."),
            ["summarize"] = Operator("T | summarize Aggregate by Group", "Groups rows and calculates aggregate values for each group."),
            ["take"] = Operator("T | take NumberOfRows", "Returns up to the requested number of rows without guaranteeing which rows are chosen."),
            ["limit"] = Operator("T | limit NumberOfRows", "Returns up to the requested number of rows; this is an alias for take."),
            ["top"] = Operator("T | top NumberOfRows by Expression", "Returns the highest- or lowest-ranked rows according to a sort expression."),
            ["top-hitters"] = Operator("T | top-hitters NumberOfValues of Column", "Returns the most frequent approximate values in a column."),
            ["top-nested"] = Operator("T | top-nested N of Column by Aggregate", "Produces hierarchical top-value aggregations across multiple grouping levels."),
            ["union"] = Operator("union TableOrExpression, ...", "Combines rows from multiple tabular inputs, matching columns by name and type."),
            ["where"] = new(
                "where",
                "Query operator",
                "T | where Predicate",
                "Filters the input rows to those for which the Boolean predicate evaluates to true."),
            ["let"] = Keyword("let Name = Expression;", "Binds a name to a scalar value, tabular expression, or user-defined function."),
            ["set"] = Keyword("set Option = Value;", "Sets a query option for subsequent statements in the request."),
            ["declare"] = Keyword("declare query_parameters(...);", "Declares typed query parameters that callers can supply."),
            ["by"] = Keyword("... by GroupExpression", "Introduces grouping or ordering expressions for the surrounding operator."),
            ["on"] = Keyword("... on KeyExpression", "Introduces matching keys, an axis, or another operator-specific expression."),
            ["kind"] = Keyword("kind=Flavor", "Selects a behavior variant for operators such as join, union, or mv-expand."),
            ["and"] = Predicate("Left and Right", "Returns true only when both Boolean expressions are true."),
            ["or"] = Predicate("Left or Right", "Returns true when either Boolean expression is true."),
            ["not"] = Predicate("not(Expression)", "Negates a Boolean expression."),
            ["=="] = Predicate("Left == Right", "Tests whether two values are equal using case-sensitive string comparison."),
            ["!="] = Predicate("Left != Right", "Tests whether two values are not equal using case-sensitive string comparison."),
            ["=~"] = Predicate("Left =~ Right", "Tests equality using case-insensitive string comparison."),
            ["!~"] = Predicate("Left !~ Right", "Tests inequality using case-insensitive string comparison."),
            ["<"] = Predicate("Left < Right", "Tests whether the left value is less than the right value."),
            ["<="] = Predicate("Left <= Right", "Tests whether the left value is less than or equal to the right value."),
            [">"] = Predicate("Left > Right", "Tests whether the left value is greater than the right value."),
            [">="] = Predicate("Left >= Right", "Tests whether the left value is greater than or equal to the right value."),
            ["between"] = Predicate("Expression between (Lower .. Upper)", "Tests whether a value falls within an inclusive range."),
            ["!between"] = Predicate("Expression !between (Lower .. Upper)", "Tests whether a value falls outside an inclusive range."),
            ["in"] = Predicate("Expression in (Value, ...)", "Tests whether a value equals any item in a case-sensitive set."),
            ["in~"] = Predicate("Expression in~ (Value, ...)", "Tests whether a string equals any item in a case-insensitive set."),
            ["!in"] = Predicate("Expression !in (Value, ...)", "Tests whether a value differs from every item in a case-sensitive set."),
            ["!in~"] = Predicate("Expression !in~ (Value, ...)", "Tests whether a string differs from every item in a case-insensitive set."),
            ["contains"] = Predicate("Text contains Fragment", "Tests whether a case-insensitive substring occurs anywhere in a string."),
            ["contains_cs"] = Predicate("Text contains_cs Fragment", "Tests whether a case-sensitive substring occurs anywhere in a string."),
            ["has"] = Predicate("Text has Term", "Tests whether a case-insensitive indexed term occurs in a string."),
            ["has_cs"] = Predicate("Text has_cs Term", "Tests whether a case-sensitive indexed term occurs in a string."),
            ["hasprefix"] = Predicate("Text hasprefix Prefix", "Tests whether an indexed term starts with the supplied case-insensitive prefix."),
            ["hassuffix"] = Predicate("Text hassuffix Suffix", "Tests whether an indexed term ends with the supplied case-insensitive suffix."),
            ["startswith"] = Predicate("Text startswith Prefix", "Tests whether a string starts with a case-insensitive substring."),
            ["endswith"] = Predicate("Text endswith Suffix", "Tests whether a string ends with a case-insensitive substring."),
            ["matches"] = Predicate("Text matches regex Pattern", "Tests whether a string matches a regular expression."),
            ["ago"] = Function("Returns the UTC datetime at the current query time minus a timespan."),
            ["now"] = Function("Returns the current UTC datetime, using one stable value throughout a query statement."),
            ["bin"] = Function("Rounds a numeric, datetime, or timespan value down to a multiple of the bin size."),
            ["bin_at"] = Function("Rounds a value down to a fixed-size bin aligned to a specified origin."),
            ["startofday"] = Function("Returns the start of the day containing a datetime."),
            ["endofday"] = Function("Returns the end of the day containing a datetime."),
            ["startofweek"] = Function("Returns the start of the week containing a datetime."),
            ["endofweek"] = Function("Returns the end of the week containing a datetime."),
            ["startofmonth"] = Function("Returns the start of the month containing a datetime."),
            ["endofmonth"] = Function("Returns the end of the month containing a datetime."),
            ["datetime_add"] = Function("Adds a requested number of time units to a datetime."),
            ["datetime_diff"] = Function("Returns the integer difference between two datetimes in a requested time unit."),
            ["format_datetime"] = Function("Formats a datetime using a KQL format pattern."),
            ["todatetime"] = Function("Converts a value to datetime, returning null when conversion fails."),
            ["totimespan"] = Function("Converts a value to timespan, returning null when conversion fails."),
            ["tostring"] = Function("Converts a scalar value to its string representation."),
            ["tobool"] = Function("Converts a value to bool, returning null when conversion fails."),
            ["toint"] = Function("Converts a value to a 32-bit integer, returning null when conversion fails."),
            ["tolong"] = Function("Converts a value to a 64-bit integer, returning null when conversion fails."),
            ["toreal"] = Function("Converts a value to a real number, returning null when conversion fails."),
            ["todouble"] = Function("Converts a value to a real number, returning null when conversion fails."),
            ["todecimal"] = Function("Converts a value to decimal, returning null when conversion fails."),
            ["toguid"] = Function("Converts a value to a GUID, returning null when conversion fails."),
            ["coalesce"] = Function("Returns the first argument that is not null; empty strings are also skipped for string arguments."),
            ["iff"] = Function("Returns one value when a Boolean condition is true and another when it is false."),
            ["iif"] = Function("Returns one value when a Boolean condition is true and another when it is false; this is an alias for iff."),
            ["case"] = Function("Evaluates conditions in order and returns the result for the first true condition."),
            ["isempty"] = Function("Returns true when a string is null or empty."),
            ["isnotempty"] = Function("Returns true when a string is neither null nor empty."),
            ["isnull"] = Function("Returns true when an expression evaluates to null."),
            ["isnotnull"] = Function("Returns true when an expression does not evaluate to null."),
            ["strlen"] = new(
                "strlen",
                "Scalar function",
                null,
                "Returns the number of characters in a string."),
            ["strcat"] = Function("Concatenates its arguments into one string."),
            ["substring"] = Function("Returns part of a string beginning at a zero-based position."),
            ["split"] = Function("Splits a string around a delimiter and returns a dynamic array of substrings."),
            ["trim"] = Function("Removes leading and trailing matches of a regular expression from a string."),
            ["tolower"] = Function("Converts a string to lowercase."),
            ["toupper"] = Function("Converts a string to uppercase."),
            ["extract"] = Function("Extracts one capture group from the first regular-expression match."),
            ["extract_all"] = Function("Returns all regular-expression matches or selected capture groups in a dynamic array."),
            ["replace_regex"] = Function("Replaces every regular-expression match in a string."),
            ["parse_json"] = Function("Parses a JSON string into a dynamic value."),
            ["dynamic"] = Function("Creates a dynamic literal that can contain arrays, property bags, or values not representable as literals."),
            ["bag_pack"] = Function("Creates a dynamic property bag from alternating key and value arguments."),
            ["pack_array"] = Function("Creates a dynamic array from its arguments."),
            ["array_length"] = Function("Returns the number of elements in a dynamic array."),
            ["array_concat"] = Function("Concatenates dynamic arrays into one array."),
            ["set_difference"] = Function("Returns values present in the first dynamic array but absent from all later arrays."),
            ["set_intersect"] = Function("Returns distinct values present in every supplied dynamic array."),
            ["set_union"] = Function("Returns the distinct union of values from all supplied dynamic arrays."),
            ["column_ifexists"] = Function("Returns a named column when it exists, otherwise a fallback expression."),
            ["toscalar"] = Function("Evaluates a tabular expression once and returns its first column and first row as a scalar value."),
            ["materialize"] = Function("Evaluates and caches a tabular expression for reuse during the query."),
            ["prev"] = Function("Returns a value from an earlier row in a serialized row set."),
            ["next"] = Function("Returns a value from a later row in a serialized row set."),
            ["row_number"] = Function("Returns the one-based row index in a serialized row set, with optional restart logic."),
            ["hash"] = Function("Returns a deterministic hash value for an expression."),
            ["base64_decode_tostring"] = Function("Decodes a base64 string and interprets the bytes as UTF-8 text."),
            ["url_decode"] = Function("Converts percent-encoded URL text back to its decoded form."),
            ["parse_ipv4"] = Function("Converts an IPv4 address string to its long-number representation."),
            ["ipv4_is_private"] = Function("Tests whether an IPv4 address belongs to a private network range."),
            ["ipv4_is_in_range"] = Function("Tests whether an IPv4 address belongs to a CIDR range."),
            ["arg_max"] = Aggregate("Returns a row that maximizes an expression, along with requested values from that row."),
            ["arg_min"] = Aggregate("Returns a row that minimizes an expression, along with requested values from that row."),
            ["avg"] = Aggregate("Returns the average of non-null values in a group."),
            ["avgif"] = Aggregate("Returns the average of values whose predicate is true."),
            ["countif"] = Aggregate("Counts rows whose predicate is true."),
            ["dcount"] = Aggregate("Returns an estimate of the number of distinct values."),
            ["dcountif"] = Aggregate("Returns an estimate of distinct values from rows whose predicate is true."),
            ["make_bag"] = Aggregate("Creates a dynamic property bag by merging property bags from the group."),
            ["make_list"] = Aggregate("Collects group values into a dynamic array, preserving order when the input is sorted."),
            ["make_set"] = Aggregate("Collects distinct group values into a dynamic array."),
            ["max"] = Aggregate("Returns the maximum non-null value in a group."),
            ["maxif"] = Aggregate("Returns the maximum value from rows whose predicate is true."),
            ["min"] = Aggregate("Returns the minimum non-null value in a group."),
            ["minif"] = Aggregate("Returns the minimum value from rows whose predicate is true."),
            ["percentile"] = Aggregate("Returns an estimate of a requested percentile for the group."),
            ["percentiles"] = Aggregate("Returns estimates of multiple requested percentiles for the group."),
            ["stdev"] = Aggregate("Returns the sample standard deviation of values in a group."),
            ["sum"] = Aggregate("Returns the sum of non-null values in a group."),
            ["sumif"] = Aggregate("Returns the sum of values whose predicate is true."),
            ["take_any"] = Aggregate("Returns an arbitrary non-empty value, or arbitrary row values, from a group."),
            ["variance"] = Aggregate("Returns the sample variance of values in a group."),
        };

    /// <summary>
    /// Gets a catalog entry by its KQL spelling.
    /// </summary>
    /// <param name="text">The syntax text.</param>
    /// <param name="entry">The matching catalog entry.</param>
    /// <returns><see langword="true"/> when the catalog contains the syntax element.</returns>
    internal static bool TryGet(string text, out KustoHelpCatalogEntry? entry)
    {
        bool found = Entries.TryGetValue(text, out entry);
        if (found && entry is { Title.Length: 0 })
        {
            entry = entry with { Title = text };
        }

        return found;
    }

    private static Uri CreateMicrosoftLearnUri(string topic)
    {
        return new UriBuilder(Uri.UriSchemeHttps, "learn.microsoft.com")
        {
            Path = $"en-us/kusto/query/{topic}",
            Query = "view=microsoft-fabric",
        }.Uri;
    }

    private static KustoHelpCatalogEntry Aggregate(string description)
    {
        return new KustoHelpCatalogEntry(string.Empty, "Aggregate function", null, description);
    }

    private static KustoHelpCatalogEntry Function(string description)
    {
        return new KustoHelpCatalogEntry(string.Empty, "Scalar function", null, description);
    }

    private static KustoHelpCatalogEntry Keyword(string signature, string description)
    {
        return new KustoHelpCatalogEntry(string.Empty, "Keyword", signature, description);
    }

    private static KustoHelpCatalogEntry Operator(string signature, string description)
    {
        return new KustoHelpCatalogEntry(string.Empty, "Query operator", signature, description);
    }

    private static KustoHelpCatalogEntry Predicate(string signature, string description)
    {
        return new KustoHelpCatalogEntry(string.Empty, "Predicate operator", signature, description);
    }

    private static KustoHelpCatalogEntry Source(string signature, string description)
    {
        return new KustoHelpCatalogEntry(string.Empty, "Tabular expression", signature, description);
    }
}
