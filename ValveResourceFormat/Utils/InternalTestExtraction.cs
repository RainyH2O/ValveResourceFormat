using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

[assembly: InternalsVisibleTo("Source2Viewer-CLI, PublicKey=002400000480000094000000060200000024000052534131000400000100010089d8964eb53f0af40a52aa1359ebbcb0ddc4a2b138a8917800d213decf8b3e06e4e8b54b8b79330f8cd48dfec4a790a65f256f141d61e155e57bf001cc69b6100bd06e1e775670cd972360a7dd8bc2373284cc9048c6911a56eedd5b7ddefcf3b5e3f1430c3e64eceebf30771d59d5072de649c12178de07385d2c52b2e56cb1")]
[assembly: InternalsVisibleTo("Tests, PublicKey=002400000480000094000000060200000024000052534131000400000100010089d8964eb53f0af40a52aa1359ebbcb0ddc4a2b138a8917800d213decf8b3e06e4e8b54b8b79330f8cd48dfec4a790a65f256f141d61e155e57bf001cc69b6100bd06e1e775670cd972360a7dd8bc2373284cc9048c6911a56eedd5b7ddefcf3b5e3f1430c3e64eceebf30771d59d5072de649c12178de07385d2c52b2e56cb1")]

namespace ValveResourceFormat.Utils
{
    internal class InternalTestExtraction
    {
        /// <summary>
        /// This method tries to run through all the code paths for a particular resource,
        /// which allows us to quickly find exceptions when running --stats using Decompiler over an entire game folder,
        /// as well it is used in tests to quickly verify.
        /// </summary>
        internal static void Test(Resource resource)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Model:
                    {
                        var model = (Model?)resource.DataBlock;
                        Debug.Assert(model != null);
                        model.GetEmbeddedAnimations();
                        model.GetEmbeddedMeshes();
                        model.GetEmbeddedPhys();

                        /* TODO: Getting first mesh index isn't working
                        if (model.Data.ContainsKey("m_modelSkeleton"))
                        {
                            var first = model.Data.GetIntegerArray("m_remappingTableStarts");
                            Skeleton.FromModelData(model.Data, (int)first[0]);
                        }
                        */

                        break;
                    }
                case ResourceType.Mesh:
                    {
                        var mesh = (Mesh?)resource.DataBlock;
                        Debug.Assert(mesh != null);
                        mesh.GetBounds();
                        break;
                    }

                case ResourceType.Particle:
                    {
                        var particle = (ParticleSystem?)resource.DataBlock;
                        Debug.Assert(particle != null);
                        particle.GetChildParticleNames();
                        particle.GetChildParticleNames(true);
                        break;
                    }

                case ResourceType.PhysicsCollisionMesh:
                    {
                        var phys = (PhysAggregateData?)resource.DataBlock;
                        Debug.Assert(phys != null);
                        var bindPose = phys.BindPose;
                        break;
                    }

                case ResourceType.Morph:
                    {
                        var morph = (Morph?)resource.DataBlock;
                        Debug.Assert(morph != null);
                        morph.GetMorphDatas();
                        morph.GetFlexDescriptors();
                        morph.GetFlexVertexData();
                        break;
                    }

                case ResourceType.Material:
                    {
                        var material = (Material?)resource.DataBlock;
                        Debug.Assert(material != null);
                        material.GetShaderArguments();
                        var inputSig = material.InputSignature;
                        break;
                    }

                case ResourceType.EntityLump:
                    {
                        var entityLump = (EntityLump?)resource.DataBlock;
                        Debug.Assert(entityLump != null);
                        entityLump.ToForgeGameData();
                        break;
                    }

                case ResourceType.Texture:
                    {
                        var texture = (Texture?)resource.DataBlock;
                        Debug.Assert(texture != null);
                        texture.GetSpriteSheetData();
                        using var _ = texture.GenerateBitmap(mipLevel: (uint)Math.Max(texture.NumMipLevels - 2, 0));

                        // Intentional return to avoid calling TextureExtract,
                        // We only test extracting a smaller mip size, and avoid packing png
                        return;
                    }
            }

            try
            {
                // Test extraction code flow
                using var contentFile = FileExtract.Extract(resource, new NullFileLoader());

                foreach (var contentSubFile in contentFile.SubFiles)
                {
                    contentSubFile.Extract();
                }
            }
            catch (FileNotFoundException)
            {
                // ignore for now because we use null file loader, map extract throws
            }
        }
    }
}
