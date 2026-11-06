using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlInvestigator.Connections;

namespace SqlInvestigator.Queries;

/// <summary>The outcome of validation (ValidationVerdict), with the base objects a query reads when it is accepted.</summary>
internal sealed record ValidationResult(bool Accepted, IReadOnlyList<string> Reasons, IReadOnlyList<string> Objects)
{
    public static ValidationResult Reject(params string[] reasons) => new(false, reasons, []);
}

/// <summary>
/// SelectOnlyAllowlist: a statement executes only if it parses as exactly one bounded SELECT accepted by a
/// fail-closed allowlist. This is an ALLOWLIST: every node of the parse tree must be of a type named here, every
/// function a name named here, every table a two-part name in the alias's approved scope and every column an approved
/// one. Anything not recognised is rejected; nothing is assumed safe because it parses.
/// </summary>
internal static class SqlValidator
{
    /// <summary>Parse-tree node types a bounded SELECT may contain. Everything else is rejected.</summary>
    private static readonly HashSet<string> AllowedNodes = new(StringComparer.Ordinal)
    {
        nameof(TSqlScript), nameof(TSqlBatch), nameof(SelectStatement), nameof(WithCtesAndXmlNamespaces), nameof(CommonTableExpression),
        nameof(QuerySpecification), nameof(QueryParenthesisExpression), nameof(BinaryQueryExpression),
        nameof(SelectScalarExpression), nameof(SelectStarExpression), nameof(FromClause), nameof(WhereClause),
        nameof(GroupByClause), nameof(ExpressionGroupingSpecification), nameof(HavingClause), nameof(OrderByClause),
        nameof(ExpressionWithSortOrder), nameof(TopRowFilter), nameof(OffsetClause),
        nameof(NamedTableReference), nameof(SchemaObjectName), nameof(Identifier), nameof(MultiPartIdentifier),
        nameof(QualifiedJoin), nameof(UnqualifiedJoin), nameof(JoinParenthesisTableReference), nameof(QueryDerivedTable),
        nameof(ColumnReferenceExpression), nameof(IdentifierOrValueExpression),
        nameof(IntegerLiteral), nameof(StringLiteral), nameof(NumericLiteral), nameof(RealLiteral), nameof(MoneyLiteral),
        nameof(NullLiteral), nameof(MaxLiteral), nameof(BinaryLiteral),
        nameof(BooleanComparisonExpression), nameof(BooleanBinaryExpression), nameof(BooleanNotExpression),
        nameof(BooleanParenthesisExpression), nameof(BooleanIsNullExpression), nameof(BooleanTernaryExpression),
        nameof(InPredicate), nameof(LikePredicate), nameof(ExistsPredicate), nameof(SubqueryComparisonPredicate), nameof(DistinctPredicate),
        nameof(ScalarSubquery), nameof(BinaryExpression), nameof(UnaryExpression), nameof(ParenthesisExpression),
        nameof(FunctionCall), nameof(CastCall), nameof(ConvertCall), nameof(TryCastCall), nameof(TryConvertCall),
        nameof(CoalesceExpression), nameof(NullIfExpression), nameof(IIfCall), nameof(LeftFunctionCall), nameof(RightFunctionCall),
        nameof(SearchedCaseExpression), nameof(SimpleCaseExpression), nameof(SearchedWhenClause), nameof(SimpleWhenClause),
        nameof(OverClause), nameof(WindowFrameClause), nameof(WindowDelimiter), nameof(WithinGroupClause),
        nameof(SqlDataTypeReference),
    };

