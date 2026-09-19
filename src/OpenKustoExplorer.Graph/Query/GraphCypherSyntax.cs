using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

#pragma warning disable MA0048, SA1402, SA1600, SA1602, SA1649

namespace OpenKustoExplorer.Graph.Query;

internal enum RelationshipDirection
{
    Outgoing,
    Incoming,
    Undirected,
}

internal enum BindingKind
{
    Node,
    Relationship,
}

internal enum LiteralKind
{
    Null,
    Text,
    Number,
    Boolean,
}

internal enum TokenKind
{
    End,
    Identifier,
    StringLiteral,
    Number,
    LeftParenthesis,
    RightParenthesis,
    LeftBracket,
    RightBracket,
    LeftBrace,
    RightBrace,
    Colon,
    Comma,
    Dot,
    Semicolon,
    Star,
    Minus,
    ArrowRight,
    ArrowLeft,
    Equals,
    NotEquals,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Start, int Length);

internal readonly record struct VariableBinding(BindingKind Kind, int Index);

internal sealed class GraphCypherParser
{
    private const int MaximumLimit = 1_000;
    private const int MaximumSkip = 10_000;
    private readonly string queryText;
    private readonly ReadOnlyCollection<Token> tokens;
    private int index;

    internal GraphCypherParser(string queryText)
    {
        this.queryText = queryText;
        tokens = new GraphCypherLexer(queryText).Tokenize();
    }

    internal Token Current => tokens[index];

    internal Token Previous => tokens[Math.Max(0, index - 1)];

    internal static int EndOf(Token token) => token.Start + token.Length;

    internal ParsedQuery Parse()
    {
        ExpectKeyword("MATCH");
        NodePattern firstNode = ParseNode();
        RelationshipPattern? relationship = null;
        NodePattern? secondNode = null;

        if (Current.Kind is TokenKind.Minus or TokenKind.ArrowLeft)
        {
            relationship = ParseRelationship();
            secondNode = ParseNode();
        }

        if (Current.Kind is TokenKind.Minus or TokenKind.ArrowLeft or TokenKind.Comma)
        {
            throw Failure("This release supports one connected relationship pattern per query.");
        }

        Expression? where = null;

        if (MatchKeyword("WHERE"))
        {
            where = ParseOr();
        }

        int returnStart = Current.Start;
        ExpectKeyword("RETURN");
        bool isDistinct = MatchKeyword("DISTINCT");
        List<ReturnItem> returnItems = ParseReturnItems();
        List<OrderItem> orderItems = [];

        if (MatchKeyword("ORDER"))
        {
            ExpectKeyword("BY");
            orderItems = ParseOrderItems();
        }

        int skip = 0;
        int? limit = null;

        if (MatchKeyword("SKIP"))
        {
            skip = ParseBoundedInteger("SKIP", MaximumSkip);
        }

        if (MatchKeyword("LIMIT"))
        {
            limit = ParseBoundedInteger("LIMIT", MaximumLimit);
        }

        Match(TokenKind.Semicolon);

        if (Current.Kind != TokenKind.End)
        {
            string message = IsWriteKeyword(Current.Text)
                ? $"Graph write clause '{Current.Text}' is not supported. Queries are read-only."
                : $"Unexpected clause or token '{Current.Text}'.";
            throw Failure(message);
        }

        ParsedQuery parsed = new(
            firstNode,
            relationship,
            secondNode,
            where,
            returnItems,
            orderItems,
            isDistinct,
            skip,
            limit,
            returnStart);
        parsed.ValidateBindings();
        return parsed;
    }

    private static bool IsWriteKeyword(string text)
    {
        return text.Equals("CREATE", StringComparison.OrdinalIgnoreCase)
            || text.Equals("MERGE", StringComparison.OrdinalIgnoreCase)
            || text.Equals("SET", StringComparison.OrdinalIgnoreCase)
            || text.Equals("REMOVE", StringComparison.OrdinalIgnoreCase)
            || text.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
            || text.Equals("DETACH", StringComparison.OrdinalIgnoreCase)
            || text.Equals("DROP", StringComparison.OrdinalIgnoreCase);
    }

