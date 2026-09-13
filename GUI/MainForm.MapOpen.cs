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
                    guiContext.AddChildren();
                    try
                    {
                        var selectedPackage = selectedNode?.PackageEntry is { TypeName: "vpk" } package ? package : null;
                        await FindAndOpenMapAsync(guiContext, selectedPackage).ConfigureAwait(true);
                    }
                    finally
                    {
                        guiContext.RemoveChildren();
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
            var maps = new List<MapCandidate>();
            FindMapEntries(guiContext, maps, new HashSet<string>(StringComparer.OrdinalIgnoreCase), packageEntry);
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

#pragma warning disable CA2000 // Nested package contexts are retained by map candidates and disposed with their parent.
        private static void FindMapEntries(VrfGuiContext context, List<MapCandidate> maps, HashSet<string> visitedPackages, PackageEntry? packageEntry = null)
        {
            if (packageEntry != null)
            {
                VrfGuiContext? nestedContext = null;
                Package? nestedPackage = null;
                try
                {
                    nestedPackage = new Package();
                    nestedPackage.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    nestedPackage.SetFileName(packageEntry.GetFullPath());
                    var stream = GameFileLoader.GetPackageEntryStream(context.CurrentPackage!, packageEntry);
                    nestedPackage.Read(stream);
                    nestedContext = new VrfGuiContext(packageEntry.GetFullPath(), context)
                    {
                        CurrentPackage = nestedPackage,
                    };
                    nestedPackage = null;
                    FindMapEntries(nestedContext, maps, visitedPackages);
                }
                catch
                {
                    nestedContext?.Dispose();
                }
                finally
                {
                    nestedPackage?.Dispose();
                }

                return;
            }

            if (context.CurrentPackage?.Entries is not { } entries || !visitedPackages.Add(context.FileName))
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
                VrfGuiContext? nestedContext = null;
                Package? nestedPackage = null;
                try
                {
                    nestedPackage = new Package();
                    nestedPackage.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    nestedPackage.SetFileName(nestedEntry.GetFullPath());
                    var stream = GameFileLoader.GetPackageEntryStream(context.CurrentPackage, nestedEntry);
                    nestedPackage.Read(stream);
                    nestedContext = new VrfGuiContext(nestedEntry.GetFullPath(), context)
                    {
                        CurrentPackage = nestedPackage,
                    };
                    nestedPackage = null;
                    FindMapEntries(nestedContext, maps, visitedPackages);
                }
                catch
                {
                    nestedContext?.Dispose();
                }
                finally
                {
                    nestedPackage?.Dispose();
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
