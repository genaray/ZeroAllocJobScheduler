using NUnit.Framework.Constraints;

namespace JobScheduler.Test.Utils.CustomConstraints;

/// <summary>
/// Ensures that the provided lambda is allocating GC memory
/// </summary>
internal class AllocatingMemoryConstraint : Constraint
{
    public AllocatingMemoryConstraint() { }
    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        var heap = GC.GetAllocatedBytesForCurrentThread();
        (actual as TestDelegate ?? throw new InvalidOperationException($"Value must be a {nameof(TestDelegate)}!")).Invoke();
        if (heap != GC.GetAllocatedBytesForCurrentThread())
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