    private NodePattern ParseNode()
    {
        Token start = Expect(TokenKind.LeftParenthesis, "Expected '(' to begin a node pattern.");
        string variable = string.Empty;
        string label = string.Empty;

        if (Current.Kind == TokenKind.Identifier && !Current.Text.Equals("WHERE", StringComparison.OrdinalIgnoreCase))
        {
            variable = Advance().Text;
        }

        if (Match(TokenKind.Colon))
        {
            label = ExpectIdentifier("Expected a node label after ':'.").Text;
        }

        IReadOnlyList<PropertyMapItem> properties = Current.Kind == TokenKind.LeftBrace
            ? ParsePropertyMap()
            : [];
        Token end = Expect(TokenKind.RightParenthesis, "Expected ')' to close the node pattern.");
        return new NodePattern(variable, label, properties, start.Start, EndOf(end) - start.Start);
    }

    private RelationshipPattern ParseRelationship()
    {
        Token start = Current;
        bool beginsIncoming = Match(TokenKind.ArrowLeft);

        if (!beginsIncoming)
        {
            Expect(TokenKind.Minus, "Expected '-' before the relationship pattern.");
        }

        string variable = string.Empty;
        string typeName = string.Empty;
        IReadOnlyList<PropertyMapItem> properties = [];

        if (Match(TokenKind.LeftBracket))
        {
            if (Current.Kind == TokenKind.Identifier)
            {
                variable = Advance().Text;
            }

            if (Match(TokenKind.Colon))
            {
                typeName = ExpectIdentifier("Expected a relationship type after ':'.").Text;
            }

            if (Match(TokenKind.Star))
            {
                throw Failure("Variable-length paths are not supported in this release. Use a fixed relationship pattern.");
            }

            properties = Current.Kind == TokenKind.LeftBrace ? ParsePropertyMap() : [];
            Expect(TokenKind.RightBracket, "Expected ']' to close the relationship pattern.");
        }

        RelationshipDirection direction;

        if (beginsIncoming)
        {
            Expect(TokenKind.Minus, "Expected '-' after an incoming relationship pattern.");
            direction = RelationshipDirection.Incoming;
        }
        else if (Match(TokenKind.ArrowRight))
        {
            direction = RelationshipDirection.Outgoing;
        }
        else
        {
            Expect(TokenKind.Minus, "Expected '-' or '->' after the relationship pattern.");
            direction = RelationshipDirection.Undirected;
        }

        return new RelationshipPattern(
            variable,
            typeName,
            direction,
            properties,
            start.Start,
            Current.Start - start.Start);
    }

    private ReadOnlyCollection<PropertyMapItem> ParsePropertyMap()
    {
        Expect(TokenKind.LeftBrace, "Expected '{' to begin a property map.");
        List<PropertyMapItem> properties = [];

        if (!Match(TokenKind.RightBrace))
        {
            do
            {
                Token name = ExpectIdentifier("Expected a property name.");
                Expect(TokenKind.Colon, "Expected ':' after the property name.");
                LiteralExpression value = ParseLiteral();
                properties.Add(new PropertyMapItem(
                    name.Text,
                    value,
                    name.Start,
                    EndOf(Previous) - name.Start));
            }
            while (Match(TokenKind.Comma));

            Expect(TokenKind.RightBrace, "Expected '}' to close the property map.");
        }

        return properties.AsReadOnly();
    }

    private List<ReturnItem> ParseReturnItems()
    {
        List<ReturnItem> items = [];

        do
        {
            int start = Current.Start;
            Expression expression = ParseValueExpression(allowStar: true);
            int end = EndOf(Previous);
            string? alias = MatchKeyword("AS")
                ? ExpectIdentifier("Expected a column alias after AS.").Text
                : null;
            items.Add(new ReturnItem(
                expression,
                alias,
                queryText[start..end].Trim()));
        }
        while (Match(TokenKind.Comma));

        if (items.Count == 0)
        {
            throw Failure("RETURN must project at least one expression.");
        }

        return items;
    }

