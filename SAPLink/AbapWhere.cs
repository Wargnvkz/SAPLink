using SAPLink.Classes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace SAPLink
{

    internal static class AbapWhere
    {
        public static string Build<T>(Expression<Func<T, bool>> predicate)
            => Visit(predicate.Body);

        static string Visit(Expression e)
        {
            switch (e)
            {
                case BinaryExpression b when b.NodeType == ExpressionType.AndAlso:
                    return $"( {Visit(b.Left)} AND {Visit(b.Right)} )";

                case BinaryExpression b when b.NodeType == ExpressionType.OrElse:
                    return $"( {Visit(b.Left)} OR {Visit(b.Right)} )";

                case BinaryExpression b:   // ==, !=, <, <=, >, >=
                    return $"{FieldName(b.Left)} {Operator(b.NodeType)} {Literal(Value(b.Right))}";

                case MethodCallExpression m when m.Method.Name == "Contains":
                    return BuildIn(m, false);

                case UnaryExpression u when u.NodeType == ExpressionType.Not
                         && u.Operand is MethodCallExpression m2
                         && m2.Method.Name == "Contains":
                    return BuildIn(m2, true);                    // !ids.Contains(w.Field) -> NOT IN
                default:
                    throw new NotSupportedException(
                        $"Expression '{e}' cannot be translated to a WHERE condition");
            }
        }

        // слева должно быть поле параметра лямбды: w.Plant
        static string FieldName(Expression e)
        {
            if (e is MemberExpression m && m.Expression is ParameterExpression)
                return SapNames.Of(m.Member);          // та же функция, что для [SAPGetTable]
            throw new NotSupportedException($"Left side must be a field of the row: {e}");
        }

        static string Operator(ExpressionType t)
        {
            switch (t)
            {
                case ExpressionType.Equal: return "=";
                case ExpressionType.NotEqual: return "<>";
                case ExpressionType.LessThan: return "<";
                case ExpressionType.LessThanOrEqual: return "<=";
                case ExpressionType.GreaterThan: return ">";
                case ExpressionType.GreaterThanOrEqual: return ">=";
                default: throw new NotSupportedException($"Operator {t} is not supported");
            }
        }

        // справа может быть константа или переменная из окружения: w.Plant == plant
        static object Value(Expression e)
        {
            if (e is ConstantExpression c) return c.Value;
            return Expression.Lambda<Func<object>>(Expression.Convert(e, typeof(object)))
                             .Compile()();         // вычисляем выражение, не зависящее от строки
        }
        static string Literal(object v)
        {
            switch (v)
            {
                case null: return "''";
                case bool b: return b ? "'X'" : "' '";
                case DateTime d: return $"'{d:yyyyMMdd}'";
                case TimeSpan t: return $"'{t:hhmmss}'";
                case decimal m: return "'" + m.ToString(CultureInfo.InvariantCulture) + "'";
                case int i: return "'" + i.ToString(CultureInfo.InvariantCulture) + "'";
                default: return "'" + v.ToString().Replace("'", "''") + "'";
            }
        }
        static string BuildIn(MethodCallExpression m, bool not)
        {
            if (m.Method.DeclaringType == typeof(string))
                throw new NotSupportedException(
                    "string.Contains is not supported. Use a list: ids.Contains(w.Field)");

            Expression source, item;
            if (m.Object != null) { source = m.Object; item = m.Arguments[0]; }  // list.Contains(x)
            else { source = m.Arguments[0]; item = m.Arguments[1]; }  // Enumerable.Contains(list, x)

            var values = ((IEnumerable)Value(source)).Cast<object>().ToList();
            if (values.Count == 0)
                throw new ArgumentException(
                    "Contains() on an empty list cannot be translated to IN (). Check the list before calling.");

            var items = string.Join(", ", values.Select(Literal));
            return $"{FieldName(item)} {(not ? "NOT IN" : "IN")} ( {items} )";
        }
    }
    static class SapNames
    {
        public static string Of(MemberInfo m)
        {
            var attrs = m.GetCustomAttributes(typeof(SAPGetTableAttribute), false);
            if (attrs.Length == 0) return m.Name.ToUpperInvariant();
            var attr = attrs[0] as SAPGetTableAttribute;
            var name = !string.IsNullOrEmpty(attr?.FieldName) ? attr.FieldName : m.Name;
            return name.ToUpperInvariant();
        }
    }
}
