namespace TestLib10;

public static class Util10
{
    public static string GetName() => $"TestLib10(dep:{TestLib9.Util9.GetName()})";
}