    private List<OrderItem> ParseOrderItems()
    {
        List<OrderItem> items = [];

        do
        {
            Expression expression = ParseValueExpression(allowStar: false);
            bool descending = MatchKeyword("DESC");

            if (!descending)
            {
                MatchKeyword("ASC");
            }

            items.Add(new OrderItem(expression, descending));
        }
        while (Match(TokenKind.Comma));

        return items;
    }

    private Expression ParseOr()
    {
        Expression expression = ParseAnd();

        while (MatchKeyword("OR"))
        {
            expression = new BinaryExpression(expression, "OR", ParseAnd());
        }

        return expression;
    }

    private Expression ParseAnd()
    {
        Expression expression = ParseNot();

        while (MatchKeyword("AND"))
        {
            expression = new BinaryExpression(expression, "AND", ParseNot());
        }

        return expression;
    }

    private Expression ParseNot()
    {
        if (MatchKeyword("NOT"))
        {
            return new UnaryExpression(ParseNot());
        }

        if (Match(TokenKind.LeftParenthesis))
        {
            Expression nested = ParseOr();
            Expect(TokenKind.RightParenthesis, "Expected ')' after the filter expression.");
            return nested;
        }

        return ParseComparison();
    }

    private Expression ParseComparison()
    {
        Expression left = ParseValueExpression(allowStar: false);

        if (MatchKeyword("IS"))
        {
            bool negated = MatchKeyword("NOT");
            ExpectKeyword("NULL");
            return new NullTestExpression(left, negated);
        }

        if (MatchKeyword("IN"))
        {
            Expect(TokenKind.LeftBracket, "Expected '[' after IN.");
            List<Expression> values = [];

            if (!Match(TokenKind.RightBracket))
            {
                do
                {
                    values.Add(ParseValueExpression(allowStar: false));
                }
                while (Match(TokenKind.Comma));

                Expect(TokenKind.RightBracket, "Expected ']' after the IN values.");
            }

            return new InExpression(left, values);
        }

        string? comparisonOperator = ParseComparisonOperator();

        if (comparisonOperator is null)
        {
            throw Failure("Expected a comparison operator in WHERE.");
        }

        return new ComparisonExpression(
            left,
            comparisonOperator,
            ParseValueExpression(allowStar: false));
    }

    private string? ParseComparisonOperator()
    {
        string? value = Current.Kind switch
        {
            TokenKind.Equals => "=",
            TokenKind.NotEquals => "<>",
            TokenKind.LessThan => "<",
            TokenKind.LessThanOrEqual => "<=",
            TokenKind.GreaterThan => ">",
            TokenKind.GreaterThanOrEqual => ">=",
            _ => null,
        };

        if (value is not null)
        {
            Advance();
        }
        else if (MatchKeyword("CONTAINS"))
        {
            value = "CONTAINS";
        }
        else if (MatchKeyword("STARTS"))
        {
            ExpectKeyword("WITH");
            value = "STARTS WITH";
        }
        else if (MatchKeyword("ENDS"))
        {
            ExpectKeyword("WITH");
            value = "ENDS WITH";
        }

        return value;
    }

