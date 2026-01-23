namespace TestTargetApp;

/// <summary>
/// Target for nested call / stack trace testing.
/// </summary>
public static class NestedTarget
{
    /// <summary>
    /// Entry point for nested calls.
    /// </summary>
    public static void Level1()
    {
        // LINE 14
        Level2();
    }

    /// <summary>
    /// Second level.
    /// </summary>
    public static void Level2()
    {
        // LINE 23
        Level3();
    }

    /// <summary>
    /// Third level - set breakpoint here to test stack trace.
    /// </summary>
    public static void Level3()
    {
        // LINE 32 - Set breakpoint here to see full stack
        Console.WriteLine("At Level3");
    }
}
