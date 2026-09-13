using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;
using VEntityLump = ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Walks an entity lump plus the child lumps its <c>point_template</c> entities reference, pairing each entity with the parent transform that applies to it.
    /// </summary>
    public static class EntityLumpTraversal
    {
        /// <summary>
        /// An entity from <see cref="EnumerateEntities"/>, with the parent transform that applies to it and whether it came from a <c>point_template</c> child lump.
        /// </summary>
        public readonly record struct TraversedEntity(Entity Entity, Matrix4x4 ParentTransform, bool FromTemplate);

        /// <summary>
        /// Enumerates <paramref name="lump"/>'s entities and, recursively, the entities of child lumps its
        /// <c>point_template</c> entities reference. Template children inherit the template's rigid transform (no scale).
        /// </summary>
        /// <param name="lump">The root entity lump.</param>
        /// <param name="fileLoader">Loads referenced child lumps by name.</param>
        /// <param name="rootTransform">Transform applied to top-level entities.</param>
        /// <param name="onMissingChildLump">Called with the lump name when a referenced child lump can't be resolved.</param>
        /// <param name="includeUnreferencedChildLumps">Whether child lumps not referenced by a point template are also enumerated.</param>
        /// <returns>Each entity with its parent transform.</returns>
        public static IEnumerable<TraversedEntity> EnumerateEntities(
            VEntityLump lump,
            IFileLoader fileLoader,
            Matrix4x4 rootTransform,
            Action<string>? onMissingChildLump = null,
            bool includeUnreferencedChildLumps = false)
        {
            return EnumerateEntitiesIterator(lump, fileLoader, rootTransform, onMissingChildLump, includeUnreferencedChildLumps);
        }

        private static IEnumerable<TraversedEntity> EnumerateEntitiesIterator(
            VEntityLump lump,
            IFileLoader fileLoader,
            Matrix4x4 rootTransform,
            Action<string>? onMissingChildLump,
            bool includeUnreferencedChildLumps)
        {
            var childLumps = new Dictionary<string, VEntityLump>();
            var visited = new HashSet<string>();
            if (!string.IsNullOrEmpty(lump.Name))
            {
                visited.Add(lump.Name);
            }

            foreach (var entity in Traverse(lump, fileLoader, rootTransform, fromTemplate: false, childLumps, visited, onMissingChildLump))
            {
                yield return entity;
            }

            if (!includeUnreferencedChildLumps)
            {
                yield break;
            }

            while (true)
            {
                var unreferenced = childLumps.FirstOrDefault(pair => !visited.Contains(pair.Key));
                if (string.IsNullOrEmpty(unreferenced.Key))
                {
                    yield break;
                }

                visited.Add(unreferenced.Key);

                foreach (var entity in Traverse(unreferenced.Value, fileLoader, rootTransform, fromTemplate: false, childLumps, visited, onMissingChildLump))
                {
                    yield return entity;
                }
            }
        }

        // Lazily mutates childLumps/visited during enumeration; safe because both consumers materialize the result.
        private static IEnumerable<TraversedEntity> Traverse(
            VEntityLump lump,
            IFileLoader fileLoader,
            Matrix4x4 parentTransform,
            bool fromTemplate,
            Dictionary<string, VEntityLump> childLumps,
            HashSet<string> visited,
            Action<string>? onMissingChildLump)
        {
            foreach (var childLumpName in lump.GetChildEntityNames())
            {
                using var childResource = fileLoader.LoadFileCompiled(childLumpName);
                var childLump = childResource?.DataBlock as VEntityLump;

                if (childLump != null)
                {
                    // shared so nested templates can reach lumps registered higher up
                    childLumps.TryAdd(childLump.Name, childLump);
                }
            }

            foreach (var entity in lump.GetEntities())
            {
                yield return new TraversedEntity(entity, parentTransform, fromTemplate);

                if (entity.GetStringProperty("classname") != "point_template")
                {
                    continue;
                }

                // empty when the template has no compiled children
                var entityLumpName = entity.GetStringProperty("entitylumpname");

                if (string.IsNullOrEmpty(entityLumpName))
                {
                    continue;
                }

                if (childLumps.TryGetValue(entityLumpName, out var templateLump))
                {
                    // guard against a malformed template cycle (A's lump references B's, B's back to A)
                    if (!visited.Add(entityLumpName))
                    {
                        continue;
                    }

                    var childTransform = EntityTransformHelper.ToRigidTransformationMatrix(entity) * parentTransform;

                    foreach (var nested in Traverse(templateLump, fileLoader, childTransform, fromTemplate: true, childLumps, visited, onMissingChildLump))
                    {
                        yield return nested;
                    }
                }
                else
                {
                    onMissingChildLump?.Invoke(entityLumpName);
                }
            }
        }
    }
}
