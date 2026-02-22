## Project Overview

ValveResourceFormat (VRF) is a C# library and toolset for parsing Valve's Source 2 resource formats. The solution file is `ValveResourceFormat.slnx`.

The project folders are:
- **ValveResourceFormat/**: Core parsing library published to NuGet
- **GUI/**: WinForms viewer application
- **CLI/**: Command-line decompiler and file viewer
- **Renderer/**: OpenGL rendering engine for Source 2 assets.
  - Shaders use the `.slang` extension (`.frag.slang`, `.vert.slang`) with GLSL syntax, and must only contain ASCII characters.
  - After changing shaders, run `dotnet run --project Misc/ShaderValidator -- <name filter>` to compile them and their combos on a real GL context. `complex` has combinatorially many combos and is far too slow to validate interactively, so iterate against a smaller shader.
- **Tests/**: TUnit test suite for the ValveResourceFormat library, plus some headless Renderer logic tests in `Tests/Renderer/`.
  - Run tests when changing code in `ValveResourceFormat/` or `Renderer/`. GUI and CLI are not covered.
  - Tests are fast, run the whole suite with `dotnet test`. If it reports `Zero tests ran` (exit code 5), do a full `dotnet build` and retry.
  - When a parsing change legitimately alters text output, run tests with `VRF_REGEN_FIXTURES=1` to rewrite the mismatching `Tests/Files/ValidOutput` dumps in the source tree.
- **Misc/**: Auxiliary tools (ShaderValidator, RenderTest, etc.) in their own solution `Misc/MiscVrfProjects.slnx`.

**Target:** Latest released .NET. Use modern C# features. Nullable reference types enabled.

### Shader Pipeline
- Each Source 2 `.vfx` shader name is mapped via `GetShaderFileByName()` to one of our shader files (e.g. `vr_complex.vfx` → `complex`, `csgo_environment_blend.vfx` → `csgo_environment`). Unmapped shaders fall back to `complex`.
- During compilation, a `GameVfx_{vfxName}` define is set to 1 (e.g. `GameVfx_vr_complex`), activating shader-specific code paths via `#if` blocks. All other `GameVfx_` defines remain 0.
- Texture names from materials are matched to shader uniforms. An alias system maps Source 2 texture names to our uniform names when they differ.
- Material float/int/vector params are set as uniforms by iterating the shader's default values and overriding with material values.
- Render mode defines (e.g. `renderMode_Illumination`) default to 0 and are overridden via static combos at compile time.

### Transforms and Angles
All angle, quaternion and direction conversions live in `EntityTransformHelper`. Use it instead of hand-rolling trig, and read its class remarks before touching this area.
- Source 2 is Z-up and right-handed: +X forward, +Y left, +Z up.
- An entity's `angles` is a QAngle: (pitch, yaw, roll) in degrees, pitch positive **downwards**.
- Matrices are row-vector, so a rotation's first row is forward. Frames built from a direction must put it on +X.
- `Camera` holds the same angles in radians. Convert at that boundary via `Camera.SetFromQAngle`/`GetQAngle`.

## Custom Branch Features

`custom` is a long-lived feature branch based on `master`. The items below describe intentional behavior that must survive rebases and later maintenance.

When rebasing, use the latest `master` code, APIs, naming, and architecture as the implementation baseline. Reapply the required custom behavior to that baseline instead of restoring obsolete custom implementation details. Update this section whenever a custom feature is added, removed, or materially changed.

During Custom implementation and maintenance, minimize divergence from `master`. Prefer the smallest localized change that satisfies the required behavior, and avoid refactoring or replacing `master` architecture unless the existing structure cannot support a safe solution. When broader changes are necessary, state the reason explicitly before implementing them.

Apply the open-closed principle to Custom work: keep `master` implementations and their default behavior closed to unnecessary modification, while making Custom behavior open through new files, partial or derived types, composition, adapters, and narrowly scoped extension points. Continue to depend on and reuse `master` APIs and implementations rather than duplicating them; complete independence from `master` is not a goal. Modify an existing file when the capability belongs in that type or when a small general-purpose hook is required, but keep the Custom policy and orchestration outside it whenever practical.

During Custom work, preserve the exact formatting of files and lines inherited unchanged from `master`, including BOMs, using order, and indentation. Never run solution-wide `dotnet format` on this branch. Format only files intentionally changed for the current Custom work, using an explicit `dotnet format --include <paths>` scope, then inspect the resulting diff and discard any unrelated formatting changes.

### Entity Browsing and Navigation

- Purpose: make map entities searchable and navigable from the 3D world viewer.
- Required behavior:
  - `Ctrl+Alt+R` opens a reusable entity list for the loaded world.
  - The entity list supports property key/value filters, exact matching, comma-OR/plus-AND/exclamation-NOT expressions, outgoing I/O filters, and per-column filters. Active property and I/O filters add columns showing the matched data.
  - Selecting a row highlights the corresponding scene node; when the per-window sync checkbox is enabled, MAP selection changes select matching visible rows without changing filters; double-clicking selects it and moves the camera to it.
  - `Ctrl+G` navigates by entity target name, world coordinate, or Hammer ID. Target-name lookup tries an exact match first, then a case-insensitive partial match, and prompts when multiple nodes match.
  - `Alt+Enter` opens information for the selected entity or scene node.
- Main integration points: `GUI/Forms/EntityListForm.cs`, `GUI/Forms/GoToForm.cs`, `GUI/Controls/EntityInfoControl.cs`, `GUI/Controls/RendererControl.cs`, `GUI/Types/GLViewers/GLWorldViewer.cs`, `GUI/Utils/KeybindingRegistry.cs`, and selection helpers in `Renderer/`.
- Invariants: navigation must search both the main and 3D skybox scenes, MAP-to-list synchronization defaults off and is not persisted, list-to-MAP selection remains active regardless of that checkbox, synchronization must not create an event loop, coordinate navigation must preserve camera-transition history, and opening a second entity-list window for the same viewer must be avoided.

### Explorer Refresh

- Purpose: allow newly installed game and Workshop content to appear without restarting the application.
- Required behavior: Refresh is available from the existing Explorer context menus and reruns the existing game-folder scan after clearing cached game and Workshop entries.
- Main integration points: `GUI/Controls/ExplorerControl.cs` and `GUI/Controls/ExplorerControl.Designer.cs`.
- Invariants: only one scan may run at a time, refresh requests during an active scan are ignored, and the existing scanning status text and scan behavior remain unchanged.

### Independent Entity I/O Graph Window and Auxiliary Windows

- Purpose: allow an independently rendered entity I/O graph to remain visible beside the world viewer without changing the original resource tabs.
- Required behavior:
  - The original resource tabs and embedded entity I/O graph retain their existing viewer instances, rendering path, and selection behavior.
  - The entity I/O graph tab alone offers "Open in New Window" and reuses one independent window per world viewer.
  - The independent window owns a separate entity I/O graph viewer, renderer context, GL context, and render thread; it synchronizes selection with MAP in both directions.
  - The independent graph window, entity lists, and entity-info windows are owned by the main window and do not create separate taskbar entries.
  - Graph text keeps Segoe UI as its primary font and uses system font fallback only for text runs whose glyphs are missing.
- Main integration points: `GUI/Controls/TabWindowMenu.cs`, `GUI/Forms/EntityIOGraphWindow.cs`, `GUI/Types/Viewers/Resource.cs`, `GUI/Types/Graphs/WindowedEntityIOGraphViewer.cs`, `GUI/Types/GLViewers/GLBaseControl.cs`, `GUI/Types/Graphs/Core/GraphView.Render.cs`, and `ValveResourceFormat/Graphs/GraphFont.cs`.
- Invariants: no original tab or viewer is moved or copied, the embedded entity I/O graph does not participate in MAP selection synchronization, closing the independent window or owning resource stops its render thread before disposing GL and renderer resources, the default shared render-loop path remains unchanged, graph measurement and drawing use identical font runs, and pure Segoe UI text keeps its existing appearance.

### Entity I/O Inspection and Navigation

- Purpose: make the existing entity-info Inputs and Outputs tabs reliable for tracing entity I/O in either direction.
- Required behavior:
  - Inputs are derived through `EntityIOTargetResolver`, including the special target syntax supported by that resolver, instead of target-name string comparison alone.
  - Outputs display resolved target Hammer IDs with compact comma separation for multiple targets; Inputs display source Hammer IDs. Both grids are sorted with numeric-aware Hammer ID ordering and delay as the secondary key.
  - Double-clicking any cell in an Output row navigates to the resolved target and selects the same connection in that entity's Inputs tab.
  - Double-clicking any cell in an Input row performs the reverse operation, navigating to the source and selecting the same connection in its Outputs tab.
  - An Output resolving to multiple renderable entities prompts the user to choose a destination before navigation.
  - Independent entity I/O graph nodes support Ctrl-click multi-selection and Ctrl+A selection of all currently visible nodes; graph and MAP selection changes synchronize in both directions without moving the camera or transferring focus.
- Main integration points: `GUI/Controls/EntityInfoControl.cs`, `GUI/Controls/EntityInfoControl.Designer.cs`, `GUI/Types/GLViewers/GLWorldViewer.cs`, `GUI/Types/Graphs/Core/GraphView.cs`, `GUI/Utils/Comparer.cs`, `ValveResourceFormat/Graphs/GraphSelection.cs`, and `ValveResourceFormat/Utils/EntityIOTargetResolver.cs`.
- Invariants: cross-entity navigation must preserve the exact `Connection` object so the counterpart row can be selected, unresolved or non-renderable targets must not navigate to an unrelated node, resolution must use the complete loaded-world entity set when available, merged independent-graph nodes synchronize all member entities, reverse synchronization selects only currently visible graph nodes, graph-originated selections must not be reduced by a MAP round trip, and synchronization ignores entities without a counterpart rather than changing filters.

### Entity JSON Export

- Purpose: export useful subsets of map or world entities without decompiling the entire resource.
- Required behavior:
  - "Export entities" is available from supported VPK file and open-tab context menus, and from the world viewer entity list.
  - VPK and open-tab exports run classname filtering, property selection, optional relationship expansion, and then save directly.
  - Entity-list exports operate on the currently displayed rows, reuse custom property selections within that window, and show a JSON preview that can be saved or opened in an external editor.
  - Property selection supports per-class property trees, search, smart defaults, all-properties selection, and classname-specific required properties.
  - Relationship expansion is transitive and can independently follow known entity-reference properties or both incoming and outgoing resolved I/O connections without duplicating entities.
  - The configured external editor is persisted in GUI settings. The entity-list export path is reused only for that world viewer and is not a global persisted setting.
  - Property-filtered JSON preserves native KV value types for selected properties and emits the supported connection fields when an entity has outputs.
- Main integration points: `GUI/Types/Exporter/ExportFile.cs`, `GUI/Forms/FilterForm.cs`, `GUI/Forms/PropertySelectionDialog.cs`, `GUI/Forms/JsonPreviewForm.cs`, `GUI/Utils/EntityPropertyManager.cs`, `GUI/MainForm.ContextMenus.cs`, and `GUI/Utils/Settings.cs`.
- Invariants: exporting from a resource tree node and an already open tab must use the same extraction and selection pipeline, cancellation must not write a file, editor launch without an explicit export path must use a temporary JSON file, and relationship expansion must terminate without duplicate entities.

### Entity Extraction and Serialization Support

- Purpose: provide the library operations required by the GUI entity tools.
- Required behavior:
  - `FileExtract.ExtractEntities` and `MapExtract.ToEntities` collect entities from map and world resources.
  - Extraction can include child entity lumps that are not reached through point-template traversal.
  - `KVObjectJsonConverter`, `KVJsonContext`, and `KVJsonSerializer` provide AOT-compatible JSON conversion while preserving supported KV scalar, collection, array, and binary value types.
  - `EntityIOTargetResolver` can return resolved incoming connections for an entity.
  - Entity identity uses `hammeruniqueid` when present and otherwise falls back to object identity, so relationship expansion and deduplication remain stable.
- Main integration points: `ValveResourceFormat/IO/FileExtract.cs`, `ValveResourceFormat/IO/MapExtract.cs`, `ValveResourceFormat/Serialization/KeyValues/KVJsonSerialization.cs`, `ValveResourceFormat/Resource/ResourceTypes/EntityLump.cs`, `ValveResourceFormat/Utils/EntityIOTargetResolver.cs`, and `ValveResourceFormat/Utils/EntityLumpTraversal.cs`.
- Invariants: traversal must avoid cycles and duplicate visits, loaded child resources must be disposed, missing child lumps must remain non-fatal, and existing default traversal behavior must remain unchanged unless the new inclusion option is requested.

### Renderer Selection Support

- Purpose: connect entity searches and list selections to renderer state without weakening existing renderer behavior.
- Required behavior:
  - Scenes can find all nodes by a case-insensitive partial string-property match and callers can inspect the primary selected node.
  - Generic renderer multi-selection controls toggle eligible items with `Ctrl+A`.
  - World viewers provide one session-only selection-highlight settings window with live controls for outline width, glow, fill opacity, one shared highlight color, selected-node dimensions, and optional distant screen-space corner markers. Restore Defaults returns to the original yellow outline with dimensions shown and no fill or marker; reopening a viewer also restores those defaults.
  - In Physics Groups, bulk selection excludes `S2V: Render as opaque` and the default world collision group; physics nodes identify that default group explicitly.
- Main integration points: `Renderer/Renderer/Scene.cs`, `Renderer/Renderer/SelectedNodeRenderer.cs`, `Renderer/Renderer/SelectionHighlightSettings.cs`, `Renderer/Renderer/SelectionScreenMarkerRenderer.cs`, `Renderer/Renderer/PostProcess/OutlineRenderer.cs`, `Renderer/Renderer/SceneNodes/PhysSceneNode.cs`, `GUI/Forms/SelectionHighlightSettingsForm.cs`, and `GUI/Types/GLViewers/GLWorldViewer.cs`.
- Invariants: search covers static and dynamic nodes, selection highlighting remains visible when initiated from the entity list, renderer selection reads use stable snapshots while UI tools may update selection concurrently, highlight-style changes remain local to the current viewer and default rendering stays unchanged, the bulk-selection predicate does not affect manual item selection, and the default collision-group marker is only assigned to the world default group rather than entity-owned physics.

### Session Coordinate Markers

- Purpose: add temporary named entities at pasted world coordinates for searching, multi-selection, and visual reference.
- Required behavior:
  - The world viewer accepts one invariant-culture `X Y Z` coordinate per non-empty line, permits whitespace or punctuation other than numeric `+`, `-`, and `.` as separators, and appends every valid line under the batch name without requiring unique names.
  - Markers can be appended repeatedly, renamed individually, deleted individually or in a selection, and cleared as a group.
  - Each marker appears in the entity list as `s2v_coordinate_marker` and renders as a cyan axis cross with a solid, pickable center diamond that retains a minimum on-screen size at long distances.
  - Marker entities and renderer nodes exist only for the current world-viewer session.
- Main integration points: `GUI/Forms/CoordinateMarkerForm.cs`, `GUI/Types/GLViewers/CoordinateMarkerSession.cs`, `GUI/Types/GLViewers/GLWorldViewer.cs`, `GUI/Forms/EntityListForm.cs`, `Renderer/Renderer/SceneNodes/CoordinateMarkerSceneNode.cs`, and shape resource cleanup in `Renderer/Renderer/SceneNodes/ShapeSceneNode.cs`.
- Invariants: duplicate names must remain independently selectable by entity identity, invalid input must not partially add a batch, marker mutations must be synchronized with the GL render context, marker geometry must retain pickable bounds and release its GL resources when removed, and markers must not enter loaded-world entity storage, entity I/O resolution, or entity export.

### World Viewer Entity Visibility

- Purpose: hide selected renderable content or isolate the current selection without changing existing renderer visibility controls.
- Required behavior:
  - `H` hides all viewer nodes belonging to the currently selected entities, or the exact selected non-entity nodes, and clears the selection.
  - `Ctrl+H` hides every scene node except the complete render hierarchy needed by the current entity or non-entity selection; an empty selection hides the entire scene.
  - `U` removes all visibility overrides created by these shortcuts without changing the current selection.
  - The shortcuts apply to both the main scene and the 3D skybox scene and support multiple selected nodes.
- Main integration points: `GUI/Types/GLViewers/GLWorldViewer.cs`, `GUI/Utils/KeybindingRegistry.cs`, `Renderer/Renderer/Scene.cs`, `Renderer/Renderer/SceneNode.cs`, and `Renderer/Renderer/SelectedNodeRenderer.cs`.
- Invariants: shortcut visibility is an additional viewer-only mask; World Layers, Physics Groups, mesh groups, and other existing visibility state must retain their values, and restoring shortcut-hidden nodes must not make nodes visible when another visibility control still hides them.

### Text Viewer Unicode Fallback

- Purpose: make text containing wide or fallback-font glyphs readable without replacing the existing code viewer.
- Required behavior: text content uses the existing code viewer by default, and its content context menu can switch the current tab between that viewer and the native read-only Windows text control.
- Main integration points: `GUI/Controls/SwitchableTextControl.cs`, `GUI/Controls/CodeTextBox.cs`, and `GUI/Controls/ViewerContentPresenter.cs`.
- Invariants: switching is session-only and local to the current text view, the original text and syntax mode are retained when switching back, oversized text continues to use the existing basic-viewer fallback, and the default viewer choice remains unchanged.

### Custom Feature Verification

After changing or rebasing these features:

1. Run the normal Release build and test checklist below because the custom behavior touches both `ValveResourceFormat/` and `Renderer/`. Apply formatting only to the explicitly changed Custom files as required by the scope rule above.
2. Manually open a map in the GUI and verify the three shortcuts, exact and partial navigation, multiple-match selection, entity filtering, row selection/highlighting, and camera navigation.
3. In entity info, verify Hammer ID display and sorting, then follow one connection Output-to-Input and Input-to-Output. Include a special or multi-target connection when test data provides one. In the I/O graph, verify Ctrl-click, Ctrl+A, merged-node expansion, MAP highlight synchronization without camera movement, and unchanged Ctrl-drag behavior.
4. Verify `Ctrl+A` in renderer multi-selection controls and confirm Physics Groups keeps the two excluded entries unchecked.
5. Export entities from both a resource context menu and an open tab, then verify class filtering, property selection, relationship expansion, cancellation, and saved JSON. Separately verify preview, editor launch, and path reuse from the entity list.
6. Review every file in `git diff --name-status master...custom`, including modifications to pre-existing controls. Add, correct, or remove feature records when the intentional branch delta changes.
7. Open the independent entity I/O graph twice and verify the existing window is reused while the embedded tab remains unchanged. Verify bidirectional MAP selection, then close the window and owning resource while confirming rendering stops cleanly. Confirm graph labels display available non-Latin scripts without missing-glyph boxes.

## Code Style
Follow standard Microsoft C# conventions. Key rules:

### Formatting
- 4 space indentation, no tabs, no trailing spaces
- LF line endings for C# files, final newline required
- Allman braces (opening brace on a new line)

### Naming
- PascalCase for types, methods, properties, and private fields
- camelCase for parameters and locals, IPascalCase for interfaces
- Namespaces loosely match folder structure

### Language Use
- Always use `var` for locals
- Collection expressions: `[]` instead of `new List<>()`
- Nullable annotations where appropriate (`string?`, `Resource?`)
- No `this.` qualification unless disambiguating
- Expression bodies for properties, indexers, and accessors; block bodies for methods and constructors
- Switch expressions, pattern matching, null coalescing, throw expressions, string interpolation
- Using declarations rather than using statements when possible
- `MathF` operations over `(float)Math` casts
- Prefer early returns
- Sort usings with System namespaces first, then others alphabetically, and remove unused ones
- `System`, `System.Numerics`, `System.Collections.Generic` are global usings (defined in Directory.Build.props)

### Comments and Documentation
- Use `//` comments, and only for non-obvious logic, workarounds, and TODOs; explain "why", not "what"
- Plain ASCII only: no em-dashes, curly quotes, ellipsis, or Unicode math symbols
- Never mention where format knowledge came from (other codebases, tools, games' internals) in comments or commit messages
- Comments must not narrate the change, this conversation, or session codenames; no decorative dividers
- Leave existing comments alone if they are clear and correct
- XML docs are required for public APIs in ValveResourceFormat and Renderer; keep them concise and use `<inheritdoc/>` on overrides that add nothing new

## Before Committing Checklist

Run these once when the work is done, not after every edit. While iterating, build only the project you changed.

1. Run `dotnet build` and fix warnings and notices. CI builds Release, which enables `TreatWarningsAsErrors` and `AnalysisMode=All`, so build with `-c Release` to catch what Debug misses.
2. Run `dotnet format` to fix formatting. On the `custom` branch, never run it solution-wide; follow the scoped formatting rule under Custom Branch Features.
3. Run `dotnet test` to ensure all tests pass
4. Remove any debug code, console logs, and commented code you added
