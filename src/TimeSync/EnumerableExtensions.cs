namespace TimeSync;

internal static class EnumerableExtensions
{
    internal static IEnumerable<TResult> FullOuterJoin<TLeft, TRight, TKey, TResult>(
        this IEnumerable<TLeft> left,
        IEnumerable<TRight> right,
        Func<TLeft, TKey> selectKeyLeft,
        Func<TRight, TKey> selectKeyRight,
        Func<TLeft, TRight, TKey, TResult> projection,
        TLeft? defaultLeft = default,
        TRight? defaultRight = default,
        IEqualityComparer<TKey>? cmp = null)
    {
        cmp ??= EqualityComparer<TKey>.Default;

        var leftLookup = left.ToLookup(selectKeyLeft, cmp);
        var rightLookup = right.ToLookup(selectKeyRight, cmp);

        var keys = new HashSet<TKey>(leftLookup.Select(p => p.Key), cmp);
        keys.UnionWith(rightLookup.Select(p => p.Key));

        var join = from key in keys
                   from leftItem in leftLookup[key].DefaultIfEmpty(defaultLeft)
                   from rightItem in rightLookup[key].DefaultIfEmpty(defaultRight)
                   select projection(leftItem, rightItem, key);

        return join;
    }
}
