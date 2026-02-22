using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
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

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
    class GLWorldViewer : GLSceneViewer
    {
        private readonly World? world;
        private readonly WorldNode? worldNode;
        private readonly ResourceExtRefList? mapExternalReferences;
        private CheckedListBox? worldLayersComboBox;
        private CheckedListBox? physicsGroupsComboBox;
        private ComboBox? cameraComboBox;
        private SavedCameraPositionsControl? savedCameraPositionsControl;
        private EntityInfoForm? entityInfoForm;
        private EntityListForm? entityListForm;
        private CoordinateMarkerForm? coordinateMarkerForm;
        private CoordinateMarkerSession? coordinateMarkerSession;
        private string EntityListExportPath = string.Empty;
        private bool ignoreLayersChangeEvents = true;
        private List<Matrix4x4> CameraMatrices = [];
        private WorldNodeLoader? LoadedWorldNode;
        public WorldLoader? LoadedWorld;
        private EntityLump.Entity? entityInfoEntity;
        private Action<IReadOnlyCollection<EntityLump.Entity>>? selectEntitiesInGraph;
        private bool selectingEntities;
        private SelectionHighlightSettings selectionHighlightSettings = SelectionHighlightSettings.Default;
        private SelectionHighlightSettingsForm? selectionHighlightSettingsForm;

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

            base.Dispose();

            worldLayersComboBox?.Dispose();
            physicsGroupsComboBox?.Dispose();
            cameraComboBox?.Dispose();
            savedCameraPositionsControl?.Dispose();
            entityInfoForm?.Dispose();
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

        private void AddSceneExposureSlider()
        {
            Debug.Assert(UiControl != null);

            var exposureLabel = new Label();
            void UpdateExposureText(float exposure)
            {
                exposureLabel.Text = $"Exposure: {exposure:0.00}";
            }

            UiControl.AddControl(exposureLabel);

            var exposureSlider = UiControl.AddTrackBar((exposureAmount) =>
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

                AppClipboard.SetText($"setpos {loc.X:F6} {loc.Y:F6} {loc.Z:F6}; setang {cameraAngles.X:F6} {cameraAngles.Y:F6} {cameraAngles.Z:F6}");

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
            var cameraSet = false;

            // Bring up the sound player before any models load, so their animation clips can pre-cache sound events
            InitializeSoundPlayer();

            if (world != null)
            {
                LoadedWorld = new WorldLoader(world, Scene);
                LoadedWorld.Load(mapExternalReferences);

                if (LoadedWorld.SkyboxScene != null)
                {
                    Renderer.SkyboxScene = LoadedWorld.SkyboxScene;
                }

                if (LoadedWorld.Skybox2D != null)
                {
                    Renderer.Skybox2D = LoadedWorld.Skybox2D;
                }

                NavMeshSceneNode.AddNavNodesToScene(LoadedWorld.NavMesh, Scene);
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

                Input.TryLoadViewmodel(Scene);

                var kzMapPrefixes = new[] { "bhop", "surf", "kz", "dr" };
                var mapName = Path.GetFileName(LoadedWorld.MapName);
                var isKzMap = kzMapPrefixes.Any(prefix => mapName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                Input.PlayerMovement.PrestrafeEnabled = isKzMap;
                Input.PlayerMovement.AutoBunnyHop = isKzMap;
                Input.PlayerMovement.AirAccelerate = isKzMap
                    ? PlayerMovement.AirAccelerateMovementMaps
                    : PlayerMovement.AirAccelerateCompetitive;

                Input.EntitySystem = Scene.EntitySystem;
                Scene.EntitySystem.SpawnPlayer(Input.PlayerMovement);
            }

            if (!cameraSet)
            {
                Input.Camera.SetLocation(new Vector3(256));
                Input.Camera.LookAt(Vector3.Zero);
            }

            if (worldNode != null)
            {
                LoadedWorldNode = new WorldNodeLoader(Scene.RendererContext, worldNode, mapExternalReferences);
                LoadedWorldNode.Load(Scene);
            }
        }

        protected override void OnFirstPaint()
        {
            Input.MoveCamera(new Vector3(0, -150f, 0));

            base.OnFirstPaint();

            StartMapSoundEvents();

            Input.MoveCamera(new Vector3(0, 150f, 0), transition: true);
        }

        /// <summary>
        /// Starts point_soundevent entities with "Start On Spawn" set. The rest require entity I/O, which the viewer does not simulate.
        /// </summary>
        private void StartMapSoundEvents()
        {
            if (soundPlayer == null)
            {
                return;
            }

            // Collect named entities first, to resolve "Source Entity Name" emitters
            var namedEntities = new Dictionary<string, SceneNode>(StringComparer.OrdinalIgnoreCase);
            var pointSoundEvents = new List<EntityLump.Entity>();

            foreach (var node in Scene.AllNodes)
            {
                var entityData = node.EntityData;
                if (entityData == null)
                {
                    continue;
                }

                var targetname = entityData.GetStringProperty("targetname");
                if (!string.IsNullOrEmpty(targetname))
                {
                    namedEntities.TryAdd(targetname, node);
                }

                switch (entityData.GetStringProperty("classname"))
                {
                    case "point_soundevent" or "snd_event_point":
                        pointSoundEvents.Add(entityData);
                        break;

                    case "env_soundscape" or "snd_soundscape":
                        if (entityData.GetBooleanProperty("startdisabled"))
                        {
                            break;
                        }

                        if (entityData.GetBooleanProperty("enablesoundevent"))
                        {
                            soundPlayer.AddSoundscape(
                                entityData.GetVector3Property("origin"),
                                entityData.GetFloatProperty("radius"),
                                entityData.GetStringProperty("soundevent"));
                        }
                        else
                        {
                            // Classic script-based soundscape (see SoundscapeBank), e.g. "amb.park"
                            soundPlayer.AddScriptedSoundscape(
                                entityData.GetVector3Property("origin"),
                                entityData.GetFloatProperty("radius"),
                                entityData.GetStringProperty("soundscape"));
                        }
                        break;

                    default:
                        break;
                }
            }

            foreach (var entityData in pointSoundEvents)
            {
                var soundName = entityData.GetStringProperty("soundname");

                if (string.IsNullOrEmpty(soundName) || !entityData.GetBooleanProperty("startonspawn"))
                {
                    continue;
                }

                soundPlayer.Play(soundName, GetSoundEventPosition(entityData, namedEntities));
            }
        }

        /// <summary>
        /// Resolves where a point_soundevent emits from: null for "To Local Player" events, otherwise the source entity's position.
        /// </summary>
        private static Vector3? GetSoundEventPosition(EntityLump.Entity entityData, Dictionary<string, SceneNode> namedEntities)
        {
            if (entityData.GetBooleanProperty("tolocalplayer"))
            {
                return null;
            }

            var sourceEntityName = entityData.GetStringProperty("sourceentityname");

            if (string.IsNullOrEmpty(sourceEntityName) || !namedEntities.TryGetValue(sourceEntityName, out var sourceNode))
            {
                return entityData.GetVector3Property("origin");
            }

            var attachmentName = entityData.GetStringProperty("sourceentityattachment");

            if (!string.IsNullOrEmpty(attachmentName)
                && sourceNode is ModelSceneNode sourceModel
                && sourceModel.Attachments.ContainsKey(attachmentName))
            {
                return sourceModel.GetAttachmentTransform(attachmentName).Translation;
            }

            if (entityData.GetBooleanProperty("uselocaloffset"))
            {
                // Entities do not move in the viewer, so the source entity plus local offset is just this entity's origin
                return entityData.GetVector3Property("origin");
            }

            return sourceNode.Transform.Translation;
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Render"))
            {
                AddRenderModeSelectionControl();
                AddWireframeToggleControl();

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
            }

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledLayers([.. worldLayers]);
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
                item => item is not string value
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
                var uniqueWorldLayers = new HashSet<string>(4);
                var uniquePhysicsGroups = new HashSet<string>();

                foreach (var node in Scene.AllNodes)
                {
                    if (node.LayerName?.StartsWith("Internal -", StringComparison.Ordinal) == true)
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

                    foreach (var worldLayer in LoadedWorld.DefaultEnabledLayers)
                    {
                        var checkboxIndex = worldLayersComboBox.FindStringExact(worldLayer);

                        if (checkboxIndex > -1)
                        {
                            worldLayersComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                        }
                    }

                    worldLayersComboBox.EndUpdate();

                    Scene.SetEnabledLayers(LoadedWorld.DefaultEnabledLayers);
                    SkyboxScene?.SetEnabledLayers(LoadedWorld.DefaultEnabledLayers);
                }

                if (uniquePhysicsGroups.Count > 0)
                {
                    SetAvailablePhysicsGroups(uniquePhysicsGroups);
                }

                using (UiControl.BeginGroup("World"))
                {
                    if (Renderer.SkyboxScene != null)
                    {
                        UiControl.AddCheckBox("Show Skybox", Renderer.ShowSkybox, (v) => Renderer.ShowSkybox = v);
                    }

                    UiControl.AddCheckBox("Show Fog", Scene.FogEnabled, v => Scene.FogEnabled = v);

                    UiControl.AddCheckBox("Entity System", Scene.EntitySystem.Enabled, v => Scene.EntitySystem.Enabled = v);

                    UiControl.AddCheckBox("Color Correction", Renderer.Postprocess.ColorCorrectionEnabled, v => Renderer.Postprocess.ColorCorrectionEnabled = v);

                    // TODO: PVS culling is not implemented yet
                    // if (Scene.VoxelVisibility != null)
                    // {
                    //     UiControl.AddCheckBox("PVS Culling", Scene.EnablePvsCulling, v => Scene.EnablePvsCulling = v);
                    // }

                    CheckBox? occlusionCullingCheckBox = null;

                    if (GLEnvironment.SlowMultiDrawIndirect)
                    {
                        Scene.EnableIndirectDraws = false;
                        SkyboxScene?.EnableIndirectDraws = false;
                    }

                    UiControl.AddCheckBox("GPU Culling", Scene.EnableIndirectDraws, v =>
                    {
                        Scene.EnableIndirectDraws = v;
                        SkyboxScene?.EnableIndirectDraws = v;

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
                var origin = entity.GetVector3Property("origin");
                FocusCameraOnBounds(new AABB(origin - new Vector3(32f), origin + new Vector3(32f)));
                ActivateRenderLoopAfterLayout();
                return;
            }

            SelectAndFocusNode(node);
        }

        private SceneNode? FindEntityNode(EntityLump.Entity entity)
            => Scene.Find(entity) ?? SkyboxScene?.Find(entity);

        public void SelectAndFocusEntities(IReadOnlyList<EntityLump.Entity> entities)
        {
            if (entities.Count == 1)
            {
                SelectAndFocusEntity(entities[0]);
                return;
            }

            SelectContainingTabs();

            Debug.Assert(SelectedNodeRenderer != null);

            var hasBounds = false;
            var bounds = default(AABB);
            var selectedAny = false;

            selectingEntities = true;
            try
            {
                foreach (var entity in entities)
                {
                    var node = Scene.Find(entity) ?? SkyboxScene?.Find(entity);

                    AABB entityBounds;

                    if (node != null)
                    {
                        if (selectedAny)
                        {
                            SelectedNodeRenderer.ToggleNode(node);
                        }
                        else
                        {
                            SelectedNodeRenderer.SelectNode(node, forceDisableDepth: true);
                            selectedAny = true;
                        }

                        EnsureNodeVisible(node);
                        entityBounds = SelectionBounds(node);
                    }
                    else
                    {
                        var origin = entity.GetVector3Property("origin");
                        entityBounds = new AABB(origin - new Vector3(32f), origin + new Vector3(32f));
                    }

                    bounds = hasBounds ? bounds.Union(entityBounds) : entityBounds;
                    hasBounds = true;
                }
            }
            finally
            {
                selectingEntities = false;
            }

            SynchronizeEntitySelection(synchronizeGraph: false);

            if (hasBounds)
            {
                FocusCameraOnBounds(bounds);
                ActivateRenderLoopAfterLayout();
            }
        }

        private void SelectAndFocusNode(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            Debug.Assert(SelectedNodeRenderer != null);

            SelectedNodeRenderer.SelectNode(node, forceDisableDepth: true);
            FocusCameraOnBounds(SelectionBounds(node));
            EnsureNodeVisible(node);
            ActivateRenderLoopAfterLayout();
        }

        private static AABB SelectionBounds(SceneNode node)
        {
            var bbox = node.BoundingBox;
            var maxSpan = Math.Max(Math.Max(bbox.Size.X, bbox.Size.Y), bbox.Size.Z);

            // Empty or degenerate bounds (e.g. a particle system that finished playing)
            // would put the camera inside the node or at a garbage position.
            if (!float.IsFinite(maxSpan) || maxSpan < 1f)
            {
                bbox = new AABB(node.Transform.Translation - new Vector3(32f), node.Transform.Translation + new Vector3(32f));
            }

            return bbox;
        }

        private void FocusCameraOnBounds(in AABB bbox)
        {
            var center = bbox.Center;
            var size = bbox.Size;
            var maxDimension = Math.Max(Math.Max(size.X, size.Y), size.Z);

            if (!float.IsFinite(maxDimension) || maxDimension < 1f)
            {
                maxDimension = 64f;
            }

            // Orbit far enough out to frame the bounds, then let the physics probe move the camera
            // off any wall or ceiling it would otherwise be spawned inside of.
            var distance = Math.Max(maxDimension * 2.5f, 64f);
            var location = CameraPlacement.FindOrbitPosition(Scene.PhysicsWorld, center, distance, maxDimension * 0.5f);

            Input.SaveCameraForTransition();
            Input.Camera.SetLocation(location);
            Input.Camera.LookAt(center);
        }

        private void EnsureNodeVisible(SceneNode node)
        {
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
            var isEntity = sceneNode.EntityData != null;
            entityInfoEntity = sceneNode.EntityData;

            EnsureEntityInfoForm();

            Debug.Assert(entityInfoForm != null);

            if (entityInfoForm.ShowInGraphButton != null)
            {
                entityInfoForm.ShowInGraphButton.Visible = isEntity
                    && entityInfoEntity != null
                    && (EntityHasGraphNode?.Invoke(entityInfoEntity) ?? false);
            }

            entityInfoForm.EntityInfoControl.Clear();

            if (isEntity)
            {
                ShowEntityProperties(sceneNode.EntityData!);
            }
            else
            {
                entityInfoForm.Text = $"{sceneNode.GetType().Name}: {sceneNode.Name}";

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
                    entityInfoForm.EntityInfoControl.AddProperty("Shader", material.ShaderName);
                    entityInfoForm.EntityInfoControl.AddProperty("Material", material.Name);
                    entityInfoForm.EntityInfoControl.AddProperty("Aggregate Model", sceneFragment.Name!);

                    var tris = sceneFragment.DrawCall.IndexCount / 3;
                    if (sceneFragment.DrawCall.NumMeshlets > 0)
                    {
                        var clusters = sceneFragment.DrawCall.NumMeshlets;
                        var trisPerCluster = tris / clusters;
                        entityInfoForm.EntityInfoControl.AddProperty("Triangles / Clusters / Per Cluster", $"{tris} / {clusters} / {trisPerCluster}");
                    }
                    else
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Triangles", $"{tris}");
                    }

                    entityInfoForm.EntityInfoControl.AddProperty("Model Tint", ToRenderColor(sceneFragment.DrawCall.TintColor));
                    entityInfoForm.EntityInfoControl.AddProperty("Model Alpha", $"{sceneFragment.DrawCall.TintColor.W:F6}");

                    if (sceneFragment.Tint != Vector4.One)
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Instance Tint", ToRenderColor(sceneFragment.Tint));
                        entityInfoForm.EntityInfoControl.AddProperty("Final Tint", ToRenderColor(sceneFragment.DrawCall.TintColor * sceneFragment.Tint));
                    }
                }
                else if (sceneNode is ModelSceneNode modelSceneNode)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Model", modelSceneNode.Name!);
                    entityInfoForm.EntityInfoControl.AddProperty("Model Tint", ToRenderColor(modelSceneNode.Tint));
                    entityInfoForm.EntityInfoControl.AddProperty("Model Alpha", $"{modelSceneNode.Tint.W:F6}");

                    if (modelSceneNode.LightingOrigin.HasValue)
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Custom Lighting Origin", FormatVector(modelSceneNode.LightingOrigin.Value));
                    }
                }

                if (sceneNode.CubeMapPrecomputedHandshake > 0)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Cubemap Handshake", $"{sceneNode.CubeMapPrecomputedHandshake}");
                }

                if (sceneNode.LightProbeVolumePrecomputedHandshake > 0)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Light Probe Handshake", $"{sceneNode.LightProbeVolumePrecomputedHandshake}");
                }

                entityInfoForm.EntityInfoControl.AddProperty("Flags", sceneNode.Flags.ToString());
                entityInfoForm.EntityInfoControl.AddProperty("Layer", sceneNode.LayerName ?? string.Empty);
            }

            if (SkyboxScene != null && sceneNode.Scene == SkyboxScene)
            {
                entityInfoForm.Text += " (in 3D skybox)";
            }

            entityInfoForm.EntityInfoControl.ShowPopulatedTabs();
            entityInfoForm.EntityInfoControl.Show();
        }

        private void EnsureEntityInfoForm()
        {
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm(GuiContext);
                entityInfoForm.ShowInTaskbar = false;

                if (ShowEntityInGraph != null)
                {
                    entityInfoForm.AddShowInGraphButton(OnShowInGraphButtonClick);
                }

                entityInfoForm.Show(Program.MainForm);
                entityInfoForm.EntityInfoControl.OutputsGrid.CellDoubleClick += OnEntityInfoOutputsCellDoubleClick;
                entityInfoForm.EntityInfoControl.InputsGrid.CellDoubleClick += OnEntityInfoInputsCellDoubleClick;
                entityInfoForm.EntityInfoControl.Disposed += OnEntityInfoFormDisposed;
            }
        }

        private void OnEntityInfoOutputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (entityInfoForm == null)
            {
                return;
            }

            if (e.RowIndex < 0 || entityInfoForm.EntityInfoControl.OutputsGrid.Rows[e.RowIndex].Tag is not EntityLump.Connection connection)
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
                var node = Scene.FindNodeByTargetName(connection.TargetName)
                    ?? SkyboxScene?.FindNodeByTargetName(connection.TargetName);
                return node == null ? [] : [node];
            }

            var entities = new List<EntityLump.Entity>();
            var resolver = new EntityIOTargetResolver(LoadedWorld.Entities);
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

            if (entityInfoForm.EntityInfoControl.InputsGrid.Rows[e.RowIndex].Tag is not EntityLump.Connection connection)
            {
                return;
            }

            var sourceEntity = connection.SourceEntity;

            var node = Scene.Find(sourceEntity);

            if (node == null && SkyboxScene != null)
            {
                node = SkyboxScene.Find(sourceEntity);
            }

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

        private void OnShowInGraphButtonClick(object? sender, EventArgs e)
        {
            if (entityInfoEntity != null)
            {
                ShowEntityInGraph?.Invoke(entityInfoEntity);
            }
        }

        private void OnEntityInfoFormDisposed(object? sender, EventArgs e)
        {
            if (entityInfoForm == null)
            {
                return;
            }

            entityInfoForm.EntityInfoControl.OutputsGrid.CellDoubleClick -= OnEntityInfoOutputsCellDoubleClick;
            entityInfoForm.EntityInfoControl.InputsGrid.CellDoubleClick -= OnEntityInfoInputsCellDoubleClick;
            entityInfoForm.EntityInfoControl.Disposed -= OnEntityInfoFormDisposed;
            entityInfoForm = null;
        }

        protected override void OnPicked(object? sender, PickingResponse pickingResponse)
        {
            Debug.Assert(SelectedNodeRenderer != null);

            var pixelInfo = pickingResponse.PixelInfo;

            // Void
            if (pixelInfo.ObjectId == 0 || pixelInfo.Unused2 != 0)
            {
                SelectedNodeRenderer.SelectNode(null);
                return;
            }

            var isInSkybox = pixelInfo.IsSkybox > 0;
            var sceneNode = isInSkybox ? SkyboxScene?.Find(pixelInfo.ObjectId) : Scene.Find(pixelInfo.ObjectId);

            if (sceneNode == null)
            {
                return;
            }

            if (pickingResponse.Intent == PickingIntent.Select)
            {
                if ((Control.ModifierKeys & Keys.Control) > 0)
                {
                    SelectedNodeRenderer.ToggleNode(sceneNode);
                }
                else
                {
                    SelectedNodeRenderer.SelectNode(sceneNode);
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

            if (pickingResponse.Intent == PickingIntent.Details)
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
                var forward = -new Vector3(transform.M31, transform.M32, transform.M33);

                if (viewerControl is GLSceneViewer sceneViewer)
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

        private void ShowEntityDetails(EntityLump.Entity entity)
        {
            entityInfoEntity = entity;
            EnsureEntityInfoForm();

            Debug.Assert(entityInfoForm != null);

            if (entityInfoForm.ShowInGraphButton != null)
            {
                entityInfoForm.ShowInGraphButton.Visible = EntityHasGraphNode?.Invoke(entity) ?? false;
            }

            entityInfoForm.EntityInfoControl.Clear();
            ShowEntityProperties(entity);
            entityInfoForm.EntityInfoControl.ShowPopulatedTabs();
            entityInfoForm.EntityInfoControl.Show();
        }

        private void ShowEntityProperties(EntityLump.Entity entity)
        {
            Debug.Assert(entityInfoForm != null);

            if (LoadedWorld is null)
            {
                entityInfoForm.EntityInfoControl.PopulateFromEntity(entity);
            }
            else
            {
                entityInfoForm.EntityInfoControl.PopulateFromEntity(LoadedWorld.Entities, entity);
            }

            entityInfoForm.EntityInfoControl.SortConnections();

            var classname = entity.GetStringProperty("classname");
            var targetName = entity.FriendlyTargetName;
            entityInfoForm.Text = string.IsNullOrEmpty(targetName)
                ? $"Entity: {classname}"
                : $"Entity: {classname} ({targetName})";
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

            Scene.HideSelectedNodes(selectedNodes);
            SkyboxScene?.HideSelectedNodes(selectedNodes);
            SelectedNodeRenderer.SelectNode(null);
        }

        private void IsolateSelectedEntities()
        {
            var selectedNodes = SelectedNodeRenderer?.SelectedNodes ?? [];
            Scene.IsolateSelectedNodes(selectedNodes);
            SkyboxScene?.IsolateSelectedNodes(selectedNodes);
        }

        private void ShowAllEntities()
        {
            Scene.ShowAllNodes();
            SkyboxScene?.ShowAllNodes();
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

        private void GoToEntityByName(string name)
        {
            var node = Scene.FindNodeByTargetName(name) ?? SkyboxScene?.FindNodeByTargetName(name);

            if (node == null)
            {
                var matches = Scene.FindNodesByKeyValuePartial("targetname", name);
                if (SkyboxScene != null)
                {
                    matches.AddRange(SkyboxScene.FindNodesByKeyValuePartial("targetname", name));
                }

                if (matches.Count == 1)
                {
                    node = matches[0];
                }
                else if (matches.Count > 1)
                {
                    ShowEntitySelectionDialog(matches, name);
                    return;
                }
            }

            if (node == null)
            {
                _ = AppMessageDialogs.ShowMessageAsync($"No entity found with name containing '{name}'.", "Not Found");
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private void ShowEntitySelectionDialog(List<SceneNode> matches, string searchTerm, Action<SceneNode>? onSelected = null)
        {
            using var selectionForm = new ThemedForm
            {
                Text = $"Select Entity — {matches.Count} matches for \"{searchTerm}\"",
                Size = new System.Drawing.Size(400, 300),
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
            };

            var listBox = new ListBox { Dock = DockStyle.Fill };
            var items = matches.Select(node => new
            {
                Text = $"{node.EntityData?.GetStringProperty("targetname", "Unknown")} ({node.EntityData?.GetStringProperty("classname", "Unknown")})",
                Node = node,
            }).ToList();
            listBox.DisplayMember = "Text";
            listBox.DataSource = items;

            var buttonPanel = new Panel { Height = 40, Dock = DockStyle.Bottom };
            var okButton = new ThemedButton { Text = "OK", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(10, 8), Size = new System.Drawing.Size(80, 25) };
            var cancelButton = new ThemedButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(100, 8), Size = new System.Drawing.Size(80, 25) };
            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);
            selectionForm.Controls.Add(listBox);
            selectionForm.Controls.Add(buttonPanel);
            selectionForm.AcceptButton = okButton;
            selectionForm.CancelButton = cancelButton;
            listBox.DoubleClick += (_, _) => selectionForm.DialogResult = DialogResult.OK;

            if (selectionForm.ShowDialog() == DialogResult.OK && listBox.SelectedIndex >= 0)
            {
                var selectedNode = items[listBox.SelectedIndex].Node;
                SelectAndFocusNode(selectedNode);
                ShowSceneNodeDetails(selectedNode);
                onSelected?.Invoke(selectedNode);
            }
        }

        private void GoToCoordinate(string input)
        {
            var match = Regexes.Coord().Match(input);
            if (!match.Success)
            {
                _ = AppMessageDialogs.ShowMessageAsync("Invalid coordinate format. Use: X Y Z", "Invalid Input", MessageIcon.Warning);
                return;
            }

            var position = new Vector3(
                float.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
                float.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                float.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture));
            Input.SaveCameraForTransition();
            Input.Camera.SetLocation(position);
        }

        private void GoToEntityByHammerId(string hammerId)
        {
            var node = Scene.FindNodeByKeyValue("hammeruniqueid", hammerId) ?? SkyboxScene?.FindNodeByKeyValue("hammeruniqueid", hammerId);
            if (node == null)
            {
                _ = AppMessageDialogs.ShowMessageAsync($"No entity found with hammeruniqueid '{hammerId}'.", "Not Found");
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

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
                    SelectedNodeRenderer.SelectNode(remainingSelection.FirstOrDefault(), forceDisableDepth: true);
                    foreach (var node in remainingSelection.Skip(1))
                    {
                        SelectedNodeRenderer.ToggleNode(node);
                    }
                }

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
                entityListForm.FormClosed += (sender, _) =>
                {
                    entityListForm.OnMapSelectionSyncRequested -= OnEntityListMapSelectionSyncRequested;
                    EntityListExportPath = ((EntityListForm)sender!).ExportPath;
                    entityListForm = null;
                };
                entityListForm.Show(Program.MainForm);
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
                SelectedNodeRenderer.SelectNode(nodes.FirstOrDefault(), forceDisableDepth: true);
                foreach (var node in nodes.Skip(1))
                {
                    SelectedNodeRenderer.ToggleNode(node);
                }
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
            var renderTranslucent = !physicsGroups.Contains(PhysicsRenderAsOpaque);

            if (!renderTranslucent)
            {
                physicsGroups.Remove(PhysicsRenderAsOpaque);
            }

            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
                physNode.IsTranslucentRenderMode = renderTranslucent;
            }

            using var lockedGl = MakeCurrent();

            Scene.UpdateOctrees();
            SkyboxScene?.UpdateOctrees();
        }
    }
}
