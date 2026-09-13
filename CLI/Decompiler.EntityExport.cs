using System.IO;
using System.Text;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace CLI
{
    public partial class Decompiler
    {
        private int ExecuteEntityExport()
        {
            if (!TryValidateEntityExportArguments(out var resourcePath))
            {
                return 1;
            }

            try
            {
                using var package = Path.GetExtension(InputFile).Equals(".vpk", StringComparison.OrdinalIgnoreCase) ? new Package() : null;

                if (package != null)
                {
                    package.Read(InputFile);
                }

                using var fileLoader = new EntityExportFileLoader(package, InputFile);

                if (GamePath != null)
                {
                    fileLoader.FindAndLoadSearchPaths(GamePath);
                }

                using var resource = LoadEntityExportResource(fileLoader, resourcePath);

                if (resource.ResourceType is not ResourceType.Map and not ResourceType.World)
                {
                    Console.Error.WriteLine($"Entity export requires a Map or World resource, got {resource.ResourceType}.");
                    return 1;
                }

                var json = MapExtract.SerializeEntities(FileExtract.ExtractEntities(resource, fileLoader));
                WriteEntityExport(json);
                Console.WriteLine($"Entity export written to \"{OutputFile}\".");

                return 0;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"Entity export failed: {exception.Message}");
                return 1;
            }
        }

        private int ExecuteEntityExportMap(string mapName)
        {
            if (!TryValidateEntityExportMapArguments(mapName, out var resourcePath, out var gameInfoPath))
            {
                return 1;
            }

            try
            {
                var mapPath = Path.Combine(Path.GetDirectoryName(gameInfoPath)!, resourcePath);
                using var fileLoader = new EntityExportFileLoader(null, mapPath);

                var existingMap = fileLoader.FindFile(resourcePath, logNotFound: false);

                if (existingMap.PathOnDisk == null && existingMap.PackageEntry == null)
                {
                    AddWorkshopMapPackage(fileLoader, resourcePath);
                }

                using var resource = fileLoader.LoadFile(resourcePath)
                    ?? throw new FileNotFoundException($"Could not find map \"{mapName}\" as \"{resourcePath}\".");

                if (resource.ResourceType != ResourceType.Map)
                {
                    Console.Error.WriteLine($"Entity export requires a Map resource, got {resource.ResourceType}.");
                    return 1;
                }

                var json = MapExtract.SerializeEntities(FileExtract.ExtractEntities(resource, fileLoader));
                WriteEntityExport(json);
                Console.WriteLine($"Entity export for \"{mapName}\" written to \"{OutputFile}\".");

                return 0;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"Entity export failed: {exception.Message}");
                return 1;
            }
        }

        private bool TryValidateEntityExportArguments(out string resourcePath)
        {
            resourcePath = string.Empty;

            if (!HasEntityExportOutput())
            {
                return false;
            }

            if (HasEntityExportModeConflict())
            {
                return false;
            }

            var extension = Path.GetExtension(InputFile);

            if (extension.Equals(".vpk", StringComparison.OrdinalIgnoreCase))
            {
                if (FileFilter.Length != 1 || !IsExactEntityResourcePath(FileFilter[0]))
                {
                    Console.Error.WriteLine("Entity export from a VPK requires exactly one --vpk_filepath ending in .vmap_c or .vwrld_c.");
                    return false;
                }

                resourcePath = FileFilter[0];
                return true;
            }

            if (!extension.Equals(".vmap_c", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".vwrld_c", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Entity export input must be a .vpk, .vmap_c, or .vwrld_c file.");
                return false;
            }

            if (FileFilter.Length != 0)
            {
                Console.Error.WriteLine("--vpk_filepath is only valid when entity export input is a VPK.");
                return false;
            }

            resourcePath = InputFile;
            return true;
        }

        private bool TryValidateEntityExportMapArguments(string mapName, out string resourcePath, out string gameInfoPath)
        {
            resourcePath = string.Empty;
            gameInfoPath = string.Empty;

            if (!HasEntityExportOutput() || HasEntityExportModeConflict() || FileFilter.Length != 0 || InputFile.Length != 0)
            {
                if (FileFilter.Length != 0 || InputFile.Length != 0)
                {
                    Console.Error.WriteLine("--export_entities_map does not accept --input or --vpk_filepath.");
                }

                return false;
            }

            if (string.IsNullOrWhiteSpace(mapName) || mapName.IndexOfAny(['/', '\\']) >= 0 || mapName.Contains("..", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Entity export map must be a map name, not a path.");
                return false;
            }

            var resolvedGameInfoPath = GamePath ?? GetCounterStrikeGameInfoPath();

            if (resolvedGameInfoPath == null)
            {
                Console.Error.WriteLine("Could not find Counter-Strike 2 gameinfo.gi. Specify --game with its path.");
                return false;
            }

            gameInfoPath = resolvedGameInfoPath;
            resourcePath = $"maps/{mapName}.vmap_c";
            return true;
        }

        private bool HasEntityExportOutput()
        {
            if (OutputFile != null && Path.GetExtension(OutputFile).Equals(".json", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(OutputFile))
            {
                return true;
            }

            Console.Error.WriteLine("Entity export requires --output to name a JSON file.");
            return false;
        }

        private bool HasEntityExportModeConflict()
        {
            if (!(RecursiveSearch || RecursiveSearchArchives || PrintAllBlocks || BlockToPrint != null || MaxParallelismThreads != 1
                || OutputVPKDir || VerifyVPKChecksums || CachedManifest || ExtFilterList != null || ListResources
                || GltfExportFormat != null || GltfExportAnimations || GltfAnimationFilter.Length > 0 || GltfMeshFilter.Length > 0
                || GltfExportMaterials || GltfExportAdaptTextures || GltfExportExtras || GltfComposeAdditive || ToolsAssetInfoShort
                || CollectStats || StatsWithLoader || StatsPrintFilePaths || StatsPrintUniqueDependencies || StatsCollectParticles
                || StatsCollectVBIB || GltfTest || DumpUnknownEntityKeys))
            {
                return false;
            }

            Console.Error.WriteLine("Entity export cannot be combined with another export or processing mode.");
            return true;
        }

        private static string? GetCounterStrikeGameInfoPath()
        {
            var game = GameFolderLocator.FindSteamGameByAppId(730);
            var gameInfoPath = game.HasValue ? Path.Combine(game.Value.GamePath, "game", "csgo", "gameinfo.gi") : null;
            return gameInfoPath != null && File.Exists(gameInfoPath) ? gameInfoPath : null;
        }

        private static void AddWorkshopMapPackage(EntityExportFileLoader fileLoader, string resourcePath)
        {
            var game = GameFolderLocator.FindSteamGameByAppId(730);

            if (!game.HasValue)
            {
                return;
            }

            var workshopRoot = Path.Combine(game.Value.SteamPath, "workshop", "content", "730");

            if (!Directory.Exists(workshopRoot))
            {
                return;
            }

            foreach (var addonPath in Directory.EnumerateDirectories(workshopRoot))
            {
                var addonId = Path.GetFileName(addonPath);
                var packagePath = Path.Combine(addonPath, $"{addonId}.vpk");

                if (!File.Exists(packagePath))
                {
                    packagePath = Path.Combine(addonPath, $"{addonId}_dir.vpk");
                }

                if (!File.Exists(packagePath))
                {
                    continue;
                }

                Package? package = null;

                try
                {
                    package = new Package();
                    package.Read(packagePath);

                    if (package.FindEntry(resourcePath) == null)
                    {
                        continue;
                    }

                    fileLoader.AddPackageToSearch(package);
                    package = null;
                    return;
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                }
                finally
                {
                    package?.Dispose();
                }
            }
        }

        private static bool IsExactEntityResourcePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && !path.Contains(',', StringComparison.Ordinal)
                && !path.Contains('*', StringComparison.Ordinal)
                && !path.Contains('?', StringComparison.Ordinal)
                && !path.StartsWith('/')
                && !path.Split('/').Contains("..", StringComparer.Ordinal)
                && (path.EndsWith(".vmap_c", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".vwrld_c", StringComparison.OrdinalIgnoreCase));
        }

        private static Resource LoadEntityExportResource(EntityExportFileLoader fileLoader, string resourcePath)
        {
            if (fileLoader.CurrentPackage != null)
            {
                return fileLoader.LoadFile(resourcePath)
                    ?? throw new FileNotFoundException($"Failed to load requested VPK resource \"{resourcePath}\".");
            }

            var resource = new Resource { FileName = resourcePath };

            try
            {
                resource.Read(resourcePath);
                return resource;
            }
            catch
            {
                resource.Dispose();
                throw;
            }
        }

        private void WriteEntityExport(string json)
        {
            var outputDirectory = Path.GetDirectoryName(OutputFile)!;
            Directory.CreateDirectory(outputDirectory);
            var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(OutputFile)}.{Path.GetRandomFileName()}.tmp");

            try
            {
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporaryPath, OutputFile!, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
