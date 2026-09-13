using System.Globalization;
using System.IO;
using ValveResourceFormat.IO;

namespace CLI
{
    public partial class Decompiler
    {
        private static int ExecuteEntityImport(string input, string? output, bool quiet)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(output) || output == "-" || !Path.GetExtension(output).Equals(".vmap", StringComparison.OrdinalIgnoreCase)
                    || Directory.Exists(output) || Path.EndsInDirectorySeparator(output))
                {
                    Console.Error.WriteLine("Entity import requires --output to name a VMAP file.");
                    return 1;
                }

                var inputPath = Path.GetFullPath(input);
                var outputPath = Path.GetFullPath(output);

                if (inputPath.Equals(outputPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Entity import input and output must be different files.");
                    return 1;
                }

                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine("Entity import requires --input to name a single existing JSON file.");
                    return 1;
                }

                var result = EntityMapImporter.Convert(File.ReadAllText(inputPath));

                if (!quiet)
                {
                    foreach (var warning in result.Warnings)
                    {
                        Console.Error.WriteLine($"Warning: {warning}");
                    }
                }

                var outputDirectory = Path.GetDirectoryName(outputPath)!;
                Directory.CreateDirectory(outputDirectory);
                var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{Path.GetRandomFileName()}.tmp");

                try
                {
                    File.WriteAllBytes(temporaryPath, result.Data);
                    File.Move(temporaryPath, outputPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Imported {result.EntityCount} entities to \"{outputPath}\"."));
                return 0;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"Entity import failed: {exception.Message}");
                return 1;
            }
        }
    }
}