    private Expression ParseValueExpression(bool allowStar)
    {
        Token token = Current;

        if (token.Kind is TokenKind.StringLiteral or TokenKind.Number
            || IsKeyword("TRUE") || IsKeyword("FALSE") || IsKeyword("NULL"))
        {
            return ParseLiteral();
        }

        if (token.Kind != TokenKind.Identifier)
        {
            throw Failure("Expected a variable, property, function, or literal value.");
        }

        Advance();

        if (Match(TokenKind.LeftParenthesis))
        {
            bool distinct = MatchKeyword("DISTINCT");
            bool star = allowStar && Match(TokenKind.Star);
            List<Expression> arguments = [];

            if (!star && !Match(TokenKind.RightParenthesis))
            {
                do
                {
                    arguments.Add(ParseValueExpression(allowStar: false));
                }
                while (Match(TokenKind.Comma));

                Expect(TokenKind.RightParenthesis, "Expected ')' after function arguments.");
            }
            else if (star)
            {
                Expect(TokenKind.RightParenthesis, "Expected ')' after '*'.");
            }

            ValidateFunction(token, arguments, star);
            return new FunctionExpression(
                token.Text,
                arguments,
                star,
                distinct,
                token.Start,
                EndOf(Previous) - token.Start);
        }

        if (Match(TokenKind.Dot))
        {
            Token property = ExpectIdentifier("Expected a property name after '.'.");
            return new PropertyExpression(
                token.Text,
                property.Text,
                token.Start,
                EndOf(property) - token.Start);
        }

        return new VariableExpression(token.Text, token.Start, token.Length);
    }

    [SuppressMessage(
        "StyleCop.CSharp.OrderingRules",
        "SA1204:Static members should appear before non-static members",
        Justification = "Function validation stays beside the parser production that invokes it.")]
    private static void ValidateFunction(Token token, List<Expression> arguments, bool star)
    {
        string name = token.Text.ToUpperInvariant();
        bool valid = name switch
        {
            "COUNT" => star || arguments.Count == 1,
            "TYPE" or "LABELS" or "ELEMENTID" or "PROPERTIES" or "TOLOWER" or "TOUPPER" =>
                !star && arguments.Count == 1,
            _ => false,
        };

        if (!valid)
        {
            throw new GraphCypherParseException(
                token.Start,
                token.Length,
                $"Function '{token.Text}' is not supported or has invalid arguments.");
        }
    }

    private LiteralExpression ParseLiteral()
    {
        Token token = Advance();

        if (token.Kind == TokenKind.StringLiteral)
        {
            return LiteralExpression.FromText(token.Text, token.Start, token.Length);
        }

        if (token.Kind == TokenKind.Number
            && double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return LiteralExpression.FromNumber(number, token.Start, token.Length);
        }

        if (token.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || token.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return LiteralExpression.FromBoolean(
                token.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                token.Start,
                token.Length);
        }

        if (token.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return LiteralExpression.FromNull(token.Start, token.Length);
        }

        throw new GraphCypherParseException(
            token.Start,
            token.Length,
            "Expected a string, number, Boolean, or null literal.");
    }

    private int ParseBoundedInteger(string clauseName, int maximum)
    {
        Token token = Expect(TokenKind.Number, $"Expected an integer after {clauseName}.");

        if (!int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value < 0
            || value > maximum)
        {
            throw new GraphCypherParseException(
                token.Start,
                token.Length,
                $"{clauseName} must be an integer from 0 to {maximum:N0}.");
        }

        return value;
    }

    private void ExpectKeyword(string keyword)
    {
        if (!MatchKeyword(keyword))
        {
            throw Failure($"Expected {keyword}.");
        }
    }

    private bool MatchKeyword(string keyword)
    {
        bool matches = IsKeyword(keyword);

        if (matches)
        {
            Advance();
        }

        return matches;
    }

    private bool IsKeyword(string keyword)
    {
        return Current.Kind == TokenKind.Identifier
            && Current.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private Token ExpectIdentifier(string message) => Expect(TokenKind.Identifier, message);

    private Token Expect(TokenKind kind, string message)
    {
        if (Current.Kind != kind)
        {
            throw Failure(message);
        }

        return Advance();
    }

    private bool Match(TokenKind kind)
    {
        bool matches = Current.Kind == kind;

        if (matches)
        {
            Advance();
        }

        return matches;
    }

    private Token Advance()
    {
        Token token = Current;

        if (index < tokens.Count - 1)
        {
            index++;
        }

        return token;
    }

    private GraphCypherParseException Failure(string message)
    {
        return new GraphCypherParseException(Current.Start, Math.Max(Current.Length, 1), message);
    }
}

internal sealed class GraphCypherLexer
{
    private readonly string text;
    private int position;