    /// <summary>Built-in functions a query may call. No user-defined, metadata, system or rowset function.</summary>
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "COUNT_BIG", "SUM", "MIN", "MAX", "AVG", "STDEV", "STDEVP", "VAR", "VARP", "STRING_AGG",
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
        "ABS", "ROUND", "CEILING", "FLOOR", "POWER", "SQRT", "SQUARE", "SIGN", "LOG", "LOG10", "EXP", "PI",
        "LEN", "DATALENGTH", "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM", "SUBSTRING", "REPLACE", "CHARINDEX", "PATINDEX",
        "CONCAT", "CONCAT_WS", "STUFF", "REVERSE", "REPLICATE", "SPACE", "FORMAT", "ISNULL", "CHOOSE", "GREATEST", "LEAST",
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATETRUNC", "YEAR", "MONTH", "DAY", "EOMONTH",
        "DATEFROMPARTS", "DATETIMEFROMPARTS", "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME", "SYSDATETIMEOFFSET",
    };

    /// <summary>Functions whose first argument is a date part keyword that the parser reads as a column name.</summary>
    private static readonly HashSet<string> DatePartFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATETRUNC",
    };

    private static readonly HashSet<string> DateParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "yy", "yyyy", "quarter", "qq", "q", "month", "mm", "m", "dayofyear", "dy", "y", "day", "dd", "d",
        "week", "wk", "ww", "weekday", "dw", "hour", "hh", "minute", "mi", "n", "second", "ss", "s",
        "millisecond", "ms", "microsecond", "mcs", "nanosecond", "ns", "iso_week", "isowk", "isoww",
    };

    public static ValidationResult Validate(string? sql, ConnectionEntry entry, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return ValidationResult.Reject("The statement is empty.");
        }

        if (sql.Length > maxLength)
        {
            return ValidationResult.Reject($"The statement is {sql.Length} characters; the limit is {maxLength}.");
        }

        // The server session runs with QUOTED_IDENTIFIER ON, and so does this parse.
        var tree = new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(sql), out var errors);
        if (errors.Count > 0)
        {
            return ValidationResult.Reject(errors.Take(3).Select(e => $"The statement does not parse as T-SQL (line {e.Line}, column {e.Column}): {e.Message}").ToArray());
        }

        // Fail closed on PARSER AMBIGUITY: a statement whose meaning depends on a session option (for example
        // "OrderId" is a column with QUOTED_IDENTIFIER ON and a string literal with it OFF) is not accepted.
        var other = new TSql160Parser(initialQuotedIdentifiers: false).Parse(new StringReader(sql), out var otherErrors);
        if (otherErrors.Count > 0 || Script(tree) != Script(other))
        {
            return ValidationResult.Reject("The statement is ambiguous: it means different things depending on the QUOTED_IDENTIFIER setting. Use [brackets] for identifiers and 'single quotes' for strings.");
        }

        if (tree is not TSqlScript script || script.Batches.Count != 1)
        {
            return ValidationResult.Reject("Exactly one batch is allowed; the statement contains a batch separator (GO) or no statement at all.");
        }

        var statements = script.Batches[0].Statements;
        if (statements.Count != 1)
        {
            return ValidationResult.Reject($"Exactly one statement is allowed; found {statements.Count}.");
        }

        if (statements[0] is not SelectStatement select || statements[0].GetType() != typeof(SelectStatement))
        {
            return ValidationResult.Reject($"Only a SELECT statement is allowed; found {Describe(statements[0])}.");
        }

        if (select.Into is not null)
        {
            return ValidationResult.Reject("SELECT INTO creates a table and is not allowed.");
        }

        var reasons = new List<string>();

        // Every node must be of an allowed type.
        var nodes = new NodeCollector();
        tree.Accept(nodes);
        foreach (var type in nodes.Types.Where(t => !AllowedNodes.Contains(t)).Distinct().Order(StringComparer.Ordinal))
        {
            reasons.Add($"The construct {Explain(type)} is not allowed in an investigation query.");
        }

        if (reasons.Count > 0)
        {
            return new ValidationResult(false, reasons, []);
        }

        // Every table is a two-part name in scope, every column an approved one, every function an allowed built-in.
        var walker = new ScopeWalker(entry);
        tree.Accept(walker);
        reasons.AddRange(walker.Reasons.Distinct());
        return reasons.Count > 0
            ? new ValidationResult(false, reasons, [])
            : new ValidationResult(true, [], walker.Objects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string Script(TSqlFragment fragment)
    {
        new Sql160ScriptGenerator(new SqlScriptGeneratorOptions { KeywordCasing = KeywordCasing.Uppercase }).GenerateScript(fragment, out var text);
        return text;
    }

    private static string Describe(TSqlStatement statement) => statement.GetType().Name switch
    {
        nameof(InsertStatement) => "INSERT",
        nameof(UpdateStatement) => "UPDATE",
        nameof(DeleteStatement) => "DELETE",
        nameof(MergeStatement) => "MERGE",
        nameof(ExecuteStatement) => "EXECUTE",
        nameof(TruncateTableStatement) => "TRUNCATE TABLE",
        nameof(WaitForStatement) => "WAITFOR",
        nameof(DeclareVariableStatement) => "DECLARE",
        nameof(BulkInsertStatement) => "BULK INSERT",
        nameof(ShutdownStatement) => "SHUTDOWN",
        var name when name.StartsWith("Create", StringComparison.Ordinal) => "a CREATE statement (DDL)",
        var name when name.StartsWith("Alter", StringComparison.Ordinal) => "an ALTER statement (DDL)",
        var name when name.StartsWith("Drop", StringComparison.Ordinal) => "a DROP statement (DDL)",
        var name when name.StartsWith("Grant", StringComparison.Ordinal) || name.StartsWith("Revoke", StringComparison.Ordinal) || name.StartsWith("Deny", StringComparison.Ordinal) => "a permission statement",
        var name => name.Replace("Statement", string.Empty, StringComparison.Ordinal),
    };

    private static string Explain(string type) => type switch
    {
        nameof(VariableReference) or nameof(GlobalVariableExpression) => "a variable",
        nameof(OpenRowsetTableReference) or "BulkOpenRowset" or "InternalOpenRowset" => "OPENROWSET",
        nameof(OpenQueryTableReference) => "OPENQUERY (linked server)",
        "AdHocTableReference" => "OPENDATASOURCE (ad hoc linked server)",
        nameof(OpenXmlTableReference) => "OPENXML",
        nameof(OpenJsonTableReference) => "OPENJSON",
        nameof(TableHint) or nameof(IndexTableHint) or nameof(LiteralTableHint) => "a table hint",
        nameof(OptimizerHint) or nameof(LiteralOptimizerHint) or nameof(TableHintsOptimizerHint) => "an OPTION query hint",
        nameof(XmlForClause) or nameof(JsonForClause) or nameof(BrowseForClause) or nameof(ReadOnlyForClause) or nameof(UpdateForClause) => "a FOR clause",
        nameof(SchemaObjectFunctionTableReference) or nameof(GlobalFunctionTableReference) => "a table-valued function",
        nameof(TableSampleClause) => "TABLESAMPLE",
        nameof(ComputeClause) => "COMPUTE",
        nameof(SelectSetVariable) => "a variable assignment",
        _ => type,
    };

    private sealed class NodeCollector : TSqlFragmentVisitor
    {
        public List<string> Types { get; } = [];

        public override void Visit(TSqlFragment node)
        {
            Types.Add(node.GetType().Name);
            base.Visit(node);
        }
    }

    /// <summary>A FROM source as a query sees it: an approved base object, or a derived table / CTE (checked where it is defined).</summary>
    private sealed record Source(IReadOnlyList<string> Names, ScopeEntry? Base);

    private sealed class Frame
    {
        public List<Source> Sources { get; } = [];
        public HashSet<string> OutputNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool InOrderBy { get; set; }
    }

    /// <summary>
    /// Checks names against the scope. A column must be allowed by every approved base object it could resolve to
    /// (the query's own FROM sources and every enclosing query's, because T-SQL resolves unqualified and correlated
    /// names outward); a qualified column is checked against the source its qualifier names.
    /// </summary>
    private sealed class ScopeWalker(ConnectionEntry entry) : TSqlFragmentVisitor
    {
        private readonly Stack<Frame> frames = new();
        private readonly HashSet<string> ctes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<TSqlFragment> exempt = new(ReferenceEqualityComparer.Instance);

        public List<string> Reasons { get; } = [];
        public List<string> Objects { get; } = [];

        public override void ExplicitVisit(SelectStatement node)
        {
            foreach (var cte in node.WithCtesAndXmlNamespaces?.CommonTableExpressions ?? [])
            {
                ctes.Add(cte.ExpressionName.Value);
            }

            if (node.WithCtesAndXmlNamespaces?.XmlNamespaces is not null)
            {
                Reasons.Add("WITH XMLNAMESPACES is not allowed.");
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            var frame = new Frame();
            foreach (var reference in node.FromClause?.TableReferences ?? [])
            {
                CollectSources(reference, frame.Sources);
            }

            foreach (var element in node.SelectElements)
            {
                if (element is SelectScalarExpression { ColumnName.Value: { } alias })
                {
                    frame.OutputNames.Add(alias);
                }
            }

            frames.Push(frame);
            base.ExplicitVisit(node);
            frames.Pop();
        }

        public override void ExplicitVisit(OrderByClause node)
        {
            if (frames.TryPeek(out var frame))
            {
                frame.InOrderBy = true;
                base.ExplicitVisit(node);
                frame.InOrderBy = false;
            }
            else
            {
                base.ExplicitVisit(node);
            }
        }

        public override void ExplicitVisit(NamedTableReference node)
        {
            var name = node.SchemaObject;
            if (name.ServerIdentifier is not null)
            {
                Reasons.Add($"{Text(name)} names a linked server; linked servers are not allowed.");
            }
            else if (name.DatabaseIdentifier is not null)
            {
                Reasons.Add($"{Text(name)} names another database; each query reads one database, through one alias.");
            }
            else if (name.SchemaIdentifier is null)
            {
                if (!ctes.Contains(name.BaseIdentifier.Value))
                {
                    Reasons.Add($"{Text(name)} is not schema-qualified; name approved objects as schema.object (for example dbo.Orders).");
                }
            }
            else if (entry.FindScope(name.SchemaIdentifier.Value, name.BaseIdentifier.Value) is { } scope)
            {
                Objects.Add(scope.QualifiedName);
            }
            else
            {
                Reasons.Add($"{Text(name)} is outside the approved scope of alias \"{entry.Alias}\" ({string.Join(", ", entry.Scope.Select(s => s.QualifiedName))}).");
            }

            if (node.TemporalClause is not null)
            {
                Reasons.Add("FOR SYSTEM_TIME is not allowed.");
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(FunctionCall node)
        {
            var name = node.FunctionName.Value;
            if (node.CallTarget is not null)
            {
                Reasons.Add($"The function {name} is called on a target (a schema, column or type); only built-in functions are allowed.");
            }
            else if (!AllowedFunctions.Contains(name))
            {
                Reasons.Add($"The function {name} is not on the list of allowed built-in functions.");
            }

            if (DatePartFunctions.Contains(name) && node.Parameters.Count > 0
                && node.Parameters[0] is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [var part] } datePart
                && DateParts.Contains(part.Value))
            {
                exempt.Add(datePart);
            }

            if ((name.Equals("COUNT", StringComparison.OrdinalIgnoreCase) || name.Equals("COUNT_BIG", StringComparison.OrdinalIgnoreCase))
                && node.Parameters is [ColumnReferenceExpression { ColumnType: ColumnType.Wildcard, MultiPartIdentifier: null } star])
            {
                exempt.Add(star);
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (!exempt.Contains(node))
            {
                CheckColumn(node);
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SelectStarExpression node)
        {
            var qualifier = node.Qualifier?.Identifiers.Select(i => i.Value).ToList();
            if (qualifier is null or { Count: 0 })
            {
                var restricted = CurrentBases().Where(s => s.Columns is not null).ToList();
                if (restricted.Count > 0)
                {
                    Reasons.Add($"SELECT * reads every column, but only some columns of {string.Join(", ", restricted.Select(s => s.QualifiedName))} are approved; list the approved columns.");
                }
            }
            else
            {
                var source = Resolve(qualifier);
                if (source is null)
                {
                    Reasons.Add($"{string.Join(".", qualifier)}.* does not name a table of this query.");
                }
                else if (source.Base is { Columns: not null } b)
                {
                    Reasons.Add($"{string.Join(".", qualifier)}.* reads every column, but only some columns of {b.QualifiedName} are approved; list the approved columns.");
                }
            }

            base.ExplicitVisit(node);
        }

        private void CheckColumn(ColumnReferenceExpression node)
        {
            if (node.ColumnType != ColumnType.Regular)
            {
                Reasons.Add(node.ColumnType == ColumnType.Wildcard
                    ? "* is allowed only as COUNT(*) or in the select list."
                    : $"The pseudo-column {node.ColumnType} is not allowed.");
                return;
            }

            var parts = node.MultiPartIdentifier?.Identifiers.Select(i => i.Value).ToList() ?? [];
            if (parts.Count == 0)
            {
                Reasons.Add("A column reference without a name is not allowed.");
                return;
            }

            if (parts.Count > 3)
            {
                Reasons.Add($"{string.Join(".", parts)} names a column in another database or server; each query reads one database.");
                return;
            }

            var column = parts[^1];
            if (parts.Count == 1)
            {
                // An ORDER BY may name an output column of its own query.
                if (frames.TryPeek(out var current) && current.InOrderBy && current.OutputNames.Contains(column))
                {
                    return;
                }

                var forbidding = AllBases().Where(s => !s.AllowsColumn(column)).ToList();
                if (forbidding.Count > 0)
                {
                    Reasons.Add($"Column {column} is not an approved column of {string.Join(", ", forbidding.Select(s => s.QualifiedName))} for alias \"{entry.Alias}\".");
                }

                return;
            }

            var source = Resolve(parts[..^1]);
            if (source is null)
            {
                Reasons.Add($"{string.Join(".", parts)}: {string.Join(".", parts[..^1])} does not name a table of this query.");
            }
            else if (source.Base is { } b && !b.AllowsColumn(column))
            {
                Reasons.Add($"Column {column} is not an approved column of {b.QualifiedName} for alias \"{entry.Alias}\".");
            }
        }

        private Source? Resolve(IReadOnlyList<string> qualifier)
        {
            var name = string.Join(".", qualifier);
            foreach (var frame in frames)
            {
                var matches = frame.Sources.Where(s => s.Names.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 1)
                {
                    return matches[0];
                }

                if (matches.Count > 1)
                {
                    Reasons.Add($"{name} names more than one table of the query; give each table a distinct alias.");
                    return matches[0];
                }
            }

            return null;
        }

        private IEnumerable<ScopeEntry> CurrentBases()
            => frames.TryPeek(out var f) ? f.Sources.Where(s => s.Base is not null).Select(s => s.Base!) : [];

        private IEnumerable<ScopeEntry> AllBases()
            => frames.SelectMany(f => f.Sources).Where(s => s.Base is not null).Select(s => s.Base!);

        private void CollectSources(TableReference reference, List<Source> into)
        {
            switch (reference)
            {
                case NamedTableReference named:
                {
                    var n = named.SchemaObject;
                    var alias = named.Alias?.Value;
                    if (n.SchemaIdentifier is null && n.DatabaseIdentifier is null && n.ServerIdentifier is null && ctes.Contains(n.BaseIdentifier.Value))
                    {
                        into.Add(new Source([alias ?? n.BaseIdentifier.Value], null));
                        break;
                    }

                    var scope = n.SchemaIdentifier is not null && n.DatabaseIdentifier is null && n.ServerIdentifier is null
                        ? entry.FindScope(n.SchemaIdentifier.Value, n.BaseIdentifier.Value)
                        : null;
                    if (scope is null)
                    {
                        break; // refused by ExplicitVisit(NamedTableReference)
                    }

                    // with an alias, T-SQL exposes only the alias; without one, the table and schema.table
                    into.Add(new Source(alias is not null ? [alias] : [n.BaseIdentifier.Value, $"{n.SchemaIdentifier!.Value}.{n.BaseIdentifier.Value}"], scope));
                    break;
                }

                case QueryDerivedTable derived:
                    into.Add(new Source([derived.Alias?.Value ?? string.Empty], null));
                    break;
                case QualifiedJoin join:
                    CollectSources(join.FirstTableReference, into);
                    CollectSources(join.SecondTableReference, into);
                    break;
                case UnqualifiedJoin join:
                    CollectSources(join.FirstTableReference, into);
                    CollectSources(join.SecondTableReference, into);
                    break;
                case JoinParenthesisTableReference parenthesis:
                    CollectSources(parenthesis.Join, into);
                    break;
                default:
                    break; // any other source type was refused by the node allowlist
            }
        }

        private static string Text(SchemaObjectName name) => string.Join(".", name.Identifiers.Select(i => i.Value));
    }
}
