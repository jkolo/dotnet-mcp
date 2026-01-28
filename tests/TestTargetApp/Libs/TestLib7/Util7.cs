namespace TestLib7;

public static class Util7
{
    public static string GetName() => $"TestLib7(dep:{TestLib4.Util4.GetName()},{TestLib6.Util6.GetName()})";
}
