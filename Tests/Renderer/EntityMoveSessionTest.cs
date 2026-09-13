using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace Tests.Renderer;

public class EntityMoveSessionTest
{
    private sealed class TestNode(Scene scene) : SceneNode(scene)
    {
        public override bool IsPointMarker => true;
    }

    private sealed class TestVisuals : IEntityToolVisuals
    {
        public IEnumerable<string> GetResourcesToPreload(string classname) => [];

        public SceneNode CreateStandIn(BaseEntity entity, ObjectTypeFlags flags)
        {
            return new TestNode(entity.Scene) { LocalBoundingBox = new AABB(-Vector3.One, Vector3.One) };
        }

        public IEnumerable<SceneNode> CreateCollisionVisuals(Scene scene, PhysAggregateData physics, string name, BaseEntity? entity) => [];

        public void AddEntityRelations(Scene scene, IReadOnlyList<BaseEntity> entities)
        {
        }
    }

    private sealed class World : IDisposable
    {
        private readonly GameFileLoader Loader = new(null, null);
        private readonly RendererContext Context;
        public Scene Scene { get; }
        public EntitySystem System { get; }
        public EntityMoveSession Moves { get; }

        public World(bool useEditorVisuals = false)
        {
            Context = new(Loader, NullLogger.Instance);
            Scene = new(Context);
            System = new(Context)
            {
                Enabled = false,
                ToolVisuals = useEditorVisuals ? new HammerEntityVisuals(Loader, NullLogger.Instance) : new TestVisuals(),
            };
            Moves = new(System);
        }

        public BaseEntity Add(string name, string origin, string? parent = null, string angles = "0 0 0", Matrix4x4? placement = null, string classname = "info_target")
        {
            var data = new EntityLump.Entity { ParentLump = new EntityLump { Resource = new() } };
            data.Add("classname", classname);
            data.Add("targetname", name);
            data.Add("origin", origin);
            data.Add("angles", angles);
            if (parent != null)
            {
                data.Add("parentname", parent);
            }

            return CreateEntity(System, data, null, new SpawnGroup("maps/test", Scene, placement ?? Matrix4x4.Identity))!;
        }

        public void Activate()
        {
            SpawnEntities(System, [.. System.Entities]);
            System.ToolVisuals!.AddEntityRelations(Scene, System.Entities);
            System.Update(0f);
            Scene.UpdateOctrees();
        }