    internal GraphCypherLexer(string text)
    {
        this.text = text;
    }

    internal ReadOnlyCollection<Token> Tokenize()
    {
        List<Token> tokens = [];

        while (true)
        {
            SkipTrivia();

            if (position >= text.Length)
            {
                tokens.Add(new Token(TokenKind.End, string.Empty, position, 0));
                break;
            }

            tokens.Add(ReadToken());
        }

        return tokens.AsReadOnly();
    }

    private Token ReadToken()
    {
        int start = position;
        char character = text[position++];
        return character switch
        {
            '(' => Create(TokenKind.LeftParenthesis, start),
            ')' => Create(TokenKind.RightParenthesis, start),
            '[' => Create(TokenKind.LeftBracket, start),
            ']' => Create(TokenKind.RightBracket, start),
            '{' => Create(TokenKind.LeftBrace, start),
            '}' => Create(TokenKind.RightBrace, start),
            ':' => Create(TokenKind.Colon, start),
            ',' => Create(TokenKind.Comma, start),
            '.' when position < text.Length && char.IsDigit(text[position]) => ReadNumber(start),
            '.' => Create(TokenKind.Dot, start),
            ';' => Create(TokenKind.Semicolon, start),
            '*' => Create(TokenKind.Star, start),
            '=' => Create(TokenKind.Equals, start),
            '!' when Match('=') => Create(TokenKind.NotEquals, start),
            '!' => throw new GraphCypherParseException(start, 1, "Expected '=' after '!'."),
            '-' when Match('>') => Create(TokenKind.ArrowRight, start),
            '-' => Create(TokenKind.Minus, start),
            '<' when Match('-') => Create(TokenKind.ArrowLeft, start),
            '<' when Match('=') => Create(TokenKind.LessThanOrEqual, start),
            '<' when Match('>') => Create(TokenKind.NotEquals, start),
            '<' => Create(TokenKind.LessThan, start),
            '>' when Match('=') => Create(TokenKind.GreaterThanOrEqual, start),
            '>' => Create(TokenKind.GreaterThan, start),
            '\'' => ReadString(start),
            '`' => ReadQuotedIdentifier(start),
            _ when char.IsLetter(character) || character == '_' => ReadIdentifier(start),
            _ when char.IsDigit(character) => ReadNumber(start),
            _ => throw new GraphCypherParseException(start, 1, $"Unexpected character '{character}'."),
        };
    }

    private Token ReadIdentifier(int start)
    {
        while (position < text.Length
            && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position++;
        }

        return Create(TokenKind.Identifier, start);
    }

    private Token ReadQuotedIdentifier(int start)
    {
        StringBuilder value = new();

        while (position < text.Length)
        {
            char character = text[position++];

            if (character == '`')
            {
                if (position < text.Length && text[position] == '`')
                {
                    position++;
                    value.Append('`');
                }
                else
                {
                    return new Token(TokenKind.Identifier, value.ToString(), start, position - start);
                }
            }
            else
            {
                value.Append(character);
            }
        }

        throw new GraphCypherParseException(start, text.Length - start, "Unterminated quoted identifier.");
    }

    private Token ReadString(int start)
    {
        StringBuilder value = new();

        while (position < text.Length)
        {
            char character = text[position++];

            if (character == '\'')
            {
                if (position < text.Length && text[position] == '\'')
                {
                    position++;
                    value.Append('\'');
                }
                else
                {
                    return new Token(TokenKind.StringLiteral, value.ToString(), start, position - start);
                }
            }
            else if (character == '\\' && position < text.Length)
            {
                value.Append(ReadEscape(text[position++]));
            }
            else
            {
                value.Append(character);
            }
        }

        throw new GraphCypherParseException(start, text.Length - start, "Unterminated string literal.");
    }

