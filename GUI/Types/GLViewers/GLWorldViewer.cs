using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.GameplayFlow;
using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Picking;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static GUI.Controls.SavedCameraPositionsControl;
using static ValveResourceFormat.Renderer.PickingTexture;
using GameplayFlowModel = GUI.Types.GameplayFlow.GameplayFlow;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
    partial class GLWorldViewer : GLSceneViewer
    {
        private readonly World? world;
        private readonly WorldNode? worldNode;
        private readonly ResourceExtRefList? mapExternalReferences;
        private CheckedListBox? worldLayersComboBox;
        private CheckedListBox? physicsGroupsComboBox;
        private ComboBox? cameraComboBox;
        private SavedCameraPositionsControl? savedCameraPositionsControl;
        private EntityInfoForm? entityInfoForm;
        private ThemedButton? playPauseButton;
        private ThemedButton? stopButton;
        private System.Drawing.Image? playImage;
        private System.Drawing.Image? pauseImage;
        private System.Drawing.Image? stopImage;
        private ThemedToggleButton? toolEntitiesButton;
        private ThemedToggleButton? toolMaterialsButton;
        private readonly ToolsVisibility tools = new();
        private volatile HashSet<string> chosenPhysicsGroups = [];
        private SimulationState simulationState;
        private int simulationRequest;
        private EntityListForm? entityListForm;
        private CoordinateMarkerForm? coordinateMarkerForm;
        private CoordinateMarkerSession? coordinateMarkerSession;
        private GameplayFlowForm? gameplayFlowForm;
        private GameplayFlowSession? gameplayFlowSession;
        private GameplayFlowStateAdapter? gameplayFlowStateAdapter;
        private string EntityListExportPath = string.Empty;
        private bool ignoreLayersChangeEvents = true;
        private string[] unlistedEnabledLayers = [];
        private List<Matrix4x4> CameraMatrices = [];
        private WorldNodeLoader? LoadedWorldNode;
        public WorldLoader? LoadedWorld;
        private EntityLump.Entity? entityInfoEntity;
        private Action<IReadOnlyCollection<EntityLump.Entity>>? selectEntitiesInGraph;
        private bool selectingEntities;
        private SelectionHighlightSettings selectionHighlightSettings = SelectionHighlightSettings.Default;
        private SelectionHighlightSettingsForm? selectionHighlightSettingsForm;

        private enum SimulationState
        {
            /// <summary>The world rests as it loaded.</summary>
            Stopped,

            /// <summary>Entity logic runs.</summary>
            Playing,

            /// <summary>Entity logic is held where it was.</summary>
            Paused,
        }

        private enum SimulationRequest
        {
            None,
            Play,
            Pause,

            /// <summary>Pause while playing, otherwise play.</summary>
            PlayOrPause,
            Stop,
        }

        /// <summary>Jump from the entity info popup to the entity's node in the I/O graph tab, when the map has one.</summary>
        public Func<EntityLump.Entity, bool>? ShowEntityInGraph { get; set; }

        /// <summary>Whether the I/O graph tab has a node for an entity. Set together with <see cref="ShowEntityInGraph"/>.</summary>
        public Func<EntityLump.Entity, bool>? EntityHasGraphNode { get; set; }

        /// <summary>Synchronizes the map selection into the entity I/O graph, when one is open.</summary>
        public Action<IReadOnlyCollection<EntityLump.Entity>>? SelectEntitiesInGraph
        {
            get => selectEntitiesInGraph;
            set
            {
                selectEntitiesInGraph = value;
                SynchronizeEntitySelection();
            }
        }

        public GLWorldViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, World world, ResourceExtRefList? externalReferences = null)
            : base(vrfGuiContext, rendererContext)
        {
            this.world = world;
            mapExternalReferences = externalReferences;
        }

        public GLWorldViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, WorldNode worldNode, ResourceExtRefList? externalReferences = null)
            : base(vrfGuiContext, rendererContext)
        {
            this.worldNode = worldNode;
            mapExternalReferences = externalReferences;
        }

        public override void Dispose()
        {
            if (gameplayFlowForm != null)
            {
                gameplayFlowForm.Close();
                gameplayFlowForm?.CloseWithoutPrompt();
            }

            gameplayFlowStateAdapter?.Dispose();
            gameplayFlowStateAdapter = null;
            gameplayFlowSession = null;

            DisposeEntityMoves();

            if (SelectedNodeRenderer != null)
            {
                SelectedNodeRenderer.SelectionChanged -= OnMapSelectionChanged;
            }

            if (selectionHighlightSettingsForm != null)
            {
                selectionHighlightSettingsForm.FormClosed -= OnSelectionHighlightSettingsClosed;
                selectionHighlightSettingsForm.Dispose();
                selectionHighlightSettingsForm = null;
            }

            SelectedNodeRenderer?.ClearPinnedNodes();
            base.Dispose();

            worldLayersComboBox?.Dispose();
            physicsGroupsComboBox?.Dispose();
            cameraComboBox?.Dispose();
            savedCameraPositionsControl?.Dispose();
            entityInfoForm?.Dispose();
            playPauseButton?.Dispose();
            stopButton?.Dispose();
            playImage?.Dispose();
            pauseImage?.Dispose();
            stopImage?.Dispose();
            toolEntitiesButton?.Dispose();
            toolMaterialsButton?.Dispose();
            entityListForm?.Dispose();
            coordinateMarkerForm?.Dispose();
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();

            Debug.Assert(SelectedNodeRenderer != null);
            ApplySelectionHighlightSettings(selectionHighlightSettings);
            SelectedNodeRenderer.SelectionChanged += OnMapSelectionChanged;
        }

        protected override bool UseSelectionVisuals => false;

        public Scene? SkyboxScene => Renderer.SkyGroup?.Scene;

        private void AddSceneExposureSlider()
        {
            Debug.Assert(UiControl != null);

            var exposureLabel = new Label();
            void UpdateExposureText(float exposure)
            {
                exposureLabel.Text = $"Exposure: {exposure:0.00}";
            }

            UiControl.AddControl(exposureLabel);

            var exposureSlider = UiControl.AddTrackBar("Exposure", (exposureAmount) =>
            {
                var exposure = exposureAmount * 10;
                UpdateExposureText(exposure);
                Renderer.Postprocess.CustomExposure = exposure;

                // also set auto exposure to off for debugging, in case this is slowing things down
                Renderer.Postprocess.State = Renderer.Postprocess.State with { ExposureSettings = Renderer.Postprocess.State.ExposureSettings with { AutoExposureEnabled = false } };
            });

            var sceneExposure = Renderer.Postprocess.CurrentExposure;
            exposureSlider.Slider.Value = sceneExposure / 10;
            UpdateExposureText(sceneExposure);
        }

        private void ShowSelectionHighlightSettings()
        {
            if (selectionHighlightSettingsForm != null)
            {
                selectionHighlightSettingsForm.Activate();
                return;
            }

            selectionHighlightSettingsForm = new(selectionHighlightSettings, ApplySelectionHighlightSettings);
            selectionHighlightSettingsForm.FormClosed += OnSelectionHighlightSettingsClosed;
            selectionHighlightSettingsForm.Show(Program.MainForm);
        }

        private void OnSelectionHighlightSettingsClosed(object? sender, FormClosedEventArgs e)
        {
            if (selectionHighlightSettingsForm != null)
            {
                selectionHighlightSettingsForm.FormClosed -= OnSelectionHighlightSettingsClosed;
                selectionHighlightSettingsForm.Dispose();
                selectionHighlightSettingsForm = null;
            }
        }

        private void ApplySelectionHighlightSettings(SelectionHighlightSettings settings)
        {
            selectionHighlightSettings = settings;
            Renderer.Postprocess.SelectionHighlightSettings = settings;

            if (SelectedNodeRenderer != null)
            {
                SelectedNodeRenderer.HighlightSettings = settings;
            }
        }

        private void OnGetOrSetPositionFromClipboardRequest(object? sender, bool isSetRequest)
        {
            if (!isSetRequest)
            {
                var loc = Renderer.Camera.Location;
                var cameraAngles = Renderer.Camera.GetQAngle();

                AppClipboard.SetText(string.Create(CultureInfo.InvariantCulture, $"setpos {loc.X:F6} {loc.Y:F6} {loc.Z:F6}; setang {cameraAngles.X:F6} {cameraAngles.Y:F6} {cameraAngles.Z:F6}"));

                return;
            }

            var text = AppClipboard.GetText();
            var pos = Regexes.SetPos().Match(text);
            var ang = Regexes.SetAng().Match(text);

            if (!pos.Success)
            {
                Log.Error(nameof(GLWorldViewer), "Failed to find setpos in clipboard text.");
                return;
            }

            var x = float.Parse(pos.Groups["x"].Value, CultureInfo.InvariantCulture);
            var y = float.Parse(pos.Groups["y"].Value, CultureInfo.InvariantCulture);
            var z = float.Parse(pos.Groups["z"].Value, CultureInfo.InvariantCulture);

            var viewAngles = ang.Success
                ? new Vector3(
                    float.Parse(ang.Groups["pitch"].Value, CultureInfo.InvariantCulture),
                    float.Parse(ang.Groups["yaw"].Value, CultureInfo.InvariantCulture),
                    0f)
                : Vector3.Zero;

            Input.SaveCameraForTransition(exitWalkMode: false);
            Input.Camera.SetLocation(new Vector3(x, y, z));
            Input.Camera.SetFromQAngle(viewAngles);
        }

        private void OnRestoreCameraRequest(object? sender, RestoreCameraRequestEvent e)
        {
            if (Settings.Config.SavedCameras.TryGetValue(e.Camera, out var savedFloats))
            {
                if (savedFloats.Length == 5)
                {
                    Input.SaveCameraForTransition();

                    // Saved cameras predate the camera holding pitch the engine's way round, and are
                    // stored positive upwards, so already saved ones keep pointing where they did.
                    Input.Camera.SetLocationPitchYaw(
                        new Vector3(savedFloats[0], savedFloats[1], savedFloats[2]),
                        -savedFloats[3],
                        savedFloats[4]);
                }
            }
        }

        private void OnSaveCameraRequest(object? sender, EventArgs e)
        {
            var cam = Renderer.Camera;
            var saveName = $"Camera at {cam.Location.X:F0} {cam.Location.Y:F0} {cam.Location.Z:F0}";
            var originalName = saveName;
            var duplicateCameraIndex = 1;

            while (Settings.Config.SavedCameras.ContainsKey(saveName))
            {
                saveName = $"{originalName} (#{duplicateCameraIndex++})";
            }

            Settings.Config.SavedCameras.Add(saveName, [cam.Location.X, cam.Location.Y, cam.Location.Z, -cam.Pitch, cam.Yaw]);
            Settings.InvokeRefreshCamerasOnSave();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();

            if (worldNode != null)
            {
                base.LoadDefaultLighting();

                Scene.LightingInfo.CubemapType = CubemapType.None; // default envmap actually looks bad
                Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;
                Scene.LightingInfo.SunLightShadowCoverageScale = 2f;

                var post = new ScenePostProcessVolume(Scene)
                {
                    HasBloom = true,
                    IsMaster = true,
                };

                Scene.PostProcessInfo.AddPostProcessVolume(post);
            }
        }

        protected override void LoadScene()
        {
            Renderer.EntitySystem.Enabled = !GuiContext.DisableEntitySimulationOnOpen;
            var cameraSet = false;

            InitializeSoundPlayer();

            if (world != null)
            {
                Renderer.EntitySystem.Enabled = false;

                LoadedWorld = new WorldLoader(world, Scene, Renderer.EntitySystem)
                {
                    LoadingProgress = GuiContext.LoadingProgress,
                    DefaultEnabledLayers = { HammerEntityVisuals.MarkerLayerName },
                };

                LoadedWorld.Load(mapExternalReferences);

                foreach (var spawnGroup in LoadedWorld.SpawnGroups)
                {
                    Renderer.AddSpawnGroup(spawnGroup);
                }

                NavMeshSceneNode.AddNavNodesToScene(LoadedWorld.NavMesh, Scene, LoadedWorld.NavigationHullNames);
                NavSpaceSceneNode.AddToScene(LoadedWorld.NavSpace, Scene);
                NavMeshSceneNode.AddFlowMapNodesToScene(LoadedWorld.NavFlowMap, LoadedWorld.NavMesh, Scene, LoadedWorld.NavigationHullNames);
                CS2BombDamageSceneNode.AddBakedBombDamageToScene(LoadedWorld.BombDamage, Scene);

                if (LoadedWorld.CameraMatrices.Count > 0)
                {
                    CameraMatrices = LoadedWorld.CameraMatrices;
                }

                if (LoadedWorld.SpawnCameraMatrix is { } spawn)
                {
                    Input.Camera.SetFromTransformMatrix(spawn);
                    cameraSet = true;
                }

                ReportLoadingStatus("Loading player model…");

                Input.TryLoadViewmodel(Scene, Renderer.EntitySystem);

                var kzMapPrefixes = new[] { "bhop", "surf", "kz", "dr" };
                var mapName = Path.GetFileName(LoadedWorld.MapName);
                var isKzMap = kzMapPrefixes.Any(prefix => mapName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                ShowSpeed = isKzMap;
                Input.PlayerMovement.PrestrafeEnabled = isKzMap;
                Input.PlayerMovement.AutoBunnyHop = isKzMap;
                Input.PlayerMovement.AirAccelerate = isKzMap
                    ? PlayerMovement.AirAccelerateMovementMaps
                    : PlayerMovement.AirAccelerateCompetitive;

                Input.EntitySystem = Renderer.EntitySystem;
                Renderer.EntitySystem.SpawnPlayer(Input.PlayerMovement, Scene);
            }

            if (!cameraSet)
            {
                Input.Camera.SetLocation(new Vector3(256));
                Input.Camera.LookAt(Vector3.Zero);
            }

            if (worldNode != null)
            {
                ReportLoadingStatus("Loading world geometry…");

                LoadedWorldNode = new WorldNodeLoader(Scene.RendererContext, worldNode, mapExternalReferences);
                LoadedWorldNode.Load(Scene);
            }
        }

        protected override bool PrewarmsRenderer => true;

        protected override void OnFirstPaint()
        {
            Input.MoveCamera(new Vector3(0, -150f, 0));

            base.OnFirstPaint();

            Input.MoveCamera(new Vector3(0, 150f, 0), transition: true);
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Render"))
            {
                AddRenderModeSelectionControl();
                AddWireframeToggleControl();
            }

            using (UiControl.BeginGroup("Tools"))
            {
                var selectionHighlightButton = new ThemedButton
                {
                    Text = "Selection display...",
                    AutoSize = true,
                };
                selectionHighlightButton.Click += (_, _) => ShowSelectionHighlightSettings();
                UiControl.AddControl(selectionHighlightButton);

                var coordinateMarkersButton = new ThemedButton
                {
                    Text = "Coordinate markers...",
                    AutoSize = true,
                };
                coordinateMarkersButton.Click += (_, _) => ShowCoordinateMarkerForm();
                UiControl.AddControl(coordinateMarkersButton);

                AddEntityMoveControl();

                var gameplayFlowsButton = new ThemedButton
                {
                    Text = "Gameplay flows...",
                    AutoSize = true,
                };
                gameplayFlowsButton.Click += (_, _) => ShowGameplayFlowForm();
                UiControl.AddControl(gameplayFlowsButton);
            }

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledLayers([.. worldLayers, .. unlistedEnabledLayers]);
            });
            physicsGroupsComboBox = UiControl.AddMultiSelection(
                "Physics Groups",
                null,
                (physicsGroups) =>
                {
                    if (ignoreLayersChangeEvents)
                    {
                        return;
                    }

                    SetEnabledPhysicsGroups([.. physicsGroups]);
                },
                height: 250,
                checkAllPredicate: item => item is not string value
                    || value != PhysicsRenderAsOpaque
                        && !Scene.AllNodes.OfType<PhysSceneNode>().Any(node => node.IsDefaultGroup && node.PhysGroupName == value));

            using (UiControl.BeginGroup("Camera"))
            {
                savedCameraPositionsControl = new SavedCameraPositionsControl();
                savedCameraPositionsControl.SaveCameraRequest += OnSaveCameraRequest;
                savedCameraPositionsControl.RestoreCameraRequest += OnRestoreCameraRequest;
                savedCameraPositionsControl.GetOrSetPositionFromClipboardRequest += OnGetOrSetPositionFromClipboardRequest;
                UiControl.AddControl(savedCameraPositionsControl);

                if (LoadedWorld != null && LoadedWorld.CameraMatrices.Count > 0)
                {
                    cameraComboBox = UiControl.AddSelection("Map Camera", (cameraName, index) =>
                    {
                        if (index > 0)
                        {
                            Input.SaveCameraForTransition();
                            Input.Camera.SetFromTransformMatrix(CameraMatrices[index - 1]);
                        }
                    });
                    cameraComboBox.BeginUpdate();
                    cameraComboBox.Items.Add("Set view to camera…");
                    cameraComboBox.Items.AddRange([.. LoadedWorld.CameraNames]);
                    cameraComboBox.SelectedIndex = 0;
                    cameraComboBox.EndUpdate();
                }
            }

            if (world != null)
            {
                AddSimulationToolbar();

                var uniqueWorldLayers = new HashSet<string>(4);
                var uniquePhysicsGroups = new HashSet<string>();

                foreach (var node in Scene.AllNodes)
                {
                    if (node.LayerName != null && Scene.IsInternalLayer(node.LayerName))
                    {
                        continue;
                    }

                    if (node.LayerName != null)
                    {
                        uniqueWorldLayers.Add(node.LayerName);
                    }

                    if (node is PhysSceneNode physSceneNode)
                    {
                        uniquePhysicsGroups.Add(physSceneNode.PhysGroupName);
                    }
                }

                if (uniqueWorldLayers.Count > 0 && LoadedWorld != null)
                {
                    Debug.Assert(worldLayersComboBox != null);

                    worldLayersComboBox.BeginUpdate();

                    SetAvailableLayers(uniqueWorldLayers);

                    // Layers that have no nodes yet, such as particles spawned later, are not listed and stay enabled
                    unlistedEnabledLayers = [.. LoadedWorld.DefaultEnabledLayers.Where(layer => !uniqueWorldLayers.Contains(layer))];

                    foreach (var worldLayer in LoadedWorld.DefaultEnabledLayers)
                    {
                        var checkboxIndex = worldLayersComboBox.FindStringExact(worldLayer);

                        if (checkboxIndex > -1)
                        {
                            worldLayersComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                        }
                    }

                    worldLayersComboBox.EndUpdate();

                    SetEnabledLayers(LoadedWorld.DefaultEnabledLayers);
                }

                if (uniquePhysicsGroups.Count > 0)
                {
                    Debug.Assert(physicsGroupsComboBox != null);

                    physicsGroupsComboBox.BeginUpdate();

                    SetAvailablePhysicsGroups(uniquePhysicsGroups);

                    // Groups of tools materials start shown, it is the tools materials switch that hides them
                    var toolsMaterialGroups = Scene.AllNodes
                        .OfType<PhysSceneNode>()
                        .Where(static node => node.IsToolsMaterial)
                        .Select(static node => node.PhysGroupName)
                        .ToHashSet();

                    foreach (var group in toolsMaterialGroups)
                    {
                        var checkboxIndex = physicsGroupsComboBox.FindStringExact(group);

                        if (checkboxIndex > -1)
                        {
                            physicsGroupsComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                        }
                    }

                    physicsGroupsComboBox.EndUpdate();

                    SetEnabledPhysicsGroups(toolsMaterialGroups);
                }

                using (UiControl.BeginGroup("World"))
                {
                    if (Renderer.SkyGroup != null)
                    {
                        UiControl.AddCheckBox("Show Skybox", Renderer.ShowSkybox, (v) => Renderer.ShowSkybox = v);
                    }

                    UiControl.AddCheckBox("Show Fog", Scene.FogEnabled, v => Scene.FogEnabled = v);

                    UiControl.AddCheckBox("Color Correction", Renderer.Postprocess.ColorCorrectionEnabled, v => Renderer.Postprocess.ColorCorrectionEnabled = v);

                    if (Scene.VoxelVisibility != null)
                    {
                        UiControl.AddCheckBox("PVS Culling", Scene.EnablePvsCulling, v => Scene.EnablePvsCulling = v);
                    }

                    if (Renderer.Scenes.Any(static scene => scene.VisibilityBoxes.Count > 0))
                    {
                        UiControl.AddCheckBox("Visibility Boxes", Scene.VisibilityBoxes.Enabled, v =>
                        {
                            foreach (var scene in Renderer.Scenes)
                            {
                                scene.VisibilityBoxes.Enabled = v;
                            }
                        });
                    }

                    CheckBox? occlusionCullingCheckBox = null;

                    if (GLEnvironment.SlowMultiDrawIndirect)
                    {
                        foreach (var scene in Renderer.Scenes)
                        {
                            scene.EnableIndirectDraws = false;
                        }
                    }

                    UiControl.AddCheckBox("GPU Culling", Scene.EnableIndirectDraws, v =>
                    {
                        foreach (var scene in Renderer.Scenes)
                        {
                            scene.EnableIndirectDraws = v;
                        }

                        if (occlusionCullingCheckBox != null)
                        {
                            occlusionCullingCheckBox.Enabled = v;
                        }
                    });

                    occlusionCullingCheckBox = UiControl.AddCheckBox("GPU Occlusion Culling", Scene.EnableOcclusionCulling, (v) => Scene.EnableOcclusionCulling = v);
                    occlusionCullingCheckBox.Enabled = Scene.EnableIndirectDraws;

                    UiControl.AddCheckBox("Depth Prepass", Scene.EnableDepthPrepass, (v) => Scene.EnableDepthPrepass = v);

                    UiControl.AddCheckBox("Barn Lights", Renderer.EnableBarnLights, v => Renderer.EnableBarnLights = v);
                    UiControl.AddCheckBox("Tiled Light Culling", Scene.EnableTiledLightCulling, v => Scene.EnableTiledLightCulling = v);

                    AddSceneExposureSlider();
                }
            }

            if (worldNode != null && worldLayersComboBox != null)
            {
                var worldLayers = Scene.AllNodes
                    .Select(static r => r.LayerName)
                    .OfType<string>()
                    .Distinct()
                    .ToList();
                SetAvailableLayers(worldLayers);

                for (var i = 0; i < worldLayersComboBox.Items.Count; i++)
                {
                    worldLayersComboBox.SetItemChecked(i, true);
                }
            }

            savedCameraPositionsControl.RefreshSavedPositions();

            ignoreLayersChangeEvents = false;

            base.AddUiControls();

            if (world != null)
            {
                AddDOFControls();
            }
        }

        public void AddDOFControls()
        {
            Debug.Assert(UiControl != null);

            var groupBoxPanel = new Panel
            {
                Height = UiControl.AdjustForDPI(230),
                Padding = new(0, 2, 0, 2),
            };

            var groupBox = new ThemedGroupBox
            {
                Text = "Depth Of Field",
                Dock = DockStyle.Fill,
                Padding = new(4, 8, 4, 4),
            };

            var controlsContainer = new Panel
            {
                Height = UiControl.AdjustForDPI(175),
                Dock = DockStyle.Top
            };

            void dofCheckBoxAction(bool v)
            {
                Renderer.Postprocess.DOF.Enabled = v;

                controlsContainer.Enabled = v;
                controlsContainer.Visible = v;

                var height = v ? 230 : 60;
                groupBoxPanel.Height = UiControl.AdjustForDPI(height);
            }

            dofCheckBoxAction(Renderer.Postprocess.DOF.Enabled);

            var checkBox = RendererControl.CreateCheckBox("Enabled", Renderer.Postprocess.DOF.Enabled, dofCheckBoxAction);
            checkBox.Dock = DockStyle.Top;

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Far blurry", val =>
            {
                Renderer.Postprocess.DOF.FarBlurry = val;
            }, Renderer.Postprocess.DOF.FarBlurry, 0, 10000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Far crisp", val =>
            {
                Renderer.Postprocess.DOF.FarCrisp = val;
            }, Renderer.Postprocess.DOF.FarCrisp, 0, 10000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Near blurry", val =>
            {
                Renderer.Postprocess.DOF.NearBlurry = -val;
            }, Renderer.Postprocess.DOF.NearBlurry, 0, 100));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Near crisp", val =>
            {
                Renderer.Postprocess.DOF.NearCrisp = val;
            }, Renderer.Postprocess.DOF.NearCrisp, 0, 1000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Max Blur Size", val =>
            {
                Renderer.Postprocess.DOF.MaxBlurSize = val;
            }, Renderer.Postprocess.DOF.MaxBlurSize, 0, 100));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Radius Scale", val =>
            {
                Renderer.Postprocess.DOF.RadScale = val;
            }, Renderer.Postprocess.DOF.RadScale, 0, 1));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Focal Distance", val =>
            {
                Renderer.Postprocess.DOF.FocalDistance = val;
            }, Renderer.Postprocess.DOF.FocalDistance, 0, 10000));

            foreach (Control control in controlsContainer.Controls)
            {
                control.Dock = DockStyle.Top;
                control.Margin = new Padding(4);
            }

            groupBox.Controls.Add(controlsContainer);
            groupBox.Controls.Add(checkBox);
            groupBoxPanel.Controls.Add(groupBox);

            UiControl.AddControl(groupBoxPanel);
        }

        public void SelectAndFocusEntity(EntityLump.Entity entity)
        {
            SelectContainingTabs();

            var node = FindEntityNode(entity);

            if (node == null)
            {
                // Tool entities (logic, sounds, finished particles) have no renderable
                // scene node; fly to the entity origin instead.
                FocusCameraOnBounds(EntityOriginBounds(entity));
                ActivateRenderLoopAfterLayout();
                return;
            }

            SelectAndFocusNode(node);
        }

        private SceneNode? FindEntityNode(EntityLump.Entity entity)
            => Renderer.FindNode(entity);

        internal IReadOnlyList<SceneNode> GetGameplayFlowSelectedNodes()
            => SelectedNodeRenderer?.SelectedNodes ?? [];

        internal void SelectGameplayFlowNodes(IReadOnlyList<SceneNode> nodes)
        {
            if (SelectedNodeRenderer == null)
            {
                return;
            }

            selectingEntities = true;
            try
            {
                SelectedNodeRenderer.SetSelectedNodes(nodes, forceDisableDepth: true);
            }
            finally
            {
                selectingEntities = false;
            }

            SynchronizeEntitySelection();
        }

        internal GameplayFlowAnnotation? CreateGameplayFlowAnnotationFromSelectedMarker()
        {
            if (coordinateMarkerSession == null
                || SelectedNodeRenderer?.SelectedNodes is not [var selectedNode]
                || coordinateMarkerSession.Markers.FirstOrDefault(marker => ReferenceEquals(marker.Node, selectedNode)) is not { } marker)
            {
                return null;
            }

            return new()
            {
                Name = marker.Entity.GetStringProperty("targetname"),
                Position = new(marker.Position.X, marker.Position.Y, marker.Position.Z),
                IsSelected = true,
            };
        }

        public void SelectAndFocusEntities(IReadOnlyList<EntityLump.Entity> entities)
        {
            if (entities.Count == 1)
            {
                SelectAndFocusEntity(entities[0]);
                return;
            }

            SelectContainingTabs();

            var hasBounds = false;
            var bounds = default(AABB);
            var nodesToSelect = new List<SceneNode>();

            selectingEntities = true;
            try
            {
                foreach (var entity in entities)
                {
                    var node = FindEntityNode(entity);

                    AABB entityBounds;

                    if (node != null)
                    {
                        nodesToSelect.Add(node);

                        EnsureNodeVisible(node);
                        entityBounds = SelectionBounds(node);
                    }
                    else
                    {
                        entityBounds = EntityOriginBounds(entity);
                    }

                    bounds = hasBounds ? bounds.Union(entityBounds) : entityBounds;
                    hasBounds = true;
                }
            }
            finally
            {
                selectingEntities = false;
            }

            if (nodesToSelect.Count > 0)
            {
                SelectedNodeRenderer?.SetSelectedNodes(nodesToSelect, forceDisableDepth: true);
            }

            SynchronizeEntitySelection(synchronizeGraph: false);

            if (hasBounds)
            {
                FocusCameraOnBounds(bounds);
                ActivateRenderLoopAfterLayout();
            }
        }

        internal void SelectAndFocusNode(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            SelectedNodeRenderer?.SelectNode(node, forceDisableDepth: true);
            FocusCameraOnBounds(SelectionBounds(node));
            EnsureNodeVisible(node);
            ActivateRenderLoopAfterLayout();
        }

        /// <summary>Bounds to focus on for an entity that has no scene node, from its authored origin.</summary>
        internal AABB EntityOriginBounds(EntityLump.Entity entity)
        {
            var origin = entity.GetVector3Property("origin");

            if (Renderer.SpawnGroups.FirstOrDefault(group => group.Entities.Contains(entity)) is { } spawnGroup)
            {
                origin = spawnGroup.EntityOriginToWorld(origin);
            }

            return PointBounds(origin);
        }

        private void EnsureNodeVisible(SceneNode node)
        {
            if (node.LayerName != null && ToolsVisibility.IsToolEntityLayer(node.LayerName) && !tools.ShowToolEntities)
            {
                tools.ShowToolEntities = true;
                toolEntitiesButton?.Checked = true;
                RequestVisibilityUpdate();
            }

            if (node is PhysSceneNode { IsToolsMaterial: true } && !tools.ShowToolMaterials)
            {
                tools.ShowToolMaterials = true;
                toolMaterialsButton?.Checked = true;
                RequestVisibilityUpdate();
            }

            if (!node.LayerEnabled && worldLayersComboBox != null && node.LayerName != null)
            {
                var layerId = worldLayersComboBox.Items.IndexOf(node.LayerName);

                if (layerId >= 0)
                {
                    worldLayersComboBox.SetItemChecked(layerId, true);
                }
            }

            if (node is PhysSceneNode physNode && !physNode.Enabled && physicsGroupsComboBox != null && physNode.PhysGroupName != null)
            {
                var physId = physicsGroupsComboBox.Items.IndexOf(physNode.PhysGroupName);

                if (physId >= 0)
                {
                    physicsGroupsComboBox.SetItemChecked(physId, true);
                }
            }
        }

        private void ShowSceneNodeDetails(SceneNode sceneNode)
        {
            if (sceneNode.EntityData != null)
            {
                ShowEntityDetails(sceneNode.EntityData);
                return;
            }

            var form = PrepareEntityInfoForm(null);
            var info = form.EntityInfoControl;

            form.Text = $"{sceneNode.GetType().Name}: {sceneNode.Name}";

            static string FormatVector(Vector3 vector)
            {
                return $"{vector.X:F2} {vector.Y:F2} {vector.Z:F2}";
            }

            static string ToRenderColor(Vector4 tint)
            {
                tint *= 255.0f;
                return $"{tint.X:F0} {tint.Y:F0} {tint.Z:F0}";
            }

            if (sceneNode is SceneAggregate.Fragment sceneFragment)
            {
                var material = sceneFragment.DrawCall.Material.Material;
                info.AddProperty("Shader", material.ShaderName);
                info.AddProperty("Material", material.Name, new LinkedFile(material.Name));
                info.AddProperty("Aggregate Model", sceneFragment.Name!, new LinkedFile(sceneFragment.Name!));

                var tris = sceneFragment.DrawCall.IndexCount / 3;
                if (sceneFragment.DrawCall.NumMeshlets > 0)
                {
                    var clusters = sceneFragment.DrawCall.NumMeshlets;
                    var trisPerCluster = tris / clusters;
                    info.AddProperty("Triangles / Clusters / Per Cluster", $"{tris} / {clusters} / {trisPerCluster}");
                }
                else
                {
                    info.AddProperty("Triangles", $"{tris}");
                }

                info.AddProperty("Model Tint", ToRenderColor(sceneFragment.DrawCall.TintColor));
                info.AddProperty("Model Alpha", $"{sceneFragment.DrawCall.TintColor.W:F6}");

                if (sceneFragment.TintAlpha != Vector4.One)
                {
                    info.AddProperty("Instance Tint", ToRenderColor(sceneFragment.TintAlpha));
                    info.AddProperty("Final Tint", ToRenderColor(sceneFragment.DrawCall.TintColor * sceneFragment.TintAlpha));
                }
            }
            else if (sceneNode is SceneClutter clutter)
            {
                var drawCalls = clutter.RenderMesh.DrawCallsOpaque;
                var tris = drawCalls.Sum(static drawCall => drawCall.IndexCount / 3);

                info.AddProperty("Clutter Model", clutter.Name!, new LinkedFile(clutter.Name!));

                foreach (var drawCall in drawCalls)
                {
                    var material = drawCall.Material.Material;
                    info.AddProperty("Shader", material.ShaderName);
                    info.AddProperty("Material", material.Name, new LinkedFile(material.Name));
                }

                info.AddProperty("Instances", $"{clutter.InstanceTransforms.Count}");
                info.AddProperty("Triangles", $"{(long)tris * clutter.InstanceTransforms.Count}");
                info.AddProperty("Triangles Per Instance", $"{tris}");
                info.AddProperty("Instance Tint", ToRenderColor(clutter.TintAlpha));
                info.AddProperty("Begin Cull Size", $"{clutter.BeginCullSize:G6}");
                info.AddProperty("End Cull Size", $"{clutter.EndCullSize:G6}");
            }
            else if (sceneNode is ModelSceneNode modelSceneNode)
            {
                info.AddProperty("Model", modelSceneNode.Name!, new LinkedFile(modelSceneNode.Name!));
                info.AddProperty("Model Tint", ToRenderColor(modelSceneNode.TintAlpha));
                info.AddProperty("Model Alpha", $"{modelSceneNode.Alpha:F6}");

                if (modelSceneNode.LightingOrigin.HasValue)
                {
                    info.AddProperty("Custom Lighting Origin", FormatVector(modelSceneNode.LightingOrigin.Value));
                }
            }
            else if (sceneNode is PhysSceneNode physSceneNode)
            {
                PhysicsDetails.AddNodeDetails(info, physSceneNode);
            }

            if (sceneNode.CubeMapPrecomputedHandshake > 0)
            {
                info.AddProperty("Cubemap Handshake", $"{sceneNode.CubeMapPrecomputedHandshake}");
            }

            if (sceneNode.LightProbeVolumePrecomputedHandshake > 0)
            {
                info.AddProperty("Light Probe Handshake", $"{sceneNode.LightProbeVolumePrecomputedHandshake}");
            }

            info.AddProperty("Flags", sceneNode.Flags.ToString());
            info.AddProperty("Layer", sceneNode.LayerName ?? string.Empty);

            if (Renderer.SpawnGroups.FirstOrDefault(group => group.Scene == sceneNode.Scene) is { } spawnGroup)
            {
                form.Text += SpawnGroupSuffix(spawnGroup);
            }

            info.ShowPopulatedTabs();
        }

        private void ShowEntityDetails(EntityLump.Entity entity)
        {
            var form = PrepareEntityInfoForm(entity);

            form.EntityInfoControl.PopulateFromEntity(GetLinkableEntities(), entity);

            var modelEntity = Renderer.EntitySystem.Entities.OfType<BaseModelEntity>().FirstOrDefault(candidate => candidate.Data == entity);

            if (modelEntity?.Physics != null)
            {
                PhysicsDetails.AddEntityDetails(form.EntityInfoControl, modelEntity, modelEntity.Physics);
            }

            var classname = entity.GetStringProperty("classname");
            var targetName = entity.FriendlyTargetName;
            form.Text = string.IsNullOrEmpty(targetName)
                ? $"Entity: {classname}"
                : $"Entity: {classname} ({targetName})";

            if (Renderer.SpawnGroups.FirstOrDefault(group => group.Entities.Contains(entity)) is { } spawnGroup)
            {
                form.Text += SpawnGroupSuffix(spawnGroup);
            }

            form.EntityInfoControl.ShowPopulatedTabs();
        }

        private static string SpawnGroupSuffix(SpawnGroup spawnGroup)
            => spawnGroup.WorldGroup != null ? " (in 3D skybox)" : $" (in {spawnGroup.MapName})";

        /// <summary>
        /// Opens the details window if it is not open yet, and empties it for <paramref name="entity"/>,
        /// or for a scene node that is not an entity when it is null.
        /// </summary>
        private EntityInfoForm PrepareEntityInfoForm(EntityLump.Entity? entity)
        {
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm();
                entityInfoForm.EntityInfoControl.LinkedResourceActivated += OnLinkedResourceActivated;
                entityInfoForm.EntityInfoControl.ConnectionFireRequested += OnConnectionFireRequested;
                entityInfoForm.EntityInfoControl.CanFireConnections = true;
                entityInfoForm.EntityInfoControl.PropertiesCellDoubleClick += OnEntityInfoPropertiesCellDoubleClick;
                entityInfoForm.EntityInfoControl.OutputsCellDoubleClick += OnEntityInfoOutputsCellDoubleClick;
                entityInfoForm.EntityInfoControl.InputsCellDoubleClick += OnEntityInfoInputsCellDoubleClick;

                if (ShowEntityInGraph != null)
                {
                    entityInfoForm.AddShowInGraphButton(OnShowInGraphButtonClick);
                }

                entityInfoForm.FormClosed += OnEntityInfoFormClosed;
                entityInfoForm.ShowInTaskbar = false;
                entityInfoForm.Show(Program.MainForm);
            }

            entityInfoEntity = entity;

            ConfigureEntityMoveButton(entityInfoForm, entity);

            if (entityInfoForm.ShowInGraphButton != null)
            {
                entityInfoForm.ShowInGraphButton.Visible = entity != null && (EntityHasGraphNode?.Invoke(entity) ?? false);
            }

            entityInfoForm.EntityInfoControl.Clear();

            return entityInfoForm;
        }

        private List<EntityLump.Entity> GetLinkableEntities()
        {
            if (LoadedWorld == null)
            {
                return [];
            }

            if (Renderer.SpawnGroups.Count == 0)
            {
                return LoadedWorld.Entities;
            }

            return [.. LoadedWorld.Entities, .. Renderer.SpawnGroups.SelectMany(static group => group.Entities)];
        }

        private void OnEntityInfoPropertiesCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (entityInfoForm == null
                || e.RowIndex < 0
                || sender is not DataGridView grid
                || grid.Columns["ColumnValue"]?.Index != e.ColumnIndex)
            {
                return;
            }

            if (grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is not string value || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (grid.Rows[e.RowIndex].Tag is LinkedFile { Path: var path }
                && Viewers.Resource.OpenExternalReference(GuiContext, path))
            {
                entityInfoForm.Close();
                return;
            }

            if (EntityTransformHelper.TryParseVector3(value, out _))
            {
                GoToCoordinate(value);
                return;
            }

            var node = FindNodeByHammerId(value);
            if (node != null)
            {
                SelectAndFocusNode(node);
                ShowSceneNodeDetails(node);
                return;
            }

            GoToEntityByName(value, showNotFound: false);
        }

        private void OnEntityInfoOutputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (entityInfoForm == null)
            {
                return;
            }

            if (e.RowIndex < 0 || GetConnection(entityInfoForm.EntityInfoControl.OutputsGrid.Rows[e.RowIndex]) is not { } connection)
            {
                return;
            }

            var nodes = ResolveOutputTargets(connection);
            if (nodes.Count == 0)
            {
                return;
            }

            if (nodes.Count > 1)
            {
                ShowEntitySelectionDialog(nodes, connection.TargetName, _ => SelectEntityInfoConnection(entityInfoForm.EntityInfoControl.InputsGrid, connection));
                return;
            }

            SelectAndFocusNode(nodes[0]);
            ShowSceneNodeDetails(nodes[0]);
            SelectEntityInfoConnection(entityInfoForm.EntityInfoControl.InputsGrid, connection);
        }

        private List<SceneNode> ResolveOutputTargets(EntityLump.Connection connection)
        {
            if (LoadedWorld == null)
            {
                var node = Renderer.FindNodeByTargetName(connection.TargetName);
                return node == null ? [] : [node];
            }

            var entities = new List<EntityLump.Entity>();
            var resolver = new EntityIOTargetResolver(GetLinkableEntities());
            resolver.Resolve(connection, entities);

            return entities
                .Select(FindEntityNode)
                .Where(static node => node != null)
                .Cast<SceneNode>()
                .Distinct<SceneNode>(ReferenceEqualityComparer.Instance)
                .ToList();
        }

        private void OnEntityInfoInputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (entityInfoForm == null)
            {
                return;
            }

            if (e.RowIndex < 0)
            {
                return;
            }

            if (GetConnection(entityInfoForm.EntityInfoControl.InputsGrid.Rows[e.RowIndex]) is not { } connection)
            {
                return;
            }

            var sourceEntity = connection.SourceEntity;

            var node = FindEntityNode(sourceEntity);

            if (node == null)
            {
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
            SelectEntityInfoConnection(entityInfoForm.EntityInfoControl.OutputsGrid, connection);
        }

        private void SelectEntityInfoConnection(DataGridView targetGrid, EntityLump.Connection connection)
        {
            entityInfoForm?.EntityInfoControl.SelectConnection(targetGrid, connection);
        }

        private static EntityLump.Connection? GetConnection(DataGridViewRow row)
            => row.Cells.Cast<DataGridViewCell>().Select(static cell => cell.Tag).OfType<EntityLump.Connection>().FirstOrDefault();

        private void OnLinkedResourceActivated(object? sender, LinkedResource link)
        {
            switch (link)
            {
                case LinkedEntities { Entities: [var entity] }:
                    SelectAndFocusEntity(entity);
                    ShowEntityDetails(entity);
                    break;

                case LinkedEntities { Entities: var entities }:
                    SelectAndFocusEntities(entities);
                    break;

                case LinkedFile { Path: var path }:
                    if (Viewers.Resource.OpenExternalReference(GuiContext, path))
                    {
                        entityInfoForm?.Close();
                    }
                    break;
            }
        }

        private void OnConnectionFireRequested(object? sender, EntityLump.Connection connection)
        {
            using var lockedGl = MakeCurrent();
            Renderer.EntitySystem.QueueConnection(connection, Renderer.EntitySystem.Player);
        }

        private void OnShowInGraphButtonClick(object? sender, EventArgs e)
        {
            if (entityInfoEntity != null)
            {
                ShowEntityInGraph?.Invoke(entityInfoEntity);
            }
        }

        private void OnEntityInfoFormClosed(object? sender, FormClosedEventArgs e)
        {
            if (entityInfoForm == null)
            {
                return;
            }

            entityInfoForm.EntityInfoControl.PropertiesCellDoubleClick -= OnEntityInfoPropertiesCellDoubleClick;
            entityInfoForm.EntityInfoControl.OutputsCellDoubleClick -= OnEntityInfoOutputsCellDoubleClick;
            entityInfoForm.EntityInfoControl.InputsCellDoubleClick -= OnEntityInfoInputsCellDoubleClick;
            entityInfoForm = null;
            entityInfoEntity = null;
        }
        protected override void OnPicked(PickResult result)
        {
            var pixelInfo = result.Pixel;

            // Void
            if (result.HitNothing)
            {
                Selection.Clear();
                return;
            }

            var sceneNode = Renderer.FindPickedNode(result);

            if (sceneNode == null)
            {
                return;
            }

            if (result.Intent == PickIntent.Select)
            {
                if (result.Has(PickModifiers.Control))
                {
                    Selection.Toggle(sceneNode);
                }
                else
                {
                    Selection.Select(sceneNode);
                }

                //Update the entity properties window if it was opened
                if (entityInfoForm != null)
                {
                    Program.MainForm.Invoke(() =>
                    {
                        ShowSceneNodeDetails(sceneNode);
                    });
                }
                return;
            }

            if (result.Intent == PickIntent.Details)
            {
                Program.MainForm.Invoke(() =>
                {
                    ShowSceneNodeDetails(sceneNode);
                    entityInfoForm?.EntityInfoControl.Focus();
                });
                return;
            }

            Log.Info(nameof(GLWorldViewer), $"Opening {sceneNode.Name} (Id: {pixelInfo.ObjectId})");

            var filename = sceneNode.Name;

            if (sceneNode.EntityData != null)
            {
                // Perhaps this needs to check for correct classname?
                var particle = sceneNode.EntityData.GetStringProperty("effect_name");

                if (particle != null)
                {
                    filename = particle;
                }
            }

            var foundFile = GuiContext.FindFileWithContext(filename + GameFileLoader.CompiledFileSuffix);

            if (foundFile.Context == null)
            {
                return;
            }

            if (!Matrix4x4.Invert(sceneNode.Transform * Renderer.Camera.CameraViewMatrix, out var transform))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            FullScreenForm?.Close();

            foundFile.Context.GLPostLoadAction = (viewerControl) =>
            {
                // The inverse of a view matrix, so its third row is the camera's backward direction
                var forward = -transform.GetRow(2).AsVector3();

                if (viewerControl is GLSceneViewer sceneViewer && sceneNode is not SceneAggregate { InstanceTransforms.Count: > 0 })
                {
                    sceneViewer.Input.Camera.CopyFrom(Renderer.Camera);
                    sceneViewer.Input.Camera.SetLocation(transform.Translation);
                    sceneViewer.Input.Camera.SetFromQAngle(EntityTransformHelper.ForwardDirectionToEulerAngles(forward));
                }

                if (viewerControl is not GLModelViewer glModelViewer || sceneNode is not ModelSceneNode worldModel)
                {
                    return;
                }

                // Set same mesh groups
                if (glModelViewer.meshGroupListBox != null)
                {
                    foreach (int checkedItemIndex in glModelViewer.meshGroupListBox.CheckedIndices)
                    {
                        glModelViewer.meshGroupListBox.SetItemChecked(checkedItemIndex, false);
                    }

                    foreach (var group in worldModel.GetActiveMeshGroups())
                    {
                        var item = glModelViewer.meshGroupListBox.FindStringExact(group);

                        if (item != ListBox.NoMatches)
                        {
                            glModelViewer.meshGroupListBox.SetItemChecked(item, true);
                        }
                    }
                }

                // Set same material group
                if (glModelViewer.materialGroupListBox != null && worldModel.ActiveMaterialGroup != null)
                {
                    var skinId = glModelViewer.materialGroupListBox.FindStringExact(worldModel.ActiveMaterialGroup);

                    if (skinId != -1)
                    {
                        glModelViewer.materialGroupListBox.SelectedIndex = skinId;
                    }
                }

                // Set animation
                if (glModelViewer.animationComboBox != null && worldModel.AnimationController.ActiveAnimation != null)
                {
                    var animationId = glModelViewer.animationComboBox.FindStringExact(worldModel.AnimationController.ActiveAnimation.Name);

                    if (animationId != -1)
                    {
                        glModelViewer.animationComboBox.SelectedIndex = animationId;
                    }
                }
            };

            Program.MainForm.Invoke(() =>
            {
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            });
        }

        protected override void OnKeyDown(Keys keyData)
        {
            if (keyData == Keys.H)
            {
                HideSelectedEntities();
                return;
            }

            if (keyData == (Keys.Control | Keys.H))
            {
                IsolateSelectedEntities();
                return;
            }

            if (keyData == Keys.U)
            {
                ShowAllEntities();
                return;
            }

            if (keyData == (Keys.Control | Keys.G))
            {
                Program.MainForm.Invoke(ShowGoToDialog);
                return;
            }

            if (keyData == (Keys.Control | Keys.Shift | Keys.E))
            {
                Program.MainForm.Invoke(GoToSelectedEntities);
                return;
            }

            if (keyData == (Keys.Control | Keys.Alt | Keys.R))
            {
                Program.MainForm.Invoke(ShowEntityListForm);
                return;
            }

            if (keyData == (Keys.Alt | Keys.Return) && SelectedNodeRenderer?.SelectedNode is { } selectedNode)
            {
                Program.MainForm.Invoke(() => ShowSceneNodeDetails(selectedNode));
                return;
            }

            base.OnKeyDown(keyData);
        }

        private void HideSelectedEntities()
        {
            if (SelectedNodeRenderer?.SelectedNodes is not { Count: > 0 } selectedNodes)
            {
                return;
            }

            foreach (var scene in Renderer.Scenes)
            {
                scene.HideSelectedNodes(selectedNodes);
            }
            SelectedNodeRenderer.SelectNode(null);
        }

        private void IsolateSelectedEntities()
        {
            var selectedNodes = SelectedNodeRenderer?.SelectedNodes ?? [];
            foreach (var scene in Renderer.Scenes)
            {
                scene.IsolateSelectedNodes(selectedNodes);
            }
        }

        private void ShowAllEntities()
        {
            foreach (var scene in Renderer.Scenes)
            {
                scene.ShowAllNodes();
            }
        }

        private void GoToSelectedEntities()
        {
            var selectedNodes = SelectedNodeRenderer?.SelectedNodes
                .Where(static node => node.EntityData != null)
                .DistinctBy(static node => node.EntityData)
                .ToList();

            if (selectedNodes is not { Count: > 0 })
            {
                return;
            }

            if (selectedNodes is [var selectedNode])
            {
                SelectAndFocusNode(selectedNode);
                return;
            }

            ShowEntitySelectionDialog(selectedNodes, "current selection", showDetails: false);
        }

        private void ShowGoToDialog()
        {
            using var dialog = new GoToForm();
            if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(dialog.InputText))
            {
                return;
            }

            switch (dialog.SelectedGoToType)
            {
                case GoToType.EntityName:
                    GoToEntityByName(dialog.InputText);
                    break;
                case GoToType.Coordinate:
                    GoToCoordinate(dialog.InputText);
                    break;
                case GoToType.HammerId:
                    GoToEntityByHammerId(dialog.InputText);
                    break;
            }
        }

        private void GoToEntityByName(string name, bool showNotFound = true)
        {
            var matches = FindEntityNameMatches(Renderer.Scenes, name);
            if (matches.Count > 1)
            {
                ShowEntitySelectionDialog(matches, name);
                return;
            }

            if (matches.Count == 0)
            {
                if (showNotFound)
                {
                    _ = AppMessageDialogs.ShowMessageAsync($"No entity found with name containing '{name}'.", "Not Found");
                }

                return;
            }

            var node = matches[0];
            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private static List<SceneNode> FindEntityNameMatches(IReadOnlyList<Scene> scenes, string name)
        {
            var matches = scenes.SelectMany(static scene => scene.AllNodes)
                .Where(node => node.EntityData?.TargetName is { } targetName && EntityLump.EntityNameMatches(name, targetName))
                .ToList();

            if (matches.Count == 0)
            {
                matches = scenes.SelectMany(scene => scene.FindNodesByKeyValuePartial("targetname", name)).ToList();
            }

            return matches
                .Select(node => node.Scene.Find(node.EntityData!))
                .OfType<SceneNode>()
                .Distinct<SceneNode>(ReferenceEqualityComparer.Instance)
                .ToList();
        }

        private void ShowEntitySelectionDialog(List<SceneNode> matches, string searchTerm, Action<SceneNode>? onSelected = null, bool showDetails = true)
        {
            using var selectionForm = new EntitySelectionForm(matches, searchTerm);
            if (selectionForm.ShowDialog() == DialogResult.OK && selectionForm.SelectedNode is { } selectedNode)
            {
                SelectAndFocusNode(selectedNode);
                if (showDetails)
                {
                    ShowSceneNodeDetails(selectedNode);
                }

                onSelected?.Invoke(selectedNode);
            }
        }

        private void GoToCoordinate(string input)
        {
            if (!EntityTransformHelper.TryParseVector3(input, out var position))
            {
                _ = AppMessageDialogs.ShowMessageAsync("Invalid coordinate format. Use: X Y Z", "Invalid Input", MessageIcon.Warning);
                return;
            }

            Input.SaveCameraForTransition();
            Input.Camera.SetLocation(position);
        }

        private void GoToEntityByHammerId(string hammerId)
        {
            var node = FindNodeByHammerId(hammerId);
            if (node == null)
            {
                _ = AppMessageDialogs.ShowMessageAsync($"No entity found with hammeruniqueid '{hammerId}'.", "Not Found");
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private SceneNode? FindNodeByHammerId(string hammerId)
            => Renderer.Scenes
                .Select(scene => scene.FindNodeByKeyValue("hammeruniqueid", hammerId))
                .FirstOrDefault(static node => node != null);

        private void ShowCoordinateMarkerForm()
        {
            if (LoadedWorld == null)
            {
                return;
            }

            coordinateMarkerSession ??= new CoordinateMarkerSession(
                Scene,
                LoadedWorld.Entities.FirstOrDefault()?.ParentLump
                    ?? new EntityLump { Resource = new ValveResourceFormat.Resource() });

            if (coordinateMarkerForm == null || coordinateMarkerForm.IsDisposed)
            {
                coordinateMarkerForm = new CoordinateMarkerForm(
                    coordinateMarkerSession,
                    AddCoordinateMarkers,
                    RenameCoordinateMarker,
                    RemoveCoordinateMarkers,
                    ClearCoordinateMarkers);
                coordinateMarkerForm.ShowInTaskbar = false;
                coordinateMarkerForm.FormClosed += OnCoordinateMarkerFormClosed;
                coordinateMarkerForm.Show(Program.MainForm);
            }
            else
            {
                coordinateMarkerForm.Activate();
            }
        }

        private void ShowGameplayFlowForm()
        {
            if (LoadedWorld == null)
            {
                return;
            }

            if (gameplayFlowForm != null && !gameplayFlowForm.IsDisposed)
            {
                gameplayFlowForm.Activate();
                return;
            }

            var resourcePath = GuiContext.FileName;
            var mapName = Path.GetFileName(resourcePath);
            var map = new GameplayFlowMap
            {
                ResourcePath = resourcePath,
                DisplayName = mapName,
            };
            var flow = new GameplayFlowModel
            {
                Title = "Gameplay Flow 1",
            };
            var document = new GameplayFlowDocument
            {
                Title = $"{Path.GetFileNameWithoutExtension(mapName)} Gameplay Flows",
                Maps = [map],
                Flows = [flow],
            };

            gameplayFlowSession = new(document);
            gameplayFlowStateAdapter = new(this, resourcePath);
            gameplayFlowForm = new(gameplayFlowSession, gameplayFlowStateAdapter);
            gameplayFlowForm.FormClosed += OnGameplayFlowFormClosed;
            gameplayFlowForm.Show(Program.MainForm);
        }

        private void OnGameplayFlowFormClosed(object? sender, FormClosedEventArgs e)
        {
            if (gameplayFlowForm != null)
            {
                gameplayFlowForm.FormClosed -= OnGameplayFlowFormClosed;
                gameplayFlowForm.Dispose();
                gameplayFlowForm = null;
            }

            gameplayFlowStateAdapter?.Dispose();
            gameplayFlowStateAdapter = null;
            gameplayFlowSession = null;
            ActivateRenderLoop();
        }

        private string? AddCoordinateMarkers(string name, string coordinatesText)
        {
            Debug.Assert(coordinateMarkerSession != null);

            if (!CoordinateMarkerSession.TryParseCoordinates(coordinatesText, out var coordinates, out var error))
            {
                return error;
            }

            using (MakeCurrent())
            {
                coordinateMarkerSession.Add(name, coordinates);
            }

            RefreshCoordinateMarkerConsumers();
            return null;
        }

        private void RenameCoordinateMarker(CoordinateMarkerSession.Marker marker, string name)
        {
            Debug.Assert(coordinateMarkerSession != null);
            using (MakeCurrent())
            {
                coordinateMarkerSession.Rename(marker, name);
            }

            RefreshCoordinateMarkerConsumers();
        }

        private void RemoveCoordinateMarkers(IReadOnlyCollection<CoordinateMarkerSession.Marker> markers)
        {
            Debug.Assert(coordinateMarkerSession != null);
            Debug.Assert(SelectedNodeRenderer != null);

            using (MakeCurrent())
            {
                var removedNodes = markers.Select(static marker => marker.Node).ToHashSet();
                var remainingSelection = SelectedNodeRenderer.SelectedNodes.Where(node => !removedNodes.Contains(node)).ToList();
                if (remainingSelection.Count != SelectedNodeRenderer.SelectedNodes.Count)
                {
                    SelectedNodeRenderer.SetSelectedNodes(remainingSelection, forceDisableDepth: true);
                }

                SelectedNodeRenderer.RemovePinnedNodes(removedNodes);
                coordinateMarkerSession.Remove(markers);
            }

            RefreshCoordinateMarkerConsumers();
        }

        private void ClearCoordinateMarkers()
        {
            Debug.Assert(coordinateMarkerSession != null);
            RemoveCoordinateMarkers([.. coordinateMarkerSession.Markers]);
        }

        private void RefreshCoordinateMarkerConsumers()
        {
            UpdatePinnedHighlightCount();
            if (LoadedWorld != null && coordinateMarkerSession != null)
            {
                entityListForm?.SetEntities(coordinateMarkerSession.CombineWith(LoadedWorld.Entities));
            }

            ActivateRenderLoop();
        }

        private void OnCoordinateMarkerFormClosed(object? sender, FormClosedEventArgs e)
        {
            if (coordinateMarkerForm == null)
            {
                return;
            }

            coordinateMarkerForm.FormClosed -= OnCoordinateMarkerFormClosed;
            coordinateMarkerForm.Dispose();
            coordinateMarkerForm = null;
        }

        private void ShowEntityListForm()
        {
            if (LoadedWorld == null)
            {
                return;
            }

            if (entityListForm == null || entityListForm.IsDisposed)
            {
                var mapName = Path.GetFileNameWithoutExtension(GuiContext.FileName);
                var defaultExportFileName = string.IsNullOrEmpty(mapName) ? "entities.json" : $"{mapName}_entities.json";
                var entities = coordinateMarkerSession?.CombineWith(LoadedWorld.Entities) ?? LoadedWorld.Entities;
                entityListForm = new EntityListForm(
                    entities,
                    defaultExportFileName,
                    EntityListExportPath,
                    entity => coordinateMarkerSession?.Contains(entity) != true);
                entityListForm.ShowInTaskbar = false;
                entityListForm.OnEntitySelectionChanged += OnEntityListSelectionChanged;
                entityListForm.OnEntityDoubleClicked += OnEntityListDoubleClicked;
                entityListForm.OnEntityInfoRequested += OnEntityListInfoRequested;
                entityListForm.OnMapSelectionSyncRequested += OnEntityListMapSelectionSyncRequested;
                entityListForm.OnEntitiesPinned += OnEntityListEntitiesPinned;
                entityListForm.OnEntitiesUnpinned += OnEntityListEntitiesUnpinned;
                entityListForm.OnPinnedEntitiesCleared += OnEntityListPinnedEntitiesCleared;
                entityListForm.FormClosed += (sender, _) =>
                {
                    entityListForm.OnMapSelectionSyncRequested -= OnEntityListMapSelectionSyncRequested;
                    entityListForm.OnEntitiesPinned -= OnEntityListEntitiesPinned;
                    entityListForm.OnEntitiesUnpinned -= OnEntityListEntitiesUnpinned;
                    entityListForm.OnPinnedEntitiesCleared -= OnEntityListPinnedEntitiesCleared;
                    SelectedNodeRenderer?.ClearPinnedNodes();
                    ActivateRenderLoop();
                    EntityListExportPath = ((EntityListForm)sender!).ExportPath;
                    entityListForm = null;
                };
                entityListForm.Show(Program.MainForm);
                UpdatePinnedHighlightCount();
                SynchronizeEntitySelection();
            }
            else
            {
                entityListForm.Activate();
            }
        }

        private void OnEntityListSelectionChanged(object? sender, IReadOnlyList<EntityLump.Entity> entities)
        {
            SelectEntities(entities, synchronizeList: false, synchronizeGraph: true);

            if (entities.Count == 1 && SelectedNodeRenderer?.SelectedNodes is [var selectedNode] && entityInfoForm != null)
            {
                ShowSceneNodeDetails(selectedNode);
            }
        }

        private void OnEntityListMapSelectionSyncRequested(object? sender, EventArgs e)
            => SynchronizeEntitySelection();

        private void OnEntityListEntitiesPinned(object? sender, IReadOnlyList<EntityLump.Entity> entities)
        {
            SelectedNodeRenderer?.AddPinnedNodes(entities.Select(FindEntityNode).OfType<SceneNode>());
            UpdatePinnedHighlightCount();
            ActivateRenderLoop();
        }

        private void OnEntityListEntitiesUnpinned(object? sender, IReadOnlyList<EntityLump.Entity> entities)
        {
            SelectedNodeRenderer?.RemovePinnedNodes(entities.Select(FindEntityNode).OfType<SceneNode>());
            UpdatePinnedHighlightCount();
            ActivateRenderLoop();
        }

        private void OnEntityListPinnedEntitiesCleared(object? sender, EventArgs e)
        {
            SelectedNodeRenderer?.ClearPinnedNodes();
            UpdatePinnedHighlightCount();
            ActivateRenderLoop();
        }

        private void UpdatePinnedHighlightCount()
        {
            if (entityListForm == null)
            {
                return;
            }

            IReadOnlyList<SceneNode> pinnedNodes = SelectedNodeRenderer?.PinnedNodes ?? [];
            entityListForm.SetPinnedEntities(pinnedNodes.Select(static node => node.EntityData).OfType<EntityLump.Entity>());
            entityListForm.SetPinnedHighlightCount(pinnedNodes.Count);
        }

        public void SelectEntities(IReadOnlyList<EntityLump.Entity> entities)
            => SelectEntities(entities, synchronizeList: true, synchronizeGraph: true);

        public void SelectEntitiesFromGraph(IReadOnlyList<EntityLump.Entity> entities)
            => SelectEntities(entities, synchronizeList: true, synchronizeGraph: false);

        private void SelectEntities(IReadOnlyList<EntityLump.Entity> entities, bool synchronizeList, bool synchronizeGraph)
        {
            if (SelectedNodeRenderer == null)
            {
                return;
            }

            var nodes = entities
                .Select(FindEntityNode)
                .OfType<SceneNode>()
                .Distinct()
                .ToList();

            selectingEntities = true;
            try
            {
                SelectedNodeRenderer.SetSelectedNodes(nodes, forceDisableDepth: true);
            }
            finally
            {
                selectingEntities = false;
            }

            SynchronizeEntitySelection(synchronizeList, synchronizeGraph);
        }

        private void OnMapSelectionChanged(object? sender, EventArgs e)
        {
            if (!selectingEntities)
            {
                SynchronizeEntitySelection();
            }
        }

        private void SynchronizeEntitySelection(bool synchronizeList = true, bool synchronizeGraph = true)
        {
            var entities = SelectedNodeRenderer?.SelectedNodes
                .Select(static node => node.EntityData)
                .OfType<EntityLump.Entity>()
                .Distinct()
                .ToList() ?? [];

            if (Program.MainForm.InvokeRequired)
            {
                Program.MainForm.BeginInvoke(() => SynchronizeEntitySelection(entities, synchronizeList, synchronizeGraph));
                return;
            }

            SynchronizeEntitySelection(entities, synchronizeList, synchronizeGraph);
        }

        private void SynchronizeEntitySelection(IReadOnlyCollection<EntityLump.Entity> entities, bool synchronizeList, bool synchronizeGraph)
        {
            if (synchronizeList)
            {
                entityListForm?.SelectEntities(entities);
            }

            if (synchronizeGraph)
            {
                selectEntitiesInGraph?.Invoke(entities);
            }
        }

        private void OnEntityListDoubleClicked(object? sender, EntityLump.Entity entity)
        {
            SelectAndFocusEntity(entity);
            GLControl?.Focus();
        }

        private void OnEntityListInfoRequested(object? sender, EntityLump.Entity entity)
        {
            ShowEntityDetails(entity);
            entityListForm?.Activate();
        }

        private void SetAvailableLayers(IEnumerable<string> worldLayers)
        {
            Debug.Assert(worldLayersComboBox != null);

            worldLayersComboBox.Items.Clear();

            var worldLayersArray = worldLayers.ToArray();

            if (worldLayersArray.Length > 0)
            {
                worldLayersComboBox.Enabled = true;
                worldLayersComboBox.Items.AddRange(worldLayersArray);
            }
            else
            {
                worldLayersComboBox.Enabled = false;
            }
        }

        private const string PhysicsRenderAsOpaque = "S2V: Render as opaque";

        private void SetAvailablePhysicsGroups(IEnumerable<string> physicsGroups)
        {
            Debug.Assert(physicsGroupsComboBox != null);

            physicsGroupsComboBox.Items.Clear();

            var physicsGroupsArray = physicsGroups
                .OrderByDescending(static s => s.StartsWith('-'))
                .ThenBy(static s => s.StartsWith('-') ? string.Empty : s)
                .ToArray();

            if (physicsGroupsArray.Length > 0)
            {
                physicsGroupsComboBox.Enabled = true;
                physicsGroupsComboBox.Items.AddRange(physicsGroupsArray);
                physicsGroupsComboBox.Items.Add(PhysicsRenderAsOpaque);
            }
            else
            {
                physicsGroupsComboBox.Enabled = false;
            }
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            chosenPhysicsGroups = physicsGroups;
            RequestVisibilityUpdate();
        }

        protected override bool HasToolsVisibilityControls => world != null;

        protected override HashSet<string> FilterLayers(HashSet<string> layers)
        {
            if (!HasToolsVisibilityControls)
            {
                return layers;
            }

            var filtered = tools.FilterLayers(layers);

            // While playing the game hides disabled entities itself, so they come and go with the other entities
            if (simulationState != SimulationState.Stopped && filtered.Contains("Entities"))
            {
                filtered.Add(WorldLoader.DisabledEntitiesLayerName);
            }

            return filtered;
        }

        protected override void ApplyVisibility()
        {
            if (HasToolsVisibilityControls)
            {
                foreach (var scene in Renderer.Scenes)
                {
                    scene.ShowToolsMaterials = tools.ShowToolMaterials;
                }
            }

            base.ApplyVisibility();

            var physicsGroups = chosenPhysicsGroups;
            var renderTranslucent = !physicsGroups.Contains(PhysicsRenderAsOpaque);

            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                var groupChosen = physicsGroups.Contains(physNode.PhysGroupName);

                physNode.Enabled = HasToolsVisibilityControls ? tools.IsPhysicsVisible(physNode, groupChosen) : groupChosen;
                physNode.IsTranslucentRenderMode = renderTranslucent;
            }
        }

        // Above the viewport rather than in the sidebar: how the world is run and what of the tools is drawn
        private void AddSimulationToolbar()
        {
            Debug.Assert(UiControl != null);

            // The image list hands out a copy each time it is asked
            playImage = AppIcons.ImageList.Images[AppIcons.Icons["RendererPlay"]];
            pauseImage = AppIcons.ImageList.Images[AppIcons.Icons["RendererPause"]];
            stopImage = AppIcons.ImageList.Images[AppIcons.Icons["RendererStop"]];

            playPauseButton = UiControl.AddToolbarButton(playImage, "Play", () => RequestSimulation(SimulationRequest.PlayOrPause), centered: true);
            stopButton = UiControl.AddToolbarButton(stopImage, "Stop", () => RequestSimulation(SimulationRequest.Stop), centered: true);

            toolEntitiesButton = UiControl.AddToolbarToggle("Tool Entities", tools.ShowToolEntities, v =>
            {
                tools.ShowToolEntities = v;
                RequestVisibilityUpdate();
            });
            toolMaterialsButton = UiControl.AddToolbarToggle("Tool Materials", tools.ShowToolMaterials, v =>
            {
                tools.ShowToolMaterials = v;
                RequestVisibilityUpdate();
            });

            UpdateSimulationButtons(simulationState);
            RequestVisibilityUpdate();
        }

        private void RequestSimulation(SimulationRequest request)
        {
            Interlocked.Exchange(ref simulationRequest, (int)request);

            // Hands the keyboard back to the viewport, which the click took, so the player can be walked
            GLControl?.Focus();
        }

        private void UpdateSimulationButtons(SimulationState state)
        {
            var playing = state == SimulationState.Playing;

            playPauseButton?.Image = playing ? pauseImage : playImage;
            playPauseButton?.AccessibleName = playing ? "Pause" : "Play";
            stopButton?.Enabled = state != SimulationState.Stopped;
        }

        protected override (string Text, Color32 Color)? ModeLabel => simulationState switch
        {
            SimulationState.Playing => ("PLAY MODE", new Color32(90, 220, 90)),
            SimulationState.Paused => ("PLAY MODE (PAUSED)", new Color32(90, 220, 90)),
            _ => null,
        };

        protected override void OnUpdate(float frameTime)
        {
            ApplySimulationRequest();

            base.OnUpdate(frameTime);
        }

        // Walking into a world at rest plays it
        protected override void OnWalkModeEntered()
        {
            if (simulationState == SimulationState.Stopped)
            {
                ApplySimulation(SimulationRequest.Play);
            }
        }

        private void ApplySimulationRequest()
            => ApplySimulation((SimulationRequest)Interlocked.Exchange(ref simulationRequest, (int)SimulationRequest.None));

        // On the render thread, which owns the entity world and the scene
        private void ApplySimulation(SimulationRequest request)
        {
            if (world == null)
            {
                return;
            }

            if (request == SimulationRequest.PlayOrPause)
            {
                request = simulationState == SimulationState.Playing ? SimulationRequest.Pause : SimulationRequest.Play;
            }

            var entitySystem = Renderer.EntitySystem;

            switch (request, simulationState)
            {
                case (SimulationRequest.Play, SimulationState.Stopped):
                    simulationState = SimulationState.Playing;

                    // Before the respawn, so entities spawn hidden as the game has them
                    entitySystem.Enabled = true;
                    ResetScene();
                    entitySystem.StartRound();
                    Input.SetWalkMode(true);
                    break;

                case (SimulationRequest.Play, SimulationState.Paused):
                    simulationState = SimulationState.Playing;
                    entitySystem.Enabled = true;
                    break;

                case (SimulationRequest.Pause, SimulationState.Playing):
                    simulationState = SimulationState.Paused;
                    entitySystem.Enabled = false;
                    break;

                case (SimulationRequest.Stop, SimulationState.Playing or SimulationState.Paused):
                    simulationState = SimulationState.Stopped;
                    entitySystem.Enabled = false;
                    Input.SetWalkMode(false);
                    ResetScene();
                    break;

                default:
                    return;
            }

            var state = simulationState;

            if (UiControl is { IsHandleCreated: true } control)
            {
                control.BeginInvoke(() => UpdateSimulationButtons(state));
            }
        }

        /// <summary>
        /// Puts the world back as it loaded: every entity is removed, along with what they left in the scene,
        /// and spawned again from the map's entity lumps.
        /// </summary>
        private void ResetScene()
        {
            Debug.Assert(LoadedWorld != null);

            ClearEntityMoves();

            // Its nodes are about to go
            Selection.Clear();

            Renderer.RemoveEntities();

            LoadedWorld.LoadingProgress = null;
            LoadedWorld.ReloadEntities();

            foreach (var spawnGroup in LoadedWorld.SpawnGroups)
            {
                Renderer.AddSpawnGroup(spawnGroup);
                spawnGroup.Scene.Initialize();
            }

            Scene.RebindLighting();

            Renderer.EntitySystem.SpawnPlayer(Input.PlayerMovement, Scene);

            ApplyRenderModeToNodes();
            ApplyVisibility();
        }
    }
}
