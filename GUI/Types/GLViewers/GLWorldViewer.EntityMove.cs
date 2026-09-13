using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers;

partial class GLWorldViewer
{
    private EntityMoveForm? EntityMoveWindow;
    private EntityMoveSession? EntityMoves;
    private BaseEntity? EntityMoveTarget;

    private void AddEntityMoveControl()
    {
        var button = new ThemedButton { Text = "Move entity...", AutoSize = true };
        button.Click += (_, _) => ShowEntityMoveWindow();
        UiControl!.AddControl(button);
    }

    private void ShowEntityMoveWindow(BaseEntity? target = null)
    {
        if (LoadedWorld == null)
        {
            return;
        }

        EntityMoves ??= new(Renderer.EntitySystem);
        if (EntityMoveWindow == null)
        {
            EntityMoveWindow = new(UseSelectedMoveEntity, MoveEntityToOrigin, RestoreEntityMove, FocusMoveEntity);
            EntityMoveWindow.FormClosed += (_, _) => EntityMoveWindow = null;
            EntityMoveWindow.Show(Program.MainForm);
        }

        if (target != null)
        {
            EntityMoveTarget = target;
            RefreshEntityMoveWindow();
        }
        else if (EntityMoveTarget == null)
        {
            UseSelectedMoveEntity();
        }
        else
        {
            RefreshEntityMoveWindow();
        }

        EntityMoveWindow.Activate();
    }

    private void UseSelectedMoveEntity()
    {
        List<SceneNode> nodes;
        using (MakeCurrent())
        {
            nodes = SelectedNodeRenderer?.SelectedNodes
                .Where(static node => IsMovableEntity(node.EntityInstance))
                .DistinctBy(static node => node.EntityInstance)
                .ToList() ?? [];
        }

        var target = ChooseMoveEntity(nodes);
        if (target == null)
        {
            if (nodes.Count == 0)
            {
                EntityMoveWindow?.ShowError("Select a map entity in MAP or Entity List first.");
            }

            return;
        }

        EntityMoveTarget = target;
        RefreshEntityMoveWindow();
    }

    private static BaseEntity? ChooseMoveEntity(List<SceneNode> nodes)
    {
        if (nodes is [var node])
        {
            return node.EntityInstance;
        }

        if (nodes.Count == 0)
        {
            return null;
        }

        using var chooser = new EntitySelectionForm(nodes, "entities to move");
        return chooser.ShowDialog(Program.MainForm) == DialogResult.OK ? chooser.SelectedNode?.EntityInstance : null;
    }

    private void ConfigureEntityMoveButton(EntityInfoForm form, EntityLump.Entity? entity)
    {
        if (LoadedWorld == null)
        {
            return;
        }

        if (form.MoveEntityButton == null)
        {
            form.AddMoveEntityButton((_, _) => MoveEntityFromInfo());
        }

        form.MoveEntityButton!.Visible = entity != null && GetLinkableEntities().Contains(entity);
    }

    private void MoveEntityFromInfo()
    {
        List<BaseEntity> entities;
        List<SceneNode> nodes;
        using (MakeCurrent())
        {
            entities = Renderer.EntitySystem.Entities
                .Where(candidate => candidate.Data == entityInfoEntity && IsMovableEntity(candidate)).ToList();
            nodes = entities.Select(static entity => entity.RootNode).OfType<SceneNode>().ToList();
        }

        var target = entities is [var entity] ? entity : ChooseMoveEntity(nodes);
        if (target != null)
        {
            ShowEntityMoveWindow(target);
        }
    }

    private string? MoveEntityToOrigin(string input, bool moveDescendants)
    {
        if (!EntityTransformHelper.TryParseVector3(input, out var origin))
        {
            return "Enter three finite numbers: X Y Z.";
        }

        return EditEntityPosition(session =>
        {
            if (!IsMovableEntity(EntityMoveTarget))
            {
                throw new InvalidOperationException("Select a living map entity first.");
            }

            session.Move(EntityMoveTarget!, origin, moveDescendants);
            return EntityMoveTarget;
        });
    }

    private string? RestoreEntityMove(bool redo)
        => EditEntityPosition(session => redo ? session.Redo() : session.Undo());

    private string? EditEntityPosition(Func<EntityMoveSession, BaseEntity?> edit)
    {
        try
        {
            using (MakeCurrent())
            {
                // A queued Play can rebuild the scene; consume it before reading any target or history.
                ApplySimulationRequest();
                ApplySimulation(SimulationRequest.Pause);
                EntityMoves ??= new(Renderer.EntitySystem);
                EntityMoveTarget = edit(EntityMoves) ?? EntityMoveTarget;
            }

            RefreshEntityMoveWindow();
            ActivateRenderLoop();
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            RefreshEntityMoveWindow();
            return exception.Message;
        }
    }

    private void RefreshEntityMoveWindow()
    {
        if (EntityMoveWindow == null || EntityMoves == null)
        {
            return;
        }

        using var lockedGl = MakeCurrent();
        if (!IsMovableEntity(EntityMoveTarget))
        {
            EntityMoveTarget = null;
            EntityMoveWindow.UpdateState(null, null, 0, EntityMoves.Position, EntityMoves.Count);
            return;
        }

        var entity = EntityMoveTarget!;
        var hammerId = entity.Data!.GetStringProperty("hammeruniqueid", string.Empty);
        var description = string.IsNullOrEmpty(entity.TargetName) ? entity.Classname : $"{entity.TargetName} ({entity.Classname})";
        if (!string.IsNullOrEmpty(hammerId))
        {
            description += $"\nHammer ID: {hammerId}";
        }

        EntityMoveWindow.UpdateState(description, EntityMoveSession.GetViewerOrigin(entity), EntityMoves.GetDescendantCount(entity), EntityMoves.Position, EntityMoves.Count);
    }

    private void FocusMoveEntity()
    {
        using var lockedGl = MakeCurrent();
        if (IsMovableEntity(EntityMoveTarget) && EntityMoveTarget!.RootNode is { } node)
        {
            SelectAndFocusNode(node);
        }
        else if (IsMovableEntity(EntityMoveTarget))
        {
            FocusCameraOnBounds(PointBounds(EntityMoveSession.GetViewerOrigin(EntityMoveTarget!)));
            ActivateRenderLoopAfterLayout();
        }
    }

    private static bool IsMovableEntity(BaseEntity? entity)
        => entity is { IsRemoved: false, Data: not null } and not WorldEntity and not PlayerEntity;

    private void ClearEntityMoves()
    {
        EntityMoves?.Clear();
        EntityMoveTarget = null;
        if (EntityMoveWindow is { IsHandleCreated: true, IsDisposed: false } form)
        {
            form.BeginInvoke(RefreshEntityMoveWindow);
        }
    }

    private void DisposeEntityMoves()
    {
        if (EntityMoves == null && EntityMoveWindow == null)
        {
            return;
        }

        using var lockedGl = MakeCurrent();
        EntityMoveWindow?.Dispose();
        EntityMoveWindow = null;
        EntityMoves?.Clear();
        EntityMoves = null;
        EntityMoveTarget = null;
    }
}
