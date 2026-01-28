namespace TestLib2;

public static class Util2
{
    public static string GetName() => $"TestLib2(dep:{TestLib1.Util1.GetName()})";
}
