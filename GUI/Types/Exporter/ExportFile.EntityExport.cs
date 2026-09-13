using System.IO;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter;

static partial class ExportFile
{
    public static void ExportEntitiesFromTreeNode(IBetterBaseItem selectedNode, VrfGuiContext vrfGuiContext)
    {
        if (selectedNode.PackageEntry != null && vrfGuiContext.CurrentPackage != null)
        {
            ExportEntities(selectedNode.PackageEntry, vrfGuiContext);
        }
    }

    public static void ExportEntitiesFromTabPage(ExportData exportData)
    {
        ExportEntities(exportData.PackageEntry, exportData.VrfGuiContext);
    }

    private static void ExportEntities(PackageEntry? packageEntry, VrfGuiContext context)
    {
        var fileName = packageEntry?.GetFullPath() ?? context.FileName;
        using var stream = packageEntry != null
            ? context.CurrentPackage!.OpenEntryStream(packageEntry)
            : File.OpenRead(fileName);
        using var resource = new Resource
        {
            FileName = fileName,
        };
        resource.Read(stream);

        var entities = FileExtract.ExtractEntities(resource, context.FileLoaderNoCache);
        ExportEntitiesPipeline(entities, Path.GetFileName(fileName));
    }

    private static void ExportEntitiesPipeline(List<EntityLump.Entity> entities, string sourceFileName)
    {
        if (entities.Count == 0)
        {
            _ = AppMessageDialogs.ShowMessageAsync("No entities found in this resource.", "Export Entities");
            return;
        }

        using var filterForm = new FilterForm(entities);
        if (filterForm.ShowDialog() != DialogResult.Continue || filterForm.FilteredEntities.Count == 0)
        {
            return;
        }

        using var propDialog = new PropertySelectionDialog(filterForm.FilteredEntities, allEntities: entities);
        if (propDialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var defaultName = Path.GetFileNameWithoutExtension(sourceFileName) + "_entities.json";
        var savePath = AppFileDialogs.SaveFile("Save Entities", defaultName, "json", "JSON files|*.json");
        if (savePath == null)
        {
            return;
        }

        File.WriteAllText(savePath, propDialog.ExportedJson);

        _ = AppMessageDialogs.ShowMessageAsync(
            $"Successfully exported {propDialog.FinalEntityList.Count} entities to {Path.GetFileName(savePath)}",
            "Export Success");
    }
}