    [SuppressMessage(
        "StyleCop.CSharp.OrderingRules",
        "SA1204:Static members should appear before non-static members",
        Justification = "Escape decoding stays beside string-token parsing.")]
    private static char ReadEscape(char character)
    {
        return character switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            _ => character,
        };
    }

    private Token ReadNumber(int start)
    {
        while (position < text.Length && char.IsDigit(text[position]))
        {
            position++;
        }

        if (position < text.Length && text[position] == '.')
        {
            position++;

            while (position < text.Length && char.IsDigit(text[position]))
            {
                position++;
            }
        }

        return Create(TokenKind.Number, start);
    }

    private void SkipTrivia()
    {
        bool skipped;
        do
        {
            skipped = SkipWhitespace() || SkipLineComment() || SkipBlockComment();
        }
        while (skipped);
    }

    private bool SkipWhitespace()
    {
        int start = position;
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position > start;
    }

    private bool SkipLineComment()
    {
        if (!StartsWith("//"))
        {
            return false;
        }

        position += 2;
        while (position < text.Length && text[position] is not '\r' and not '\n')
        {
            position++;
        }

        return true;
    }

    private bool SkipBlockComment()
    {
        if (!StartsWith("/*"))
        {
            return false;
        }

        int start = position;
        position += 2;
        while (position + 1 < text.Length && !StartsWith("*/"))
        {
            position++;
        }

        if (position + 1 >= text.Length)
        {
            throw new GraphCypherParseException(start, text.Length - start, "Unterminated block comment.");
        }

        position += 2;
        return true;
    }

    private bool StartsWith(string value)
    {
        return position + value.Length <= text.Length
            && text.AsSpan(position, value.Length).SequenceEqual(value);
    }

    private bool Match(char expected)
    {
        bool matches = position < text.Length && text[position] == expected;

        if (matches)
        {
            position++;
        }

        return matches;
    }

    private Token Create(TokenKind kind, int start)
    {
        return new Token(kind, text[start..position], start, position - start);
    }
}

internal sealed class ParsedQuery
{
    private readonly Dictionary<string, VariableBinding> bindings = new(StringComparer.Ordinal);

    internal ParsedQuery(
        NodePattern firstNode,
        RelationshipPattern? relationship,
        NodePattern? secondNode,
        Expression? where,
        IReadOnlyList<ReturnItem> returnItems,
        IReadOnlyList<OrderItem> orderItems,
        bool isDistinct,
        int skip,
        int? limit,
        int returnStart)
    {
        FirstNode = firstNode;
        Relationship = relationship;
        SecondNode = secondNode;
        Where = where;
        ReturnItems = returnItems;
        OrderItems = orderItems;
        IsDistinct = isDistinct;
        Skip = skip;
        Limit = limit;
        ReturnStart = returnStart;
    }

    internal NodePattern FirstNode { get; }

    internal RelationshipPattern? Relationship { get; }

    internal NodePattern? SecondNode { get; }

    internal Expression? Where { get; }

    internal IReadOnlyList<ReturnItem> ReturnItems { get; }

    internal IReadOnlyList<OrderItem> OrderItems { get; }

    internal bool IsDistinct { get; }

    internal int Skip { get; }

    internal int? Limit { get; }

    internal int ReturnStart { get; }

    internal void ValidateBindings()
    {
        AddBinding(FirstNode.VariableName, new VariableBinding(BindingKind.Node, 0), FirstNode);

        if (SecondNode is not null)
        {
            AddBinding(SecondNode.VariableName, new VariableBinding(BindingKind.Node, 1), SecondNode);
        }

        if (Relationship is not null)
        {
            AddBinding(
                Relationship.VariableName,
                new VariableBinding(BindingKind.Relationship, 0),
                Relationship);
        }

        IEnumerable<Expression> expressions = ReturnItems.Select(item => item.Expression)
            .Concat(OrderItems.Select(item => item.Expression));

        if (Where is not null)
        {
            expressions = expressions.Append(Where);
        }

        foreach (Expression expression in expressions)
        {
            ValidateExpression(expression);
        }
    }

