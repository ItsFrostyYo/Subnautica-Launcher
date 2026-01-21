using System.Reflection;

public static class AppVersion
{
    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version!;
}