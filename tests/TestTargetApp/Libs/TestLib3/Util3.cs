namespace TestLib3;

public static class Util3
{
    public static string GetName() => $"TestLib3(dep:{TestLib1.Util1.GetName()})";
}
