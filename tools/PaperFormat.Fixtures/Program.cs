namespace PaperFormat.Fixtures;

internal static class Program
{
    private const string OutputOption = "--output";

    private static int Main(string[] args)
    {
        try
        {
            var outputDirectory = ParseOutputDirectory(args);
            if (outputDirectory is null)
            {
                PrintUsage();
                return 0;
            }

            var generatedFiles = FixtureGenerator.Generate(outputDirectory);

            Console.WriteLine($"Generated {generatedFiles.Count} DOCX fixtures in {outputDirectory}");
            foreach (var generatedFile in generatedFiles)
            {
                Console.WriteLine($"- {Path.GetFileName(generatedFile)}");
            }

            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Fixture generation failed: {exception.Message}");
            return 1;
        }
    }

    private static string? ParseOutputDirectory(IReadOnlyList<string> args)
    {
        var outputDirectory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "tests",
            "fixtures",
            "generated");

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument is "--help" or "-h")
            {
                return null;
            }

            if (argument.StartsWith($"{OutputOption}=", StringComparison.Ordinal))
            {
                outputDirectory = argument[(OutputOption.Length + 1)..];
                continue;
            }

            if (argument == OutputOption)
            {
                if (++index >= args.Count)
                {
                    throw new ArgumentException($"{OutputOption} requires a directory path.");
                }

                outputDirectory = args[index];
                continue;
            }

            throw new ArgumentException($"Unknown argument: {argument}");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("The output directory cannot be empty.");
        }

        return Path.GetFullPath(outputDirectory);
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project tools/PaperFormat.Fixtures -- " +
            "[--output <directory>]");
    }
}
