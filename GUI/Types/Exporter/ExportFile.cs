using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        public static void ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var currentPackage = vrfGuiContext.CurrentPackage;
            if (currentPackage == null)
            {
                Log.Error(nameof(ExportFile), "CurrentPackage is null, cannot extract file");
                return;
            }

            var stream = GameFileLoader.GetPackageEntryStream(currentPackage, file);

            ExtractFileFromStream(file.GetFullPath(), stream, vrfGuiContext, decompile);
        }

        public static void ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (!PreExportDisclaimer(Path.GetExtension(fileName)))
            {
                return;
            }

            if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                var exportData = new ExportData
                {
                    VrfGuiContext = new VrfGuiContext(fileName, vrfGuiContext),
                };

                var resourceTemp = new Resource
                {
                    FileName = fileName,
                };
                var resource = resourceTemp;
                string filaNameToSave;

                try
                {
                    resource.Read(stream);

                    var extension = FileExtract.GetExtension(resource);

                    if (extension == null)
                    {
                        stream.Dispose();
                        Log.Error(nameof(ExportFile), $"Export for \"{fileName}\" has no suitable extension");
                        return;
                    }

                    var filter = $"{extension} file|*.{extension}";

                    if (GltfModelExporter.CanExport(resource))
                    {
                        const string gltfFilter = "glTF|*.gltf";
                        const string glbFilter = "glTF Binary|*.glb";

                        filter = $"{filter}|{gltfFilter}|{glbFilter}";
                    }

                    var fileNameForSave = Path.GetFileNameWithoutExtension(fileName);

                    if (Path.GetExtension(fileName) == ".vmap_c")
                    {
                        // When exporting a vmap, suggest saving with a suffix like de_dust2_d,
                        // to reduce conflicts when users end up recompiling the map with the same name as it exists in the game
                        fileNameForSave += "_d";
                    }

                    using var dialog = new SaveFileDialog
                    {
                        Title = "Choose where to save the file",
                        FileName = fileNameForSave,
                        InitialDirectory = Settings.Config.SaveDirectory,
                        DefaultExt = extension,
                        Filter = filter,
                        AddToRecent = true,
                    };

                    var result = dialog.ShowDialog();

                    if (result != DialogResult.OK)
                    {
                        return;
                    }

                    filaNameToSave = dialog.FileName;
                    resourceTemp = null;
                }
                finally
                {
                    resourceTemp?.Dispose();
                }

                var directory = Path.GetDirectoryName(filaNameToSave);
                if (directory != null)
                {
                    Settings.Config.SaveDirectory = directory;
                }

                var extractDialog = new ExtractProgressForm(exportData, directory ?? string.Empty, true)
                {
                    ShownCallback = (form, cancellationToken) =>
                    {
                        form.SetProgress($"Extracting {fileName} to \"{Path.GetFileName(filaNameToSave)}\"");

                        Task.Run(async () =>
                        {
                            await form.ExtractFile(resource, fileName, filaNameToSave, true).ConfigureAwait(false);
                        }, cancellationToken).ContinueWith(t =>
                        {
                            stream.Dispose();
                            resource.Dispose();

                            form.ExportContinueWith(t);
                        }, CancellationToken.None);
                    }
                };

                try
                {
                    extractDialog.ShowDialog();
                    extractDialog = null;
                }
                finally
                {
                    extractDialog?.Dispose();
                    exportData.VrfGuiContext.Dispose();
                }
            }
            else
            {
                if (decompile && FileExtract.TryExtractNonResource(stream, fileName, out var content))
                {
                    if (content.Data == null)
                    {
                        // Content has no data to extract, only potentially subfiles
                        content.Dispose();
                        stream.Dispose();
                        Log.Info(nameof(ExportFile), $"File \"{fileName}\" has no extractable data");
                        return;
                    }

                    var extension = Path.GetExtension(content.FileName);
                    fileName = Path.ChangeExtension(fileName, extension);
                    stream.Dispose();

                    stream = new MemoryStream(content.Data);
                    content.Dispose();
                }

                using var dialog = new SaveFileDialog
                {
                    Title = "Choose where to save the file",
                    InitialDirectory = Settings.Config.SaveDirectory,
                    Filter = "All files (*.*)|*.*",
                    FileName = Path.GetFileName(fileName),
                    AddToRecent = true,
                };
                var userOK = dialog.ShowDialog();

                if (userOK == DialogResult.OK)
                {
                    var directory = Path.GetDirectoryName(dialog.FileName);
                    if (directory != null)
                    {
                        Settings.Config.SaveDirectory = directory;
                    }

                    Log.Info(nameof(ExportFile), $"Saved \"{Path.GetFileName(dialog.FileName)}\"");

                    using var streamOutput = dialog.OpenFile();
                    stream.CopyTo(streamOutput);
                }

                stream.Dispose();
            }
        }

        public static void ExtractFilesFromTreeNode(IBetterBaseItem selectedNode, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (!selectedNode.IsFolder)
            {
                var file = selectedNode.PackageEntry;
                Debug.Assert(file != null);
                // We are a file
                ExtractFileFromPackageEntry(file, vrfGuiContext, decompile);
            }
            else
            {
                // We are a folder
                var exportData = new ExportData
                {
                    VrfGuiContext = vrfGuiContext,
                };

                var extractDialog = new ExtractProgressForm(exportData, null, decompile);

                try
                {
                    extractDialog.QueueFiles(selectedNode);
                    extractDialog.ExecuteMultipleFileExtract();
                    extractDialog = null;
                }
                finally
                {
                    extractDialog?.Dispose();
                }
            }
        }

        public static void ExtractFilesFromListViewNodes(BetterListView.SelectedListViewItemCollection items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var exportData = new ExportData
            {
                VrfGuiContext = vrfGuiContext,
            };

            var extractDialog = new ExtractProgressForm(exportData, null, decompile);

            try
            {
                // When queuing files this way, it'll preserve the original tree
                // which is probably unwanted behaviour? It works tho /shrug
                foreach (IBetterBaseItem item in items)
                {
                    extractDialog.QueueFiles(item);
                }

                extractDialog.ExecuteMultipleFileExtract();
                extractDialog = null;
            }
            finally
            {
                extractDialog?.Dispose();
            }
        }

        public static bool PreExportDisclaimer(string fileExtension)
        {
            var messageString = "";

            switch (fileExtension)
            {
                case ".vmap_c":

                    messageString =
                    """
                    Decompiling Source2 maps is a difficult process, as such the output will be messy and imperfect, and will not resemble how
                    real .vmap files are made!

                    - Models will be merged by material across the map.
                    - Parts of the skybox mesh might be missing.
                    - The collision of the map will be merged into one mesh using special materials.
                    - The map will lack lightmap resolution volumes.
                    - Hammer meshes will be triangulated.

                    It is NOT ADVISED to work on decompiled maps as your first map if you are new to mapping!
                    """;
                    break;

                default:
                    break;
            }

            if (!string.IsNullOrEmpty(messageString))
            {
                var result = MessageBox.Show(messageString, "Decompile warning", MessageBoxButtons.OKCancel);

                if (result == DialogResult.Cancel)
                {
                    return false;
                }
            }

            return true;
        }

        public static void ExportEntitiesFromTreeNode(IBetterBaseItem selectedNode, VrfGuiContext vrfGuiContext)
        {
            if (selectedNode.PackageEntry == null || vrfGuiContext.CurrentPackage == null)
            {
                return;
            }

            vrfGuiContext.CurrentPackage.ReadEntry(selectedNode.PackageEntry, out var data, validateCrc: false);
            using var ms = new MemoryStream(data);
            using var resource = new Resource();
            resource.Read(ms);

            var entities = FileExtract.ExtractEntities(resource, vrfGuiContext);
            ExportEntitiesPipeline(entities, selectedNode.PackageEntry.GetFileName());
        }

        public static void ExportEntitiesFromTabPage(ExportData exportData)
        {
            Resource resource;
            Stream? stream = null;

            try
            {
                if (exportData.PackageEntry != null && exportData.VrfGuiContext.CurrentPackage != null)
                {
                    exportData.VrfGuiContext.CurrentPackage.ReadEntry(exportData.PackageEntry, out var data, validateCrc: false);
                    stream = new MemoryStream(data);
                }
                else
                {
                    stream = File.OpenRead(exportData.VrfGuiContext.FileName);
                }

                resource = new Resource();
                resource.Read(stream);
            }
            catch
            {
                stream?.Dispose();
                throw;
            }

            using (resource)
            using (stream)
            {
                var entities = FileExtract.ExtractEntities(resource, exportData.VrfGuiContext);
                var fileName = exportData.PackageEntry?.GetFileName() ?? Path.GetFileName(exportData.VrfGuiContext.FileName);
                ExportEntitiesPipeline(entities, fileName);
            }
        }

        private static void ExportEntitiesPipeline(List<EntityLump.Entity> entities, string sourceFileName)
        {
            if (entities.Count == 0)
            {
                MessageBox.Show("No entities found in this resource.", "Export Entities",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Step 1: Filter by classname
            using var filterForm = new FilterForm(entities);
            if (filterForm.ShowDialog() != DialogResult.Continue || filterForm.FilteredEntities.Count == 0)
            {
                return;
            }

            // Step 2: Property selection and export config
            using var propDialog = new PropertySelectionDialog(filterForm.FilteredEntities, allEntities: entities);
            if (propDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            // Step 3: Save file
            var defaultName = Path.GetFileNameWithoutExtension(sourceFileName) + "_entities.json";
            using var saveDialog = new SaveFileDialog
            {
                Title = "Save Entities",
                FileName = defaultName,
                InitialDirectory = Settings.Config.SaveDirectory,
                DefaultExt = "json",
                Filter = "JSON files|*.json",
                AddToRecent = true,
            };

            if (saveDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var directory = Path.GetDirectoryName(saveDialog.FileName);
            if (directory != null)
            {
                Settings.Config.SaveDirectory = directory;
            }

            File.WriteAllText(saveDialog.FileName, propDialog.ExportedJson);

            MessageBox.Show(
                $"Successfully exported {propDialog.FinalEntityList.Count} entities to {Path.GetFileName(saveDialog.FileName)}",
                "Export Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

    }
}
