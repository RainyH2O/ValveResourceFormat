using System.IO;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace CLI
{
    internal sealed class EntityExportFileLoader : GameFileLoader
    {
        public EntityExportFileLoader(Package? currentPackage, string currentFileName)
            : base(currentPackage, currentFileName)
        {
        }

        public override Resource? LoadFile(string file)
        {
            var resource = base.LoadFile(file);

            if (file.EndsWith(".vents_c", StringComparison.OrdinalIgnoreCase) && resource?.DataBlock is not EntityLump)
            {
                resource?.Dispose();
                throw new FileNotFoundException($"Required entity lump \"{file}\" could not be loaded.", file);
            }

            return resource;
        }
    }
}
