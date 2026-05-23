using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using ParquetLoader.Configuration;

namespace ParquetLoader.ParquetLoads;

public abstract class ParquetLoader<T>
    where T : new()
{
    public abstract string DatasetName { get; }

    protected ParquetLoaderConfig Config { get; }

    protected string BaseParquetPath => Config.BaseParquetPath;

    protected virtual string DefaultFolder => Path.Combine(BaseParquetPath, DatasetName);

    protected abstract IReadOnlyDictionary<string, string> ColumnMap { get; }

    private readonly ILogger _logger;

    protected ParquetLoader(
        ParquetLoaderConfig config,
        ILogger logger)
    {
        Config = config;
        _logger = logger;
    }

    public List<T> Load(
        string? parquetFolder,
        int limit,
        Expression<Func<T, bool>>? predicate = null)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be at least 1.");
        }

        var whereClause = predicate is null
            ? null
            : TranslatePredicate(predicate);

        return LoadData(parquetFolder ?? DefaultFolder, whereClause, limit);
    }

    protected List<T> LoadData(
        string parquetFolder,
        string? whereClause,
        int limit)
    {
        var fullPath = Path.GetFullPath(parquetFolder);
        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException($"Parquet folder does not exist: {fullPath}", nameof(parquetFolder));
        }

        if (!Directory.EnumerateFiles(fullPath, "*.parquet", SearchOption.AllDirectories).Any())
        {
            throw new ArgumentException($"No .parquet files found under: {fullPath}", nameof(parquetFolder));
        }

        var glob = Path.Combine(fullPath, "**", "*.parquet");
        var sql =
            $"""
             SELECT *
             FROM read_parquet({SqlString(glob)}, hive_partitioning = true)
             """;

        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $"{Environment.NewLine}WHERE {whereClause}";
        }

        sql += $"{Environment.NewLine}LIMIT {limit}";
        _logger.LogDebug("DuckDB SQL:{NewLine}{Sql}", Environment.NewLine, sql);

        using var connection = new DuckDBConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        return MapRows(reader);
    }

    protected string TranslatePredicate(
        Expression<Func<T, bool>> predicate)
    {
        return TranslateExpression(predicate.Body);
    }

    private List<T> MapRows(DuckDBDataReader reader)
    {
        var rows = new List<T>();
        var ordinals = Enumerable
            .Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, index => index, StringComparer.OrdinalIgnoreCase);

        var properties = typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanWrite)
            .ToArray();

        while (reader.Read())
        {
            var row = new T();
            foreach (var property in properties)
            {
                if (!ColumnMap.TryGetValue(property.Name, out var columnName) ||
                    !ordinals.TryGetValue(columnName, out var ordinal))
                {
                    continue;
                }

                var value = reader.GetValue(ordinal);
                if (value is DBNull)
                {
                    property.SetValue(row, null);
                    continue;
                }

                property.SetValue(row, ConvertValue(value, property.PropertyType));
            }

            rows.Add(row);
        }

        return rows;
    }

    private string TranslateExpression(Expression expression)
    {
        return expression.NodeType switch
        {
            ExpressionType.AndAlso => TranslateLogical((BinaryExpression)expression, "AND"),
            ExpressionType.OrElse => TranslateLogical((BinaryExpression)expression, "OR"),
            ExpressionType.Not => TranslateNot((UnaryExpression)expression),
            ExpressionType.Equal => TranslateComparison((BinaryExpression)expression, "="),
            ExpressionType.NotEqual => TranslateComparison((BinaryExpression)expression, "<>"),
            ExpressionType.GreaterThan => TranslateComparison((BinaryExpression)expression, ">"),
            ExpressionType.GreaterThanOrEqual => TranslateComparison((BinaryExpression)expression, ">="),
            ExpressionType.LessThan => TranslateComparison((BinaryExpression)expression, "<"),
            ExpressionType.LessThanOrEqual => TranslateComparison((BinaryExpression)expression, "<="),
            ExpressionType.Call => TranslateMethodCall((MethodCallExpression)expression),
            _ => throw new NotSupportedException($"Unsupported predicate expression: {expression.NodeType}")
        };
    }

    private string TranslateLogical(BinaryExpression expression, string sqlOperator)
    {
        return $"({TranslateExpression(expression.Left)} {sqlOperator} {TranslateExpression(expression.Right)})";
    }

    private string TranslateNot(UnaryExpression expression)
    {
        return $"NOT ({TranslateExpression(expression.Operand)})";
    }

    private string TranslateComparison(BinaryExpression expression, string sqlOperator)
    {
        var leftColumn = TryTranslateColumn(expression.Left);
        var rightColumn = TryTranslateColumn(expression.Right);
        var leftIsNull = IsNullConstant(expression.Left);
        var rightIsNull = IsNullConstant(expression.Right);

        if (leftColumn is not null && rightIsNull)
        {
            return sqlOperator == "="
                ? $"{leftColumn} IS NULL"
                : $"{leftColumn} IS NOT NULL";
        }

        if (rightColumn is not null && leftIsNull)
        {
            return sqlOperator == "="
                ? $"{rightColumn} IS NULL"
                : $"{rightColumn} IS NOT NULL";
        }

        if (leftColumn is not null)
        {
            return $"{leftColumn} {sqlOperator} {SqlLiteral(Evaluate(expression.Right))}";
        }

        if (rightColumn is not null)
        {
            return $"{SqlLiteral(Evaluate(expression.Left))} {sqlOperator} {rightColumn}";
        }

        throw new NotSupportedException("Comparison must include a DTO property.");
    }

    private string TranslateMethodCall(MethodCallExpression expression)
    {
        if (TryTranslateCollectionContains(expression, out var sql))
        {
            return sql;
        }

        throw new NotSupportedException($"Unsupported method call in predicate: {expression.Method.Name}");
    }

    private bool TryTranslateCollectionContains(MethodCallExpression expression, out string sql)
    {
        sql = string.Empty;

        Expression? collectionExpression;
        Expression? valueExpression;

        if (expression.Method.Name == nameof(Enumerable.Contains) &&
            expression.Arguments.Count == 2)
        {
            collectionExpression = expression.Arguments[0];
            valueExpression = expression.Arguments[1];
        }
        else if (expression.Method.Name == nameof(List<object>.Contains) &&
                 expression.Object is not null &&
                 expression.Arguments.Count == 1 &&
                 expression.Object.Type != typeof(string))
        {
            collectionExpression = expression.Object;
            valueExpression = expression.Arguments[0];
        }
        else
        {
            return false;
        }

        var column = TryTranslateColumn(valueExpression);
        if (column is null)
        {
            return false;
        }

        var collectionValue = Evaluate(collectionExpression);
        if (collectionValue is string || collectionValue is not IEnumerable values)
        {
            return false;
        }

        var literals = values.Cast<object?>().Select(SqlLiteral).ToArray();
        sql = literals.Length == 0
            ? "1 = 0"
            : $"{column} IN ({string.Join(", ", literals)})";
        return true;
    }

    private string? TryTranslateColumn(Expression expression)
    {
        expression = UnwrapConvert(expression);

        if (expression is not MemberExpression memberExpression ||
            memberExpression.Expression is not ParameterExpression)
        {
            return null;
        }

        if (!ColumnMap.TryGetValue(memberExpression.Member.Name, out var columnName))
        {
            throw new NotSupportedException($"No Parquet column mapping for DTO property: {memberExpression.Member.Name}");
        }

        return QuoteIdentifier(columnName);
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            expression = ((UnaryExpression)expression).Operand;
        }

        return expression;
    }

    private static object? Evaluate(Expression expression)
    {
        expression = UnwrapConvert(expression);

        if (expression is ConstantExpression constantExpression)
        {
            return constantExpression.Value;
        }

        var lambda = Expression.Lambda<Func<object?>>(
            Expression.Convert(expression, typeof(object)));
        return lambda.Compile().Invoke();
    }

    private static bool IsNullConstant(Expression expression)
    {
        return UnwrapConvert(expression) is ConstantExpression { Value: null };
    }

    private static object? ConvertValue(object value, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType.IsEnum)
        {
            return Enum.ToObject(underlyingType, value);
        }

        return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
    }

    private static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string SqlString(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static string SqlLiteral(object? value)
    {
        return value switch
        {
            null => "NULL",
            string text => SqlString(text),
            DateTime dateTime => $"TIMESTAMP {SqlString(dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}",
            DateTimeOffset dateTimeOffset => $"TIMESTAMP {SqlString(dateTimeOffset.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}",
            bool boolean => boolean ? "TRUE" : "FALSE",
            Enum enumValue => Convert.ToInt64(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => SqlString(value.ToString() ?? string.Empty)
        };
    }
}
