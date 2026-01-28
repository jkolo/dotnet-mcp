namespace TestLib9;

public static class Util9
{
    public static string GetName() => $"TestLib9(dep:{TestLib5.Util5.GetName()},{TestLib7.Util7.GetName()},{TestLib8.Util8.GetName()})";
}
