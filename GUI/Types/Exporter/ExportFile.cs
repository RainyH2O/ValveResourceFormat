using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Resource = ValveResourceFormat.Resource;

#nullable disable

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        public static void ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var stream = AdvancedGuiFileLoader.GetPackageEntryStream(vrfGuiContext.CurrentPackage, file);

            ExtractFileFromStream(file.GetFileName(), stream, vrfGuiContext, decompile);
        }

        public static void ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                var exportData = new ExportData
                {
                    VrfGuiContext = new VrfGuiContext(null, vrfGuiContext),
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
                Settings.Config.SaveDirectory = directory;

                var extractDialog = new ExtractProgressForm(exportData, directory, true)
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
                    FileName = fileName,
                    AddToRecent = true,
                };
                var userOK = dialog.ShowDialog();

                if (userOK == DialogResult.OK)
                {
                    Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);

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
                    extractDialog.Execute();
                    extractDialog = null;
                }
                finally
                {
                    extractDialog?.Dispose();
                }
            }
        }

        public static void ExtractFilesFromListViewNodes(ListView.SelectedListViewItemCollection items,
            VrfGuiContext vrfGuiContext, bool decompile)
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

                extractDialog.Execute();
                extractDialog = null;
            }
            finally
            {
                extractDialog?.Dispose();
            }
        }

        public static void ExportEntitiesFromTreeNode(IBetterBaseItem selectedNode, VrfGuiContext vrfGuiContext)
        {
            if (selectedNode.IsFolder || !selectedNode.PackageEntry.GetFileName()
                    .EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                return;
            }

            var file = selectedNode.PackageEntry;
            var fileName = file.GetFileName();
            using var stream = AdvancedGuiFileLoader.GetPackageEntryStream(vrfGuiContext.CurrentPackage, file);

            var exportData = new ExportData { VrfGuiContext = new VrfGuiContext(null, vrfGuiContext) };
            using var resource = new Resource { FileName = fileName };

            try
            {
                resource.Read(stream);

                var saveFileName = GetSaveFileName(fileName);
                if (string.IsNullOrEmpty(saveFileName))
                {
                    return;
                }

                Settings.Config.SaveDirectory = Path.GetDirectoryName(saveFileName);
                var entities = FileExtract.ExtractEntities(resource, exportData.VrfGuiContext.FileLoader);

                var filteredEntities = FilterEntitiesByType(entities);
                if (filteredEntities.Count == 0)
                {
                    return;
                }

                var (exportType, selectedProperties, entitiesToExport) = ConfigureExport(filteredEntities, entities);
                if (exportType == null)
                {
                    return;
                }

                var entitiesJson = exportType == PropertySelectionDialog.ExportType.Custom
                    ? SerializeEntitiesWithSelectedProperties(entitiesToExport, selectedProperties)
                    : MapExtract.SerializeEntities(entitiesToExport);

                File.WriteAllText(saveFileName, entitiesJson);
                ShowExportSuccess(entitiesToExport.Count, exportType.Value, Path.GetFileName(saveFileName));
            }
            finally
            {
                exportData.VrfGuiContext.Dispose();
            }
        }

        public static void ExportEntitiesFromTabPage(ExportData exportData)
        {
            Stream stream = null;

            try
            {
                var fileName = exportData.PackageEntry?.GetFileName() ?? exportData.VrfGuiContext.FileName;

                if (!fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    MessageBox.Show("File does not support entity export.", "Export Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                stream = exportData.PackageEntry != null
                    ? AdvancedGuiFileLoader.GetPackageEntryStream(exportData.VrfGuiContext.CurrentPackage,
                        exportData.PackageEntry)
                    : File.OpenRead(fileName);

                using var resource = new Resource { FileName = Path.GetFileName(fileName) };
                resource.Read(stream);

                var saveFileName = GetSaveFileName(fileName);
                if (string.IsNullOrEmpty(saveFileName))
                {
                    return;
                }

                Settings.Config.SaveDirectory = Path.GetDirectoryName(saveFileName);
                var entities = FileExtract.ExtractEntities(resource, exportData.VrfGuiContext.FileLoader);

                var filteredEntities = FilterEntitiesByType(entities);
                if (filteredEntities.Count == 0)
                {
                    return;
                }

                var (exportType, selectedProperties, entitiesToExport) = ConfigureExport(filteredEntities, entities);
                if (exportType == null)
                {
                    return;
                }

                var entitiesJson = exportType == PropertySelectionDialog.ExportType.Custom
                    ? SerializeEntitiesWithSelectedProperties(entitiesToExport, selectedProperties)
                    : MapExtract.SerializeEntities(entitiesToExport);

                File.WriteAllText(saveFileName, entitiesJson);
                ShowExportSuccess(entitiesToExport.Count, exportType.Value, Path.GetFileName(saveFileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private static string GetSaveFileName(string fileName)
        {
            var baseFileName = Path.GetFileNameWithoutExtension(fileName);
            if (Path.GetExtension(fileName) == ".vmap_c")
            {
                baseFileName += "_entities";
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Choose save location",
                FileName = baseFileName + ".json",
                InitialDirectory = Settings.Config.SaveDirectory,
                DefaultExt = "json",
                Filter = "JSON files|*.json",
                AddToRecent = true
            };

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : "";
        }

        private static List<EntityLump.Entity> FilterEntitiesByType(List<EntityLump.Entity> entities)
        {
            using var filterWindow = new FilterForm(entities);
            if (filterWindow.ShowDialog() != DialogResult.Continue)
            {
                return new List<EntityLump.Entity>();
            }

            var filteredEntities = filterWindow.filteredEntities;
            if (filteredEntities?.Count > 0)
            {
                return filteredEntities;
            }

            MessageBox.Show("No entity types selected.", "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new List<EntityLump.Entity>();
        }

        private static (PropertySelectionDialog.ExportType?, HashSet<string>, List<EntityLump.Entity>) ConfigureExport(
            List<EntityLump.Entity> filteredEntities, List<EntityLump.Entity> allEntities)
        {
            using var propertyDialog = new PropertySelectionDialog(filteredEntities);
            if (propertyDialog.ShowDialog() != DialogResult.OK)
            {
                return (null, new HashSet<string>(), new List<EntityLump.Entity>());
            }

            var entitiesToExport = propertyDialog.IncludeRelatedEntities
                ? GetEntitiesWithRelated(filteredEntities, allEntities)
                : filteredEntities;

            return (propertyDialog.SelectedExportType, propertyDialog.SelectedProperties, entitiesToExport);
        }

        private static void ShowExportSuccess(int entityCount, PropertySelectionDialog.ExportType exportType,
            string fileName)
        {
            var typeText = exportType switch
            {
                PropertySelectionDialog.ExportType.Full => "full",
                PropertySelectionDialog.ExportType.Custom => "custom",
                _ => "unknown"
            };

            MessageBox.Show($"Successfully exported {entityCount} entities ({typeText}) to {fileName}",
                "Export Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static List<EntityLump.Entity> GetEntitiesWithRelated(List<EntityLump.Entity> filteredEntities,
            List<EntityLump.Entity> allEntities)
        {
            var visitedEntities = new HashSet<EntityLump.Entity>(filteredEntities);
            var resultEntities = new List<EntityLump.Entity>(filteredEntities);
            var namedEntitiesLookup = CreateNamedEntitiesLookup(allEntities);

            foreach (var entity in filteredEntities)
            {
                FindRelatedEntities(entity, allEntities, namedEntitiesLookup, visitedEntities, resultEntities);
            }

            return resultEntities;
        }

        private static Dictionary<string, List<EntityLump.Entity>> CreateNamedEntitiesLookup(
            List<EntityLump.Entity> allEntities)
        {
            return allEntities
                .Where(entity => entity.Properties.Properties.TryGetValue("targetname", out var kvValue) &&
                                 kvValue.Value != null)
                .GroupBy(entity => entity.Properties.Properties["targetname"].Value!.ToString()!)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private static void FindRelatedEntities(EntityLump.Entity rootEntity, List<EntityLump.Entity> allEntities,
            Dictionary<string, List<EntityLump.Entity>> namedEntitiesLookup, HashSet<EntityLump.Entity> visitedEntities,
            List<EntityLump.Entity> resultEntities)
        {
            if (rootEntity.Connections?.Count > 0)
            {
                ProcessOutgoingConnections(rootEntity, namedEntitiesLookup, allEntities, visitedEntities,
                    resultEntities);
            }

            if (rootEntity.Properties.Properties.TryGetValue("targetname", out var rootTargetName) &&
                rootTargetName.Value != null)
            {
                ProcessIncomingConnections(rootEntity, allEntities, rootTargetName.Value.ToString()!, visitedEntities,
                    resultEntities);
            }
        }

        private static void ProcessOutgoingConnections(EntityLump.Entity rootEntity,
            Dictionary<string, List<EntityLump.Entity>> namedEntitiesLookup, List<EntityLump.Entity> allEntities,
            HashSet<EntityLump.Entity> visitedEntities, List<EntityLump.Entity> resultEntities)
        {
            foreach (var connection in rootEntity.Connections!)
            {
                if (!connection.Properties.TryGetValue("m_targetName", out var targetNameValue) ||
                    targetNameValue.Value == null)
                {
                    continue;
                }

                var targetName = targetNameValue.Value.ToString()!;
                if (!namedEntitiesLookup.TryGetValue(targetName, out var targetEntities))
                {
                    continue;
                }

                foreach (var entity in targetEntities.Where(e => e != rootEntity && !visitedEntities.Contains(e)))
                {
                    resultEntities.Add(entity);
                    visitedEntities.Add(entity);
                    FindRelatedEntities(entity, allEntities, namedEntitiesLookup, visitedEntities, resultEntities);
                }
            }
        }

        private static void ProcessIncomingConnections(EntityLump.Entity rootEntity,
            List<EntityLump.Entity> allEntities,
            string rootName, HashSet<EntityLump.Entity> visitedEntities, List<EntityLump.Entity> resultEntities)
        {
            var namedEntitiesLookup = CreateNamedEntitiesLookup(allEntities);

            foreach (var entity in allEntities.Where(e =>
                         e.Connections?.Count > 0 && e != rootEntity && !visitedEntities.Contains(e)))
            {
                if (entity.Connections!.Any(conn => conn.Properties.TryGetValue("m_targetName", out var targetValue) &&
                                                    targetValue.Value?.ToString() == rootName))
                {
                    resultEntities.Add(entity);
                    visitedEntities.Add(entity);
                    FindRelatedEntities(entity, allEntities, namedEntitiesLookup, visitedEntities, resultEntities);
                }
            }
        }

        private static readonly HashSet<string> EssentialConnectionProperties = new()
            { "m_outputName", "m_targetName", "m_inputName", "m_overrideParam", "m_flDelay", "m_nTimesToFire" };

        private static string SerializeEntitiesWithSelectedProperties(List<EntityLump.Entity> entities,
            HashSet<string> selectedProperties)
        {
            var filteredEntities = entities.Select(entity =>
            {
                var entityDict = new Dictionary<string, object>();

                foreach (var (key, value) in entity.Properties.Properties)
                {
                    if (selectedProperties.Contains(key) && value.Value != null)
                    {
                        entityDict[key] = ConvertEntityValue(value.Value);
                    }
                }

                if (entity.Connections?.Count > 0)
                {
                    var connections = entity.Connections
                        .Select(FilterConnection)
                        .Where(filteredConn => filteredConn.Count > 0)
                        .Cast<object>()
                        .ToList();

                    if (connections.Count > 0)
                    {
                        entityDict["connections"] = connections;
                    }
                }

                return entityDict;
            }).Where(dict => dict.Count > 0).ToList();

            return JsonSerializer.Serialize(filteredEntities, KVJsonContext.Options);
        }

        private static object ConvertEntityValue(object value)
        {
            return value switch
            {
                string str => str,
                bool boolean => boolean,
                Vector3 vector => new { vector.X, vector.Y, vector.Z },
                Vector2 vector => new { vector.X, vector.Y },
                KVObject { IsArray: true } kvArray => kvArray.Select(p => p.Value).ToArray(),
                _ when value.GetType().IsPrimitive => value,
                _ => value.ToString() ?? ""
            };
        }

        private static Dictionary<string, object> FilterConnection(KVObject connection)
        {
            var filteredConnection = new Dictionary<string, object>();

            foreach (var (key, kvValue) in connection.Properties)
            {
                if (EssentialConnectionProperties.Contains(key) && kvValue.Value != null)
                {
                    filteredConnection[key] = ConvertEntityValue(kvValue.Value);
                }
            }

            return filteredConnection;
        }
    }
}
