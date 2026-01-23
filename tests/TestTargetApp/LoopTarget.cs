namespace TestTargetApp;

/// <summary>
/// Target for loop breakpoint testing.
/// Line numbers are significant - tests depend on them!
/// </summary>
public static class LoopTarget
{
    /// <summary>
    /// Runs a simple loop. Set breakpoint on line 17 (the Console.WriteLine).
    /// </summary>
    public static void RunLoop(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            // LINE 17 - Set breakpoint here to hit multiple times
            Console.WriteLine($"Loop iteration {i}");
        }
    }

    /// <summary>
    /// A method that can be called to verify we're in the right place.
    /// </summary>
    public static int Add(int a, int b)
    {
        // LINE 26 - Simple arithmetic for testing
        return a + b;
    }
}