    internal VariableBinding GetBinding(string variableName, int start, int length)
    {
        return bindings.TryGetValue(variableName, out VariableBinding binding)
            ? binding
            : throw new GraphCypherParseException(start, length, $"Variable '{variableName}' is not defined by MATCH.");
    }

    internal string GetNodeVariable(int nodeIndex)
    {
        NodePattern node = nodeIndex == 0 ? FirstNode : SecondNode!;
        return node.VariableName;
    }

    private void AddBinding(
        string variableName,
        VariableBinding binding,
        PatternElement pattern)
    {
        if (!string.IsNullOrWhiteSpace(variableName)
            && !bindings.TryAdd(variableName, binding))
        {
            throw new GraphCypherParseException(
                pattern.Start,
                pattern.Length,
                $"Variable '{variableName}' is declared more than once.");
        }
    }

    private void ValidateExpression(Expression expression)
    {
        switch (expression)
        {
            case VariableExpression variable:
                GetBinding(variable.Name, variable.Start, variable.Length);
                break;
            case PropertyExpression property:
                GetBinding(property.VariableName, property.Start, property.Length);
                break;
            case FunctionExpression function:
                foreach (Expression argument in function.Arguments)
                {
                    ValidateExpression(argument);
                }

                break;
            case BinaryExpression binary:
                ValidateExpression(binary.Left);
                ValidateExpression(binary.Right);
                break;
            case UnaryExpression unary:
                ValidateExpression(unary.Operand);
                break;
            case ComparisonExpression comparison:
                ValidateExpression(comparison.Left);
                ValidateExpression(comparison.Right);
                break;
            case NullTestExpression nullTest:
                ValidateExpression(nullTest.Operand);
                break;
            case InExpression inExpression:
                ValidateExpression(inExpression.Operand);

                foreach (Expression value in inExpression.Values)
                {
                    ValidateExpression(value);
                }

                break;
        }
    }
}

internal abstract class PatternElement
{
    protected PatternElement(string variableName, int start, int length)
    {
        VariableName = variableName;
        Start = start;
        Length = length;
    }

    internal string VariableName { get; }

    internal int Start { get; }

    internal int Length { get; }
}

internal sealed class NodePattern : PatternElement
{
    internal NodePattern(
        string variableName,
        string label,
        IReadOnlyList<PropertyMapItem> properties,
        int start,
        int length)
        : base(variableName, start, length)
    {
        Label = label;
        Properties = properties;
    }

    internal string Label { get; }

    internal IReadOnlyList<PropertyMapItem> Properties { get; }
}

internal sealed class RelationshipPattern : PatternElement
{
    internal RelationshipPattern(
        string variableName,
        string typeName,
        RelationshipDirection direction,
        IReadOnlyList<PropertyMapItem> properties,
        int start,
        int length)
        : base(variableName, start, length)
    {
        TypeName = typeName;
        Direction = direction;
        Properties = properties;
    }

    internal string TypeName { get; }

    internal RelationshipDirection Direction { get; }

    internal IReadOnlyList<PropertyMapItem> Properties { get; }
}

internal abstract class Expression
{
    protected Expression(int start, int length)
    {
        Start = start;
        Length = length;
    }

    internal int Start { get; }

    internal int Length { get; }
}

internal sealed class VariableExpression : Expression
{
    internal VariableExpression(string name, int start, int length)
        : base(start, length)
    {
        Name = name;
    }

    internal string Name { get; }
}

internal sealed class PropertyExpression : Expression
{
    internal PropertyExpression(string variableName, string propertyName, int start, int length)
        : base(start, length)
    {
        VariableName = variableName;
        PropertyName = propertyName;
    }

    internal string VariableName { get; }

    internal string PropertyName { get; }
}

