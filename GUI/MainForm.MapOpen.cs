using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI
{
    partial class MainForm
    {
        private void InitializeMapOpenMenu()
        {
#pragma warning disable CA2000 // Ownership is transferred to vpkContextMenu.
            var openItem = new ThemedToolStripMenuItem
            {
                Text = "Find and open map",
                Name = "findAndOpenMapToolStripMenuItem",
            };
            vpkContextMenu.Items.Insert(0, openItem);
#pragma warning restore CA2000

            vpkContextMenu.Opening += (_, _) =>
            {
                var (guiContext, selectedNode) = GetSingleSelectedNode(openItem);
                openItem.Visible = guiContext?.CurrentPackage != null && selectedNode != null;
            };

            openItem.Click += async (_, _) =>
            {
                var (guiContext, selectedNode) = GetSingleSelectedNode(openItem);
                if (guiContext != null)
                {
                    try
                    {
                        var selectedPackage = selectedNode?.PackageEntry is { TypeName: "vpk" } package ? package : null;
                        var task = FindAndOpenMapAsync(guiContext, selectedPackage);
                        guiContext.DisposeAfter(task);
                        await task.ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        await AppMessageDialogs.ShowMessageAsync(exception.Message, "Unable to open map", MessageIcon.Error).ConfigureAwait(true);
                    }
                }
            };
        }

        internal async Task FindAndOpenMapAsync(string packagePath)
        {
            using var context = new VrfGuiContext(packagePath, null);
            context.CurrentPackage = new Package();
            try
            {
                var package = context.CurrentPackage;
                await Task.Run(() =>
                {
                    package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    package.Read(packagePath);
                }).ConfigureAwait(true);
                if (!IsDisposed && !Disposing)
                {
                    await FindAndOpenMapAsync(context).ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                await AppMessageDialogs.ShowMessageAsync(exception.Message, "Unable to open map", MessageIcon.Error).ConfigureAwait(true);
            }
        }

        private async Task FindAndOpenMapAsync(VrfGuiContext guiContext, PackageEntry? packageEntry = null)
        {
            var nestedContexts = new List<VrfGuiContext>();
            try
            {
                var maps = new List<MapCandidate>();
                FindMapEntries(guiContext, maps, new HashSet<string>(StringComparer.OrdinalIgnoreCase), nestedContexts, packageEntry);
                if (maps.Count == 0)
                {
                    await AppMessageDialogs.ShowMessageAsync("This package contains no vmap_c maps.", "Find and open map").ConfigureAwait(true);
                    return;
                }

                var map = maps[0];
                if (maps.Count > 1)
                {
                    using var dialog = new MapSelectionForm(maps.OrderBy(static map => map.DisplayPath, SearchForm.NumericComparer).ToList());
                    if (await dialog.ShowDialogAsync(this).ConfigureAwait(true) != DialogResult.OK || dialog.SelectedEntry is not { } selectedEntry)
                    {
                        return;
                    }

                    map = maps.First(candidate => ReferenceEquals(candidate.Entry, selectedEntry));
                }

                var fileContext = new VrfGuiContext(map.Entry.GetFullPath(), map.Context);
                try
                {
                    OpenFile(fileContext, map.Entry);
                    fileContext = null;
                }
                finally
                {
                    fileContext?.Dispose();
                }
            }
            finally
            {
                for (var index = nestedContexts.Count - 1; index >= 0; index--)
                {
                    nestedContexts[index].Dispose();
                }
            }
        }

#pragma warning disable CA2000 // Nested contexts are disposed after selection, or deferred until the opened map closes.
        private static void FindMapEntries(VrfGuiContext context, List<MapCandidate> maps, HashSet<string> visitedPackages,
            List<VrfGuiContext> nestedContexts, PackageEntry? packageEntry = null)
        {
            if (packageEntry != null)
            {
                var nestedPackage = NestedPackage.Open(context.CurrentPackage!, packageEntry);
                try
                {
                    var nestedContext = new VrfGuiContext(packageEntry.GetFullPath(), context)
                    {
                        CurrentPackage = nestedPackage,
                    };
                    nestedPackage = null;
                    nestedContexts.Add(nestedContext);
                    FindMapEntries(nestedContext, maps, visitedPackages, nestedContexts);
                }
                finally
                {
                    nestedPackage?.Dispose();
                }

                return;
            }

            if (context.CurrentPackage?.Entries is not { } entries || !visitedPackages.Add(context.DisplayPath))
            {
                return;
            }

            if (entries.TryGetValue("vmap_c", out var directMaps))
            {
                maps.AddRange(directMaps.Select(entry => new MapCandidate(context, entry, GetDisplayPath(context, entry))));
            }

            if (!entries.TryGetValue("vpk", out var nestedPackages))
            {
                return;
            }

            foreach (var nestedEntry in nestedPackages)
            {
                try
                {
                    FindMapEntries(context, maps, visitedPackages, nestedContexts, nestedEntry);
                }
                catch (Exception exception)
                {
                    Log.Error(nameof(MainForm), $"Unable to search nested package {nestedEntry.GetFullPath()}: {exception.Message}");
                }
            }
        }

        private static string GetDisplayPath(VrfGuiContext context, PackageEntry entry)
        {
            var packagePath = new Stack<string>();
            for (var current = context; current != null; current = current.ParentGuiContext)
            {
                packagePath.Push(current.FileName);
            }

            return string.Join("/", packagePath.Append(entry.GetFullPath()));
        }

        internal sealed record MapCandidate(VrfGuiContext Context, PackageEntry Entry, string DisplayPath);
#pragma warning restore CA2000
    }
}
