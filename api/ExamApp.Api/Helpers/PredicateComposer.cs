using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace ExamApp.Api.Helpers;

/// <summary>
/// EF'e çevrilebilir predicate'leri (<c>Expression&lt;Func&lt;T, bool&gt;&gt;</c>) OR/AND ile birleştirir: her ifadenin
/// parametresi ortak tek parametreyle değiştirilir (Invoke yok — EF çevirisi düz SQL koşulu üretir). Paylaşılan bir kuralı
/// (ör. <c>WorksheetCommentPinRule.HoldsOn</c>) birden fazla sorgu koşulunun içine aynı ağaçla gömmek için.
/// </summary>
public static class PredicateComposer
{
    public static Expression<Func<T, bool>> Or<T>(params Expression<Func<T, bool>>[] predicates) =>
        Combine(predicates, Expression.OrElse);

    public static Expression<Func<T, bool>> And<T>(params Expression<Func<T, bool>>[] predicates) =>
        Combine(predicates, Expression.AndAlso);

    /// <summary>
    /// <paramref name="lambda"/>'nın gövdesinde parametreleri sırasıyla <paramref name="arguments"/> ile değiştirir (lambda'yı
    /// "satır içi çağırır"). Sonuç başka bir ifade ağacına gömülebilir.
    /// </summary>
    public static Expression Inline(LambdaExpression lambda, params Expression[] arguments)
    {
        ArgumentNullException.ThrowIfNull(lambda);
        if (lambda.Parameters.Count != arguments.Length)
            throw new ArgumentException("Parametre ve argüman sayısı eşleşmiyor.", nameof(arguments));

        var map = new Dictionary<ParameterExpression, Expression>();
        for (var i = 0; i < arguments.Length; i++)
            map[lambda.Parameters[i]] = arguments[i];
        return new ParameterReplacer(map).Visit(lambda.Body);
    }

    private static Expression<Func<T, bool>> Combine<T>(
        IReadOnlyList<Expression<Func<T, bool>>> predicates, Func<Expression, Expression, BinaryExpression> join)
    {
        if (predicates.Count == 0)
            throw new ArgumentException("En az bir koşul gerekli.", nameof(predicates));

        var parameter = Expression.Parameter(typeof(T), "c");
        var body = predicates.Select(p => Inline(p, parameter)).Aggregate((left, right) => join(left, right));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class ParameterReplacer(IReadOnlyDictionary<ParameterExpression, Expression> map) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            map.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
    }
}
