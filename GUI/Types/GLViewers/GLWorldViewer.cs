using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
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
        private Dictionary<string, List<KVObject>>? entityInputListDict;
        private bool ignoreLayersChangeEvents = true;
        private List<Matrix4x4> CameraMatrices = [];
        private WorldNodeLoader? LoadedWorldNode;
        public WorldLoader? LoadedWorld;

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
            base.Dispose();

            worldLayersComboBox?.Dispose();
            physicsGroupsComboBox?.Dispose();
            cameraComboBox?.Dispose();
            savedCameraPositionsControl?.Dispose();
            entityInfoForm?.Dispose();
            entityListForm?.Dispose();
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

        private void OnGetOrSetPositionFromClipboardRequest(object? sender, bool isSetRequest)
        {
            var pitch = 0.0f;
            var yaw = 0.0f;

            if (!isSetRequest)
            {
                var loc = Renderer.Camera.Location;
                pitch = -1.0f * float.RadiansToDegrees(Renderer.Camera.Pitch);
                yaw = float.RadiansToDegrees(Renderer.Camera.Yaw);

                Clipboard.SetText($"setpos {loc.X:F6} {loc.Y:F6} {loc.Z:F6}; setang {pitch:F6} {yaw:F6} 0.0");

                return;
            }

            var text = Clipboard.GetText();
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

            if (ang.Success)
            {
                pitch = -1f * float.DegreesToRadians(float.Parse(ang.Groups["pitch"].Value, CultureInfo.InvariantCulture));
                yaw = float.DegreesToRadians(float.Parse(ang.Groups["yaw"].Value, CultureInfo.InvariantCulture));
            }

            Input.SaveCameraForTransition();
            Input.Camera.SetLocationPitchYaw(new Vector3(x, y, z), pitch, yaw);
        }

        private void OnRestoreCameraRequest(object? sender, RestoreCameraRequestEvent e)
        {
            if (Settings.Config.SavedCameras.TryGetValue(e.Camera, out var savedFloats))
            {
                if (savedFloats.Length == 5)
                {
                    Input.SaveCameraForTransition();
                    Input.Camera.SetLocationPitchYaw(
                        new Vector3(savedFloats[0], savedFloats[1], savedFloats[2]),
                        savedFloats[3],
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

            Settings.Config.SavedCameras.Add(saveName, [cam.Location.X, cam.Location.Y, cam.Location.Z, cam.Pitch, cam.Yaw]);
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

            if (world != null)
            {
                LoadedWorld = new WorldLoader(world, Scene, mapExternalReferences);

                if (LoadedWorld.SkyboxScene != null)
                {
                    Renderer.SkyboxScene = LoadedWorld.SkyboxScene;
                }

                if (LoadedWorld.Skybox2D != null)
                {
                    Renderer.Skybox2D = LoadedWorld.Skybox2D;
                }

                NavMeshSceneNode.AddNavNodesToScene(LoadedWorld.NavMesh, Scene);

                if (LoadedWorld.CameraMatrices.Count > 0)
                {
                    CameraMatrices = LoadedWorld.CameraMatrices;

                    Input.Camera.SetFromTransformMatrix(CameraMatrices[0]);
                    cameraSet = true;
                }
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

            BuildEntityInputList();
        }

        private void BuildEntityInputList()
        {
            var entities = LoadedWorld?.Entities;
            if (entities == null || entities.Count == 0)
            {
                return;
            }

            entityInputListDict = [];

            foreach (var entity in entities)
            {
                if (entity.Connections == null)
                {
                    continue;
                }

                var sourceHammerId = entity.GetProperty<string>("hammeruniqueid") ?? string.Empty;
                var sourceName = entity.GetProperty<string>("targetname") ?? string.Empty;

                foreach (var connection in entity.Connections)
                {
                    var targetName = connection.GetStringProperty("m_targetName");
                    if (string.IsNullOrEmpty(targetName))
                    {
                        continue;
                    }

                    // Create augmented connection with source info
                    var augmented = new KVObject("connection");
                    foreach (var (key, value) in connection.Properties)
                    {
                        augmented.AddProperty(key, value);
                    }
                    augmented.AddProperty("sourceHammerUniqueId", new KVValue(ValveKeyValue.KVValueType.String, sourceHammerId));
                    augmented.AddProperty("sourceName", new KVValue(ValveKeyValue.KVValueType.String, sourceName));

                    if (!entityInputListDict.TryGetValue(targetName, out var list))
                    {
                        list = [];
                        entityInputListDict[targetName] = list;
                    }
                    list.Add(augmented);
                }
            }
        }

        protected override void OnFirstPaint()
        {
            base.OnFirstPaint();

            Input.MoveCamera(new Vector3(0, -150f, 0));
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

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledLayers([.. worldLayers]);
            });
            physicsGroupsComboBox = UiControl.AddMultiSelection("Physics Groups", null, (physicsGroups) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledPhysicsGroups([.. physicsGroups]);
            });

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
                    if (node.LayerName?.StartsWith("LightProbeGrid", StringComparison.Ordinal) == true)
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
                    UiControl.AddCheckBox("Color Correction", Renderer.Postprocess.ColorCorrectionEnabled, v => Renderer.Postprocess.ColorCorrectionEnabled = v);
                    UiControl.AddCheckBox("Occlusion Culling", Scene.EnableOcclusionCulling, (v) => Scene.EnableOcclusionCulling = v);
                    UiControl.AddCheckBox("Gpu Culling", Scene.EnableIndirectDraws, v =>
                    {
                        Scene.EnableIndirectDraws = v;
                    });

                    UiControl.AddCheckBox("Depth Prepass", Scene.EnableDepthPrepass, (v) => Scene.EnableDepthPrepass = v);
                    UiControl.AddCheckBox("Experimental Lights", false, v => Renderer.ViewBuffer!.Data!.ExperimentalLightsEnabled = v);

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
            if (UiControl != null && UiControl.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectTab(tabPage);
            }

            var node = Scene.Find(entity);

            if (node == null && SkyboxScene != null)
            {
                node = SkyboxScene.Find(entity);
            }

            if (node == null)
            {
                return;
            }

            SelectAndFocusNode(node);
        }

        private void SelectAndFocusNode(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            Debug.Assert(SelectedNodeRenderer != null);

            SelectedNodeRenderer.SelectNode(node, forceDisableDepth: true);

            var bbox = node.BoundingBox;
            var size = bbox.Size;
            var maxDimension = Math.Max(Math.Max(size.X, size.Y), size.Z);
            var distance = maxDimension * 1.2f;
            var cameraHeight = bbox.Center.Y + size.Y * 2f;

            var location = new Vector3(bbox.Center.X + distance, cameraHeight, bbox.Center.Z + distance);
            Input.SaveCameraForTransition();
            Input.Camera.SetLocation(location);
            Input.Camera.LookAt(bbox.Center);

            // Ensure the node is visible
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
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm(GuiContext);
                entityInfoForm.Show();
                entityInfoForm.EntityInfoControl.OutputsGrid.CellDoubleClick += OnEntityInfoOutputsCellDoubleClick;
                entityInfoForm.EntityInfoControl.InputsGrid.CellDoubleClick += OnEntityInfoInputsCellDoubleClick;
                entityInfoForm.EntityInfoControl.Disposed += OnEntityInfoFormDisposed;
            }

            Debug.Assert(entityInfoForm != null);

            entityInfoForm.EntityInfoControl.Clear();

            if (isEntity)
            {
                ShowEntityProperties(sceneNode);
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

            entityInfoForm.EntityInfoControl.ShowOutputsTabIfAnyData();
            entityInfoForm.EntityInfoControl.Show();
        }

        private void OnEntityInfoOutputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (entityInfoForm == null || e.RowIndex < 0)
            {
                return;
            }

            var row = entityInfoForm.EntityInfoControl.OutputsGrid.Rows[e.RowIndex];
            var entityName = row.Cells["TargetEntity"].Value?.ToString() ?? string.Empty;
            var hammerId = row.Cells["TargetHammerUniqueId"].Value?.ToString() ?? string.Empty;

            SceneNode? node = null;

            // Try by hammeruniqueid first, then by targetname
            if (!string.IsNullOrEmpty(hammerId))
            {
                node = Scene.FindNodeByKeyValue("hammeruniqueid", hammerId);
                node ??= SkyboxScene?.FindNodeByKeyValue("hammeruniqueid", hammerId);
            }

            if (node == null && !string.IsNullOrEmpty(entityName))
            {
                node = Scene.FindNodeByKeyValueSmart("targetname", entityName);
                node ??= SkyboxScene?.FindNodeByKeyValueSmart("targetname", entityName);
            }

            if (node == null)
            {
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private void OnEntityInfoInputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (entityInfoForm == null || e.RowIndex < 0)
            {
                return;
            }

            var row = entityInfoForm.EntityInfoControl.InputsGrid.Rows[e.RowIndex];
            var sourceHammerId = row.Cells["SourceHammerUniqueId"].Value?.ToString() ?? string.Empty;
            var sourceName = row.Cells["SourceName"].Value?.ToString() ?? string.Empty;

            SceneNode? node = null;

            if (!string.IsNullOrEmpty(sourceHammerId))
            {
                node = Scene.FindNodeByKeyValue("hammeruniqueid", sourceHammerId);
                node ??= SkyboxScene?.FindNodeByKeyValue("hammeruniqueid", sourceHammerId);
            }

            if (node == null && !string.IsNullOrEmpty(sourceName))
            {
                node = Scene.FindNodeByKeyValueSmart("targetname", sourceName);
                node ??= SkyboxScene?.FindNodeByKeyValueSmart("targetname", sourceName);
            }

            if (node == null)
            {
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
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
                var particle = sceneNode.EntityData.GetProperty<string>("effect_name");

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
                var yaw = MathF.Atan2(-transform.M32, -transform.M31);
                var scaleZ = MathF.Sqrt(transform.M31 * transform.M31 + transform.M32 * transform.M32 + transform.M33 * transform.M33);
                var unscaledZ = transform.M33 / scaleZ;
                var pitch = MathF.Asin(-unscaledZ);

                if (viewerControl is GLSceneViewer sceneViewer)
                {
                    sceneViewer.Input.Camera.CopyFrom(Renderer.Camera);
                    sceneViewer.Input.Camera.SetLocationPitchYaw(transform.Translation, pitch, yaw);
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

        private void ShowEntityProperties(SceneNode sceneNode)
        {
            Debug.Assert(entityInfoForm != null);
            Debug.Assert(sceneNode.EntityData != null);

            entityInfoForm.EntityInfoControl.PopulateFromEntity(sceneNode.EntityData);

            // Populate inputs tab
            var targetName = sceneNode.EntityData.GetProperty<string>("targetname");
            if (!string.IsNullOrEmpty(targetName) && entityInputListDict != null &&
                entityInputListDict.TryGetValue(targetName, out var inputConnections))
            {
                foreach (var inputConnection in inputConnections)
                {
                    entityInfoForm.EntityInfoControl.AddInputConnection(inputConnection);
                }
            }

            entityInfoForm.EntityInfoControl.SortConnections();
            entityInfoForm.EntityInfoControl.ShowInputsTabIfAnyData();

            var classname = sceneNode.EntityData.GetProperty<string>("classname");
            entityInfoForm.Text = $"Entity: {classname}";
        }

        protected override void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyData == (Keys.Control | Keys.G))
            {
                Program.MainForm.Invoke(ShowGoToDialog);
                e.Handled = true;
                return;
            }

            if (e.KeyData == (Keys.Control | Keys.Alt | Keys.R))
            {
                Program.MainForm.Invoke(ShowEntityListForm);
                e.Handled = true;
                return;
            }

            if (e.KeyData == (Keys.Alt | Keys.Return))
            {
                Debug.Assert(SelectedNodeRenderer != null);
                var selectedNode = SelectedNodeRenderer.SelectedNode;
                if (selectedNode != null)
                {
                    Program.MainForm.Invoke(() => ShowSceneNodeDetails(selectedNode));
                }
                e.Handled = true;
                return;
            }

            base.OnKeyDown(sender, e);
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
            var node = Scene.FindNodeByKeyValueSmart("targetname", name);
            node ??= SkyboxScene?.FindNodeByKeyValueSmart("targetname", name);

            if (node == null)
            {
                // Try partial matches and show selection if multiple
                var partialMatches = Scene.FindNodesByKeyValuePartial("targetname", name);
                if (SkyboxScene != null)
                {
                    partialMatches.AddRange(SkyboxScene.FindNodesByKeyValuePartial("targetname", name));
                }

                if (partialMatches.Count == 0)
                {
                    MessageBox.Show($"No entity found with name containing '{name}'.", "Not Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (partialMatches.Count == 1)
                {
                    node = partialMatches[0];
                }
                else
                {
                    ShowEntitySelectionDialog(partialMatches, name);
                    return;
                }
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private void ShowEntitySelectionDialog(List<SceneNode> matches, string searchTerm)
        {
            using var selectionForm = new ThemedForm
            {
                Text = $"Select Entity — {matches.Count} matches for \"{searchTerm}\"",
                Size = new System.Drawing.Size(400, 300),
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
            };

            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                DisplayMember = "Text",
            };

            var items = matches.Select(node =>
            {
                var targetname = node.EntityData?.GetProperty<string>("targetname") ?? "Unknown";
                var classname = node.EntityData?.GetProperty<string>("classname") ?? "Unknown";
                return new
                {
                    Text = $"{targetname} ({classname})",
                    Node = node,
                };
            }).ToList();

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

            listBox.DoubleClick += (_, _) =>
            {
                selectionForm.DialogResult = DialogResult.OK;
                selectionForm.Close();
            };

            if (selectionForm.ShowDialog() == DialogResult.OK && listBox.SelectedValue != null)
            {
                var selectedItem = items[listBox.SelectedIndex];
                SelectAndFocusNode(selectedItem.Node);
                ShowSceneNodeDetails(selectedItem.Node);
            }
        }

        private void GoToCoordinate(string input)
        {
            var match = Regexes.Coord().Match(input);
            if (!match.Success)
            {
                MessageBox.Show("Invalid coordinate format. Use: X Y Z", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var x = float.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
            var y = float.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
            var z = float.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture);

            Input.SaveCameraForTransition();
            Input.Camera.SetLocation(new Vector3(x, y, z));
        }

        private void GoToEntityByHammerId(string hammerId)
        {
            var node = Scene.FindNodeByKeyValue("hammeruniqueid", hammerId);
            node ??= SkyboxScene?.FindNodeByKeyValue("hammeruniqueid", hammerId);

            if (node == null)
            {
                MessageBox.Show($"No entity found with hammeruniqueid '{hammerId}'.", "Not Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private void ShowEntityListForm()
        {
            if (LoadedWorld?.Entities == null || LoadedWorld.Entities.Count == 0)
            {
                return;
            }

            if (entityListForm == null || entityListForm.IsDisposed)
            {
                entityListForm = new EntityListForm(LoadedWorld.Entities);
                entityListForm.OnEntityClicked += OnEntityListClicked;
                entityListForm.OnEntityDoubleClicked += OnEntityListDoubleClicked;
                entityListForm.FormClosed += (_, _) =>
                {
                    entityListForm.OnEntityClicked -= OnEntityListClicked;
                    entityListForm.OnEntityDoubleClicked -= OnEntityListDoubleClicked;
                    entityListForm = null;
                };
                entityListForm.Show();
            }
            else
            {
                entityListForm.Activate();
            }
        }

        private void OnEntityListClicked(object? sender, string hammerUniqueId)
        {
            Debug.Assert(SelectedNodeRenderer != null);

            var node = Scene.FindNodeByKeyValue("hammeruniqueid", hammerUniqueId);
            node ??= SkyboxScene?.FindNodeByKeyValue("hammeruniqueid", hammerUniqueId);

            if (node == null)
            {
                return;
            }

            SelectedNodeRenderer.SelectNode(node, forceDisableDepth: true);

            if (entityInfoForm != null)
            {
                Program.MainForm.Invoke(() => ShowSceneNodeDetails(node));
            }

            GLControl?.Focus();
        }

        private void OnEntityListDoubleClicked(object? sender, string _)
        {
            Debug.Assert(SelectedNodeRenderer != null);

            var selectedNode = SelectedNodeRenderer.SelectedNode;
            if (selectedNode != null)
            {
                SelectAndFocusNode(selectedNode);
            }

            GLControl?.Focus();
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
