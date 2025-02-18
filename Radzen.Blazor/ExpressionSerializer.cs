using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

/// <summary>
/// Serializes LINQ Expression Trees into C# string representations.
/// </summary>
public class ExpressionSerializer : ExpressionVisitor
{
    private readonly StringBuilder _sb = new StringBuilder();

    /// <summary>
    /// Serializes a given LINQ Expression into a C# string.
    /// </summary>
    /// <param name="expression">The expression to serialize.</param>
    /// <returns>A string representation of the expression.</returns>
    public string Serialize(Expression expression)
    {
        _sb.Clear();
        Visit(expression);
        return _sb.ToString();
    }

    /// <inheritdoc/>
    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        if (node.Parameters.Count > 1)
        {
            _sb.Append("(");
            for (int i = 0; i < node.Parameters.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                _sb.Append(node.Parameters[i].Name);
            }
            _sb.Append(") => ");
        }
        else
        {
            _sb.Append(node.Parameters[0].Name);
            _sb.Append(" => ");
        }
        Visit(node.Body);
        return node;
    }

    /// <inheritdoc/>
    protected override Expression VisitParameter(ParameterExpression node)
    {
        _sb.Append(node.Name);
        return node;
    }

    /// <inheritdoc/>
    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression != null)
        {
            Visit(node.Expression);
            _sb.Append($".{node.Member.Name}");
        }
        else
        {
            _sb.Append(node.Member.Name);
        }
        return node;
    }

    /// <inheritdoc/>
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.IsStatic && node.Arguments.Count > 0 &&
            (node.Method.DeclaringType == typeof(Enumerable) || 
                node.Method.DeclaringType == typeof(Queryable)))
        {
            Visit(node.Arguments[0]);
            _sb.Append($".{node.Method.Name}(");

            for (int i = 1; i < node.Arguments.Count; i++) 
            {
                if (i > 1) _sb.Append(", ");

                if (node.Arguments[i] is NewArrayExpression arrayExpr)
                {
                    VisitNewArray(arrayExpr);
                }
                else
                {
                    Visit(node.Arguments[i]);
                }
            }

            _sb.Append(")");
        }
        else if (node.Method.IsStatic)
        {
            _sb.Append($"{node.Method.DeclaringType.Name}.{node.Method.Name}(");

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                Visit(node.Arguments[i]);
            }

            _sb.Append(")");
        }
        else
        {
            if (node.Object != null)
            {
                Visit(node.Object);
                _sb.Append($".{node.Method.Name}(");
            }
            else
            {
                _sb.Append($"{node.Method.Name}(");
            }

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                Visit(node.Arguments[i]);
            }

            _sb.Append(")");
        }

        return node;
    }

    /// <inheritdoc/>
    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            _sb.Append("(!");
            Visit(node.Operand);
            _sb.Append(")");
        }
        else if (node.NodeType == ExpressionType.Convert) 
        {
            Visit(node.Operand);
        }
        else
        {
            _sb.Append(node.NodeType switch
            {
                ExpressionType.Negate => "-",
                ExpressionType.UnaryPlus => "+",
                _ => throw new NotSupportedException($"Unsupported unary operator: {node.NodeType}")
            });
            Visit(node.Operand);
        }
        return node;
    }

    /// <inheritdoc/>
    protected override Expression VisitConstant(ConstantExpression node)
    {
        _sb.Append(FormatValue(node.Value));
        return node;
    }

    private string FormatValue(object value)
    {
        if (value == null)
            return "null";

        return value switch
        {
            string str => $"\"{str}\"",
            char c => $"'{c}'",
            bool b => b.ToString().ToLower(),
            DateTime dt => $"DateTime.Parse(\"{dt:yyyy-MM-ddTHH:mm:ss.fffZ}\")",
            DateOnly dateOnly => $"DateOnly.Parse(\"{dateOnly:yyyy-MM-dd}\")",
            TimeOnly timeOnly => $"TimeOnly.Parse(\"{timeOnly:HH:mm:ss}\")",
            Guid guid => $"Guid.Parse(\"{guid:D}\")",
            IEnumerable enumerable when value is not string => FormatEnumerable(enumerable),
            _ => value.GetType().IsEnum
                ? $"({value.GetType().FullName.Replace("+", ".")})" + Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())).ToString()
                : value.ToString()
        };
    }

    private string FormatEnumerable(IEnumerable enumerable)
    {
        var items = enumerable.Cast<object>().Select(FormatValue);
        return $"new [] {{ {string.Join(", ", items)} }}";
    }


    /// <inheritdoc/>
    protected override Expression VisitNewArray(NewArrayExpression node)
    {
        bool needsParentheses = node.NodeType == ExpressionType.NewArrayInit &&
                                (node.Expressions.Count > 1 || node.Expressions[0].NodeType != ExpressionType.Constant);

        if (needsParentheses) _sb.Append("(");

        _sb.Append("new [] { ");
        bool first = true;
        foreach (var expr in node.Expressions)
        {
            if (!first) _sb.Append(", ");
            first = false;
            Visit(expr);
        }
        _sb.Append(" }");

        if (needsParentheses) _sb.Append(")");

        return node;
    }

    /// <inheritdoc/>
    protected override Expression VisitBinary(BinaryExpression node)
    {
        _sb.Append("(");
        Visit(node.Left);
        _sb.Append($" {GetOperator(node.NodeType)} ");
        Visit(node.Right);
        _sb.Append(")");
        return node;
    }

    /// <inheritdoc/>
    protected override Expression VisitConditional(ConditionalExpression node)
    {
        _sb.Append("(");
        Visit(node.Test);
        _sb.Append(" ? ");
        Visit(node.IfTrue);
        _sb.Append(" : ");
        Visit(node.IfFalse);
        _sb.Append(")");
        return node;
    }

    /// <summary>
    /// Maps an ExpressionType to its corresponding C# operator.
    /// </summary>
    /// <param name="type">The ExpressionType to map.</param>
    /// <returns>A string representation of the corresponding C# operator.</returns>
    private static string GetOperator(ExpressionType type)
    {
        return type switch
        {
            ExpressionType.Add => "+",
            ExpressionType.Subtract => "-",
            ExpressionType.Multiply => "*",
            ExpressionType.Divide => "/",
            ExpressionType.AndAlso => "&&",
            ExpressionType.OrElse => "||",
            ExpressionType.Equal => "==",
            ExpressionType.NotEqual => "!=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.Coalesce => "??",
            _ => throw new NotSupportedException($"Unsupported operator: {type}")
        };
    }
}
