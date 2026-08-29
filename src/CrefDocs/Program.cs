namespace CrefDocs;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3));
            return 0;
        }

        Console.WriteLine("CrefDocs");
        Console.WriteLine();
        Console.WriteLine("Commands will be added as the snapshot and renderer contracts are implemented.");
        return 0;
    }
}

