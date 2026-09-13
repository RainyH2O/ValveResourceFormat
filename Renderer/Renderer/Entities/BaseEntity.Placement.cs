using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

public abstract partial class BaseEntity
{
    // Explicit pose edits also have to place still children and attachment nodes while simulation is paused.
    internal virtual void RefreshPlacement()
    {
        SnapInterpolation();

        if (parentFrameKind == ParentFrameKind.Attachment && MoveParent is BaseModelEntity { ModelNode: { } parentModel })
        {
            var local = Matrix4x4.CreateScale(EntityScale) * EntityTransformHelper.ToRigidTransformationMatrix(Angles, Origin);
            var frame = parentModel.GetChildFrame(parentAttachmentName);

            foreach (var node in ownedNodes)
            {
                if (node.Parent == parentModel)
                {
                    node.LocalTransform = node.ApplyPlacementScale(local);
                    node.Transform = node.LocalTransform * frame;
                    Scene.DynamicOctree.Update(node);
                }
            }
        }

        Update();
    }
}