internal sealed class LiteralExpression : Expression
{
    private LiteralExpression(
        LiteralKind kind,
        string textValue,
        double numberValue,
        bool booleanValue,
        int start,
        int length)
        : base(start, length)
    {
        Kind = kind;
        TextValue = textValue;
        NumberValue = numberValue;
        BooleanValue = booleanValue;
    }

    internal LiteralKind Kind { get; }

    internal string TextValue { get; }

    internal double NumberValue { get; }

    internal bool BooleanValue { get; }

    internal static LiteralExpression FromText(string value, int start, int length)
    {
        return new LiteralExpression(LiteralKind.Text, value, 0, false, start, length);
    }

    internal static LiteralExpression FromNumber(double value, int start, int length)
    {
        return new LiteralExpression(LiteralKind.Number, string.Empty, value, false, start, length);
    }

    internal static LiteralExpression FromBoolean(bool value, int start, int length)
    {
        return new LiteralExpression(LiteralKind.Boolean, string.Empty, 0, value, start, length);
    }

    internal static LiteralExpression FromNull(int start, int length)
    {
        return new LiteralExpression(LiteralKind.Null, string.Empty, 0, false, start, length);
    }
}

internal sealed class FunctionExpression : Expression
{
    internal FunctionExpression(
        string name,
        IReadOnlyList<Expression> arguments,
        bool isStarArgument,
        bool isDistinct,
        int start,
        int length)
        : base(start, length)
    {
        Name = name;
        Arguments = arguments;
        IsStarArgument = isStarArgument;
        IsDistinct = isDistinct;
    }

    internal string Name { get; }

    internal IReadOnlyList<Expression> Arguments { get; }

    internal bool IsStarArgument { get; }

    internal bool IsDistinct { get; }
}

internal sealed class BinaryExpression : Expression
{
    internal BinaryExpression(Expression left, string @operator, Expression right)
        : base(left.Start, (right.Start + right.Length) - left.Start)
    {
        Left = left;
        Operator = @operator;
        Right = right;
    }

    internal Expression Left { get; }

    internal string Operator { get; }

    internal Expression Right { get; }
}

internal sealed class UnaryExpression : Expression
{
    internal UnaryExpression(Expression operand)
        : base(operand.Start, operand.Length)
    {
        Operand = operand;
    }

    internal Expression Operand { get; }
}

internal sealed class ComparisonExpression : Expression
{
    internal ComparisonExpression(Expression left, string @operator, Expression right)
        : base(left.Start, (right.Start + right.Length) - left.Start)
    {
        Left = left;
        Operator = @operator;
        Right = right;
    }

    internal Expression Left { get; }

    internal string Operator { get; }

    internal Expression Right { get; }
}

internal sealed class NullTestExpression : Expression
{
    internal NullTestExpression(Expression operand, bool isNegated)
        : base(operand.Start, operand.Length)
    {
        Operand = operand;
        IsNegated = isNegated;
    }

    internal Expression Operand { get; }

    internal bool IsNegated { get; }
}

internal sealed class InExpression : Expression
{
    internal InExpression(Expression operand, IReadOnlyList<Expression> values)
        : base(operand.Start, operand.Length)
    {
        Operand = operand;
        Values = values;
    }

    internal Expression Operand { get; }

    internal IReadOnlyList<Expression> Values { get; }
}

[SuppressMessage(
    "Major Code Smell",
    "S3871:Exception types should be public",
    Justification = "Parser failures are translated to diagnostics inside the query engine and never cross its boundary.")]
internal sealed class GraphCypherParseException : Exception
{
    internal GraphCypherParseException(int start, int length, string message)
        : base(message)
    {
        Start = Math.Max(0, start);
        Length = Math.Max(1, length);
    }

    internal int Start { get; }

    internal int Length { get; }
}

internal sealed record PropertyMapItem(
    string Name,
    LiteralExpression Value,
    int Start,
    int Length);

internal sealed record ReturnItem(Expression Expression, string? Alias, string ExpressionText);

internal sealed record OrderItem(Expression Expression, bool IsDescending);

#pragma warning restore SA1402, SA1600, SA1602, SA1649