        public void Dispose()
        {
            Scene.Dispose();
            Context.Dispose();
            Loader.Dispose();
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "CreateEntity")]
    private static extern BaseEntity? CreateEntity(EntitySystem system, EntityLump.Entity data, string? layerName, SpawnGroup spawnGroup);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "SpawnEntities")]
    private static extern void SpawnEntities(EntitySystem system, List<BaseEntity> batch);

    [Test]
    public async Task MovingAndUndoingRefreshesUnselectedVolumeHelpersAndIncomingConnections()
    {
        using var world = new World(useEditorVisuals: true);
        var source = world.Add("source", "-100 0 0");
        var volume = world.Add("volume", "10 0 0", classname: "env_shake_volume");
        volume.Data!.Add("box_mins", "-16 -16 -16");
        volume.Data.Add("box_maxs", "16 16 16");
        source.Data!.Connections =
        [
            new EntityLump.Connection
            {
                SourceEntity = source.Data,
                TargetName = "volume",
                OutputName = "OnTrigger",
                InputName = "Enable",
                OverrideParam = string.Empty,
                Delay = 0f,
                TimesToFire = -1,
                TargetType = EntityIOTargetType.EntityName,
            },
        ];
        world.Activate();
        var helper = world.Scene.AllNodes.OfType<ToolSceneNode>()
            .Single(node => node.EntityInstance == volume && node.LayerName == HammerEntityVisuals.HelpersLayerName);
        var connection = world.Scene.AllNodes.OfType<ToolSceneNode>()
            .Single(node => node.LayerName == HammerEntityVisuals.ConnectionsLayerName);
        var helperBounds = helper.BoundingBox;
        var connectionBounds = connection.BoundingBox;

        world.Moves.Move(volume, new(210f, 0f, 0f));

        await Assert.That(helper.BoundingBox.Center).IsEqualTo(helperBounds.Center + new Vector3(200f, 0f, 0f));
        await Assert.That(connection.BoundingBox.Max.X).IsEqualTo(210f);
        await Assert.That(connection.BoundingBox.Min.X).IsEqualTo(-100f);
        world.Moves.Undo();
        await Assert.That(helper.BoundingBox).IsEqualTo(helperBounds);
        await Assert.That(connection.BoundingBox).IsEqualTo(connectionBounds);
        world.Moves.Redo();
        await Assert.That(helper.BoundingBox.Center).IsEqualTo(helperBounds.Center + new Vector3(200f, 0f, 0f));
    }

    [Test]
    public async Task RefreshingEditorHelpersPreservesSkyMarkerScale()
    {
        using var world = new World(useEditorVisuals: true);
        var marker = world.Add("marker", "10 0 0", classname: "env_message");
        world.Activate();
        typeof(Scene).GetProperty("MarkerScale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(world.Scene, 1f / 16f);
        var node = world.Scene.AllNodes.OfType<ToolSceneNode>().Single(node => node.EntityInstance == marker && node.KeepsMarkerSize);
        node.Transform = Matrix4x4.CreateScale(1f / 16f) * node.Transform;
        var size = node.BoundingBox.Size;

        world.Moves.Move(marker, new(210f, 0f, 0f));

        await Assert.That(node.BoundingBox.Size).IsEqualTo(size);
        await Assert.That(node.BoundingBox.Center).IsEqualTo(new Vector3(210f, 0f, 0f));
        world.Moves.Undo();
        await Assert.That(node.BoundingBox.Size).IsEqualTo(size);
    }

    [Test]
    public async Task MovingPausedParentPlacesEveryDescendantAndItsPickBounds()
    {
        using var world = new World();
        var parent = world.Add("parent", "10 20 30");
        var child = world.Add("child", "12 23 34", "parent");
        var grandchild = world.Add("grandchild", "13 26 39", "child");
        var unrelated = world.Add("unrelated", "100 200 300");
        world.Activate();
        var childLocal = child.Origin;
        var grandchildLocal = grandchild.Origin;

        world.Moves.Move(parent, new(110f, 220f, 330f));
        world.System.Update(0f);

        await AssertPosition(parent, new(110f, 220f, 330f));
        await AssertPosition(child, new(112f, 223f, 334f));
        await AssertPosition(grandchild, new(113f, 226f, 339f));
        await AssertPosition(unrelated, new(100f, 200f, 300f));
        await Assert.That(child.Origin).IsEqualTo(childLocal);
        await Assert.That(grandchild.Origin).IsEqualTo(grandchildLocal);
        await Assert.That(world.Scene.DynamicOctree.FindContaining(new(113f, 226f, 339f))).IsEqualTo(grandchild.RootNode);
        await Assert.That(parent.Data!.GetStringProperty("origin")).IsEqualTo("10 20 30");
    }

    [Test]
    public async Task MovingAParentedEntityUsesWorldCoordinatesWithoutMovingItsParentOrSibling()
    {
        using var world = new World();
        var parent = world.Add("parent", "10 20 30", angles: "0 90 0");
        var child = world.Add("child", "10 25 30", "parent");
        var grandchild = world.Add("grandchild", "10 26 30", "child");
        var sibling = world.Add("sibling", "8 20 30", "parent");
        world.Activate();
        var localAngles = child.Angles;

        world.Moves.Move(child, new(10f, 30f, 40f));

        await AssertPosition(parent, new(10f, 20f, 30f));
        await AssertPosition(child, new(10f, 30f, 40f));
        await AssertPosition(grandchild, new(10f, 31f, 40f));
        await AssertPosition(sibling, new(8f, 20f, 30f));
        await Assert.That(child.Angles).IsEqualTo(localAngles);
    }

    [Test]
    public async Task MovingWithoutDescendantsKeepsTheirWorldPosesAndRestoresBothModes()
    {
        using var world = new World();
        var parent = world.Add("parent", "10 20 30", angles: "0 90 0");
        var child = world.Add("child", "10 25 30", "parent");
        var grandchild = world.Add("grandchild", "10 26 30", "child");
        world.Activate();
        var childLocalOrigin = child.Origin;
        var childLocalAngles = child.Angles;
        var childWorldOrigin = child.WorldOrigin;
        var childWorldAngles = child.WorldAngles;
        var grandchildWorldOrigin = grandchild.WorldOrigin;

        world.Moves.Move(parent, new(20f, 20f, 30f), moveDescendants: false);

        await AssertPosition(parent, new(20f, 20f, 30f));
        await AssertPosition(child, childWorldOrigin);
        await Assert.That(child.WorldAngles).IsEqualTo(childWorldAngles);
        await AssertPosition(grandchild, grandchildWorldOrigin);
        await Assert.That(child.Origin).IsNotEqualTo(childLocalOrigin);

        world.Moves.Undo();
        await AssertPosition(parent, new(10f, 20f, 30f));
        await Assert.That(child.Origin).IsEqualTo(childLocalOrigin);
        await Assert.That(child.Angles).IsEqualTo(childLocalAngles);
        await AssertPosition(grandchild, grandchildWorldOrigin);

        world.Moves.Redo();
        await AssertPosition(parent, new(20f, 20f, 30f));
        await AssertPosition(child, childWorldOrigin);
        await Assert.That(child.WorldAngles).IsEqualTo(childWorldAngles);
        await AssertPosition(grandchild, grandchildWorldOrigin);
    }

    [Test]
    public async Task UndoAndRedoRestoreInterleavedParentAndChildMoves()
    {
        using var world = new World();
        var parent = world.Add("parent", "0 0 0");
        var child = world.Add("child", "1 2 3", "parent");
        world.Activate();

        world.Moves.Move(parent, new(10f, 0f, 0f));
        world.Moves.Move(child, new(20f, 2f, 3f));
        world.Moves.Undo();
        await AssertPosition(child, new(11f, 2f, 3f));
        world.Moves.Undo();
        await AssertPosition(parent, Vector3.Zero);
        await AssertPosition(child, new(1f, 2f, 3f));
        world.Moves.Redo();
        world.Moves.Redo();
        await AssertPosition(parent, new(10f, 0f, 0f));
        await AssertPosition(child, new(20f, 2f, 3f));
        await Assert.That(world.Moves.CanUndo).IsTrue();
        await Assert.That(world.Moves.CanRedo).IsFalse();
    }

    [Test]
    public async Task ANewMoveAfterUndoReplacesOnlyTheRedoBranch()
    {
        using var world = new World();
        var entity = world.Add("entity", "0 0 0");
        world.Activate();
        world.Moves.Move(entity, new(10f, 0f, 0f));
        world.Moves.Move(entity, new(20f, 0f, 0f));
        world.Moves.Undo();
        world.Moves.Move(entity, new(30f, 0f, 0f));

        await Assert.That(world.Moves.CanRedo).IsFalse();
        await Assert.That(world.Moves.Count).IsEqualTo(2);
        world.Moves.Undo();
        await AssertPosition(entity, new(10f, 0f, 0f));
        world.Moves.Redo();
        await AssertPosition(entity, new(30f, 0f, 0f));
    }

    [Test]
    public async Task MovingToTheCurrentOriginDoesNotDiscardRedo()
    {
        using var world = new World();
        var entity = world.Add("entity", "0 0 0");
        world.Activate();
        world.Moves.Move(entity, Vector3.UnitX);
        world.Moves.Undo();
        world.Moves.Move(entity, Vector3.Zero);

        await Assert.That(world.Moves.Count).IsEqualTo(1);
        await Assert.That(world.Moves.CanRedo).IsTrue();
    }

    [Test]
    public async Task RemovedEntitiesInvalidateHistoryBeforeAnyPoseIsRestored()
    {
        using var world = new World();
        var parent = world.Add("parent", "0 0 0");
        var child = world.Add("child", "1 0 0", "parent");
        world.Activate();
        world.Moves.Move(parent, new(10f, 0f, 0f));
        world.System.Remove(child);

        await Assert.That(() => world.Moves.Undo()).Throws<InvalidOperationException>();
        await AssertPosition(parent, new(10f, 0f, 0f));
        await Assert.That(world.Moves.CanUndo).IsFalse();
        await Assert.That(world.Moves.CanRedo).IsFalse();
    }

    [Test]
    public async Task PostProcessingVolumesFollowTheirParentThroughUndoAndRedo()
    {
        using var world = new World();
        var parent = world.Add("parent", "0 0 0");
        var volume = (PostProcessingVolume)world.Add("volume", "1 2 3", "parent", classname: "post_processing_volume");
        world.Activate();

        world.Moves.Move(parent, new(10f, 0f, 0f));
        await Assert.That(volume.Volume!.Transform.Translation).IsEqualTo(new Vector3(11f, 2f, 3f));
        world.Moves.Undo();
        await Assert.That(volume.Volume.Transform.Translation).IsEqualTo(new Vector3(1f, 2f, 3f));
        world.Moves.Redo();
        await Assert.That(volume.Volume.Transform.Translation).IsEqualTo(new Vector3(11f, 2f, 3f));
    }

    [Test]
    public async Task NewlyParentedEntitiesInvalidateAnOlderHierarchySnapshot()
    {
        using var world = new World();
        var parent = world.Add("parent", "0 0 0");
        world.Activate();
        world.Moves.Move(parent, new(10f, 0f, 0f));
        var child = world.Add("child", "11 0 0", "parent");
        world.Activate();

        await Assert.That(() => world.Moves.Undo()).Throws<InvalidOperationException>();
        await AssertPosition(parent, new(10f, 0f, 0f));
        await AssertPosition(child, new(11f, 0f, 0f));
        await Assert.That(world.Moves.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ViewerCoordinatesIncludeSkyScaleAndTheExistingSpawnPlacement()
    {
        using var world = new World();
        typeof(Scene).GetProperty(nameof(Scene.ToViewerWorld))!.SetValue(world.Scene,
            Matrix4x4.CreateScale(16f) * Matrix4x4.CreateTranslation(1000f, 2000f, 3000f));
        var placement = Matrix4x4.CreateTranslation(100f, -100f, 0f);
        var parent = world.Add("parent", "1 2 3", placement: placement);
        var child = world.Add("child", "2 2 3", "parent", placement: placement);
        world.Activate();

        await Assert.That(EntityMoveSession.GetViewerOrigin(parent)).IsEqualTo(new Vector3(2616f, 432f, 3048f));
        world.Moves.Move(parent, new(3000f, 4000f, 5000f));
        await AssertPosition(parent, new(125f, 125f, 125f));
        await Assert.That(EntityMoveSession.GetViewerOrigin(child)).IsEqualTo(new Vector3(3016f, 4000f, 5000f));
        world.Moves.Undo();
        await AssertPosition(parent, new(101f, -98f, 3f));
        world.Moves.Redo();
        await AssertPosition(child, new(126f, 125f, 125f));
    }

    [Test]
    public async Task MovingOneOfTwoEquallyNamedEntitiesUsesInstanceIdentity()
    {
        using var world = new World();
        var first = world.Add("same_name", "0 0 0");
        var second = world.Add("same_name", "5 0 0");
        world.Activate();
        world.Moves.Move(second, new(10f, 0f, 0f));

        await AssertPosition(first, Vector3.Zero);
        await AssertPosition(second, new(10f, 0f, 0f));
        world.Moves.Undo();
        await AssertPosition(second, new(5f, 0f, 0f));
    }

    [Test]
    public async Task RejectsRunningSimulationAndNonFiniteDestinations()
    {
        using var world = new World();
        var entity = world.Add("entity", "0 0 0");
        world.Activate();
        world.System.Enabled = true;
        await Assert.That(() => world.Moves.Move(entity, Vector3.One)).Throws<InvalidOperationException>();
        world.System.Enabled = false;
        await Assert.That(() => world.Moves.Move(entity, new(float.NaN, 0f, 0f))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => world.Moves.Move(entity, new(0f, float.PositiveInfinity, 0f))).Throws<ArgumentOutOfRangeException>();
        await AssertPosition(entity, Vector3.Zero);
        await Assert.That(world.Moves.CanUndo).IsFalse();
    }

    private static async Task AssertPosition(BaseEntity entity, Vector3 expected)
    {
        await Assert.That(Vector3.Distance(entity.WorldOrigin, expected)).IsLessThan(0.001f);
        await Assert.That(Vector3.Distance(entity.RootNode!.Transform.Translation, expected)).IsLessThan(0.001f);
    }
}
