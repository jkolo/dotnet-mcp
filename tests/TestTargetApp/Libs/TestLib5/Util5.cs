namespace TestLib5;

public static class Util5
{
    public static string GetName() => $"TestLib5(dep:{TestLib2.Util2.GetName()},{TestLib3.Util3.GetName()})";
}
