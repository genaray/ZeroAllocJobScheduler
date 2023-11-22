using NUnit.Framework.Constraints;

namespace Schedulers.Test.Utils.CustomConstraints;

/// <summary>
/// Ensures that the provided lambda is allocating GC memory
/// </summary>
internal class AllocatingMemoryConstraint : Constraint
{
    private static readonly object _gcLock = new();
    public AllocatingMemoryConstraint() { }
    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        var code = actual as TestDelegate ?? throw new InvalidOperationException($"Value must be a {nameof(TestDelegate)}!");
        bool allocated;
        // prevent threads from enabling the GC
        lock (_gcLock)
        {
            // trigger a  manual collection; reduces the possibility that a different
            // test-thread allocates and triggers a GC to run on the main thread. that still
            // might happen though, no easy way to prevent that.
            GC.Collect();
            var heap = GC.GetAllocatedBytesForCurrentThread();
            code.Invoke();
            allocated = heap != GC.GetAllocatedBytesForCurrentThread();
        }

        if (allocated)
        {
            // we DID allocate memory! So return true
            return new ConstraintResult(this, actual, true);
        }

        return new ConstraintResult(this, actual, false);
    }
}

// enable Is.Not usage
internal static class CustomConstraintExtensions
{
    /// <summary>
    /// Ensures that the provided lambda is allocating GC memory
    /// </summary>
    public static AllocatingMemoryConstraint AllocatingMemory(this ConstraintExpression expression)
    {
        var constraint = new AllocatingMemoryConstraint();
        expression.Append(constraint);
        return constraint;
    }
}

// enable Is usage
internal class Is : NUnit.Framework.Is
{
    public static AllocatingMemoryConstraint AllocatingMemory()
    {
        return new AllocatingMemoryConstraint();
    }
}
