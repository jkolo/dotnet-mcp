namespace TestTargetApp;

/// <summary>
/// Target for exception breakpoint testing.
/// </summary>
public static class ExceptionTarget
{
    /// <summary>
    /// Throws InvalidOperationException. Use for testing exception breakpoints.
    /// </summary>
    public static void ThrowException()
    {
        // LINE 14 - Exception is thrown here
        throw new InvalidOperationException("Test exception from ExceptionTarget");
    }

    /// <summary>
    /// Throws ArgumentNullException. Use for testing specific exception type breakpoints.
    /// </summary>
    public static void ThrowArgumentNull()
    {
        // LINE 22
        throw new ArgumentNullException("param", "Test argument null exception");
    }

    /// <summary>
    /// Nested exception - inner and outer.
    /// </summary>
    public static void ThrowNested()
    {
        try
        {
            ThrowException();
        }
        catch (Exception ex)
        {
            // LINE 35
            throw new ApplicationException("Wrapper exception", ex);
        }
    }
}
