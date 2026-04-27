using System;
using System.Collections.Generic;

namespace MG.Pipelines.Attribute;

/// <summary>
/// An <see cref="IEqualityComparer{T}"/> built from a user-supplied equality lambda and an optional hash lambda.
/// </summary>
/// <remarks>
/// If no hasher is supplied, the default equality comparer's hash is used. For a lambda-based comparer to be correct
/// inside hash-based containers, the hash function must agree with the equality function (equal items must hash the
/// same). When in doubt, supply both.
/// </remarks>
public class LambdaComparer<T> : IEqualityComparer<T>
{
    private readonly Func<T?, T?, bool> comparer;
    private readonly Func<T, int> hasher;

    /// <summary>Creates a new <see cref="LambdaComparer{T}"/>.</summary>
    /// <param name="comparer">Equality function. Required.</param>
    /// <param name="hasher">Hash function. If <see langword="null"/>, <see cref="EqualityComparer{T}.Default"/>'s hash is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparer"/> is <see langword="null"/>.</exception>
    public LambdaComparer(Func<T?, T?, bool> comparer, Func<T, int>? hasher = null)
    {
        this.comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        this.hasher = hasher ?? (obj => obj is null ? 0 : EqualityComparer<T>.Default.GetHashCode(obj));
    }

    /// <inheritdoc/>
    public bool Equals(T? x, T? y) => comparer(x, y);

    /// <inheritdoc/>
    public int GetHashCode(T obj) => obj is null ? 0 : hasher(obj);
}
