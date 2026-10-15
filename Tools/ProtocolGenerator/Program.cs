namespace ProtocolGeneratorTool;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (!ProtocolGenerationOptions.TryParse(args, out ProtocolGenerationOptions options, out string error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            ProtocolGenerator.Generate(options);
            Console.WriteLine("Protocol generation completed: Client and Server message sets updated.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
