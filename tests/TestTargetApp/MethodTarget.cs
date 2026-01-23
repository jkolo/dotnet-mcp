namespace TestTargetApp;

/// <summary>
/// Target for method breakpoint testing.
/// Line numbers are significant - tests depend on them!
/// </summary>
public static class MethodTarget
{
    /// <summary>
    /// Simple greeting method. Set breakpoint on line 14 (the greeting assignment).
    /// </summary>
    public static string SayHello(string name)
    {
        var greeting = $"Hello, {name}!";  // LINE 14 - Set breakpoint here
        return greeting;
    }

    /// <summary>
    /// Method with multiple statements for step testing.
    /// </summary>
    public static int Calculate(int x)
    {
        // LINE 24
        var a = x * 2;
        // LINE 26
        var b = a + 10;
        // LINE 28
        var c = b - 5;
        // LINE 30
        return c;
    }
}
