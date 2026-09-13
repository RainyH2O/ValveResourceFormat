---
description: Create an editable Hammer entity module from exported or authored entity JSON.
---

<script setup>
import exampleUrl from '../examples/entity-logic-module.json?url'
</script>

# Create an Entity Map from JSON

Source 2 Viewer can convert exported or hand-authored entity JSON into a new editable
`.vmap`. Use it to build reusable logic modules, review generated entity I/O, or bring
selected exported entities into Hammer. The conversion creates a separate map; it does
not insert entities into an existing map.

## Create a VMAP

In the GUI, choose **File > Create entity map from JSON...**. Use **Load JSON...** to load a
file, **Paste JSON** to use clipboard text, or **Use Example** to start with a small logic module.
Edit the JSON, choose **Validate** to see the entity count and any warnings, then choose
**Save VMAP...**. Saving validates the current text again. Validation errors prevent saving;
warnings allow it.

From the command line:

```powershell
./Source2Viewer-CLI.exe --import_entities -i "module.json" -o "module.vmap"
```

This mode accepts one JSON file and one `.vmap` output path. Validation completes before
the output is written, so invalid JSON does not replace an existing output file. The
command reports warnings and returns a nonzero exit code on failure.

Open the generated `.vmap` in the target game's Hammer tools. Inspect the entities,
properties, mesh sizes, and connections, then copy the entities into your working map or
reference the module as a prefab. Use the target game's entity definitions to check class
and input/output availability, and compile and test the result in that game. A module does
not include a playable level, player spawns, lighting, or other surrounding map content.

## Example Logic Module

The <a :href="exampleUrl" download="entity-logic-module.json">complete JSON example</a> contains three entities:

1. `module_trigger`, a `trigger_once` with a 128-unit box, fires `OnStartTouch` at
   `module_relay` with input `Trigger`.
2. `module_relay`, a `logic_relay`, fires `OnTrigger` at `module_door` with input `Open`
   after a 0.5-second delay.
3. `module_door`, a `func_door`, owns a 16 by 128 by 128 mesh box.

Save that JSON as `module.json` and run the command above. The example supplies geometry
explicitly, so its dimensions can be edited before or after conversion. Check its material
paths and entity settings against your game's tools before using it in a level.

### Authoring with AI

Give the AI the example JSON, the target game's entity definitions, and a concrete list
of desired events. For example:

```text
Create an entity module for [game] using the attached example's formatVersion: 1
envelope and nested properties objects. Return JSON only.

Use exact class, input, output, and property names from the supplied game entity
definitions; do not invent names. Connect a trigger_once OnStartTouch to a
logic_relay Trigger, then its OnTrigger to a func_door Open after 0.5 seconds.
Give each entity a module_ targetname and an origin. For brush geometry, specify
only box.size; let the importer choose the default center and material.
Keep connections outside properties. Use explicit target names, and include all
destinations unless a target is intentionally external. Name intentional external
targets external_* so they can be connected to matching entities in the final map.
```

Replace the game placeholder and check that the requested classes and I/O exist in that
game. Review and validate the generated JSON, then inspect and test the VMAP in Hammer.

## JSON Format

The recommended document shape is:

```json
{
    "formatVersion": 1,
    "entities": [
        {
            "properties": {
                "classname": "logic_relay",
                "targetname": "module_relay",
                "origin": [0, 0, 64],
                "angles": "0 0 0"
            },
            "connections": []
        }
    ]
}
```

The entity array must be non-empty and include at least one entity other than
`worldspawn`. Every entity needs a non-empty `classname` without whitespace. Entity properties can instead be placed
directly in the entity object. The bare array produced by
[entity JSON export](./command-line.md#export-entities-from-a-map) is also accepted:

```json
[
    {
        "classname": "logic_relay",
        "targetname": "module_relay",
        "origin": "0 0 64",
        "connections": []
    }
]
```

`properties`, `connections`, and `box` are importer fields. Use the field casing shown
here, and keep `connections` and `box` outside a nested `properties` object. Nested
entities accept only these three outer fields. Do not repeat JSON keys, even with
different casing, or provide both friendly and legacy names for the same connection
field. Unsupported document versions are rejected.

Origins, angles, scales, and box vectors accept either three numeric array elements or a string
containing exactly three finite coordinates. Strings use invariant-culture numbers with
`.` as the decimal separator; whitespace and punctuation can separate coordinates.
Source 2 uses +X forward, +Y left, and +Z up. Angles are pitch, yaw, and roll in degrees,
with positive pitch pointing down.

Scale components must be nonzero. An optional single `worldspawn` supplies world
properties and cannot have box geometry. Imported `hammeruniqueid` values are not used
as the new map's entity IDs.

### Connections

Each entity's `connections` array describes its outgoing I/O:

```json
{
    "output": "OnTrigger",
    "target": "module_door",
    "input": "Open",
    "parameter": "",
    "delay": 0.5,
    "timesToFire": -1,
    "targetType": 2
}
```

| Friendly field | Legacy export field | Meaning or default                                     |
| -------------- | ------------------- | ------------------------------------------------------ |
| `output`       | `m_outputName`      | Output event on the source entity.                     |
| `target`       | `m_targetName`      | Target expression.                                     |
| `input`        | `m_inputName`       | Input on the target entity.                            |
| `parameter`    | `m_overrideParam`   | Input parameter; defaults to an empty string.          |
| `delay`        | `m_flDelay`         | Delay in seconds; defaults to `0`.                     |
| `timesToFire`  | `m_nTimesToFire`    | Fire count; defaults to `-1` (unlimited).              |
| `targetType`   | `m_targetType`      | Target lookup type; defaults to `2` (name).            |
| `paramMap`     | `m_paramMap`        | Optional parameter mapping retained in the connection. |

Numbers in parameter maps must fit the VMAP writer's signed 32-bit integer, unsigned
64-bit integer, or finite single-precision floating-point representation. Out-of-range
values are rejected instead of being silently changed.

Output, target, and input must be non-empty strings. Delay must be finite and non-negative;
the fire count must be an integer of `-1` or greater. Parameters may be strings, numbers,
or booleans; `paramMap`, when supplied, must be an object or `null`.

Target expressions are preserved, including contextual targets such as `!activator` and
`!caller`; their meaning depends on the event at runtime. Duplicate target names and I/O
cycles are allowed. A target outside the module can be intentional: unresolved target
warnings do not prevent export, but you must supply the destination in the final map.

### Box Geometry

Use `box` for editable entity-owned mesh geometry:

```json
{
    "properties": {
        "classname": "func_door",
        "targetname": "module_door",
        "origin": [320, 0, 64]
    },
    "box": {
        "size": [16, 128, 128],
        "center": [0, 0, 0],
        "material": "materials/dev/graygrid.vmat"
    }
}
```

`size` gives the full dimensions, all strictly positive. `center` defaults to `[0, 0, 0]`.
Both are local to the entity, so the entity's origin and angles place the box in the map.
Ordinary solid boxes default to `materials/dev/graygrid.vmat`. Trigger and tool volumes
use their class's tool material, usually `materials/tools/toolstrigger.vmat`. Omit
`material` to use these defaults, or supply a material available in your target game.

For recognized brush classes and `trigger_*` classes without an explicit box, conversion
generates a 64 by 64 by 64 placeholder box and reports a warning. These placeholders do
not recover original geometry. Supply an explicit box when its shape matters, and inspect
generated placeholders in Hammer. Compiled brush-model references are replaced by the
generated geometry.

Model-backed props still need a valid `model` resource in the target game. Adding a box
does not replace that requirement. Missing model warnings need review before compiling.

## Limits

Conversion validates JSON structure, transforms, box dimensions, and connection fields.
It does not perform exhaustive validation against the target game's FGD or guarantee that
a class, property, output, or input is supported by that game. Materials, models, scripts,
and other external assets are not bundled or recreated.

Compiled `entitylumpname` references are removed with a warning. Configure any required
template membership in Hammer.

Exported entity JSON contains static properties and I/O, without the original trigger
geometry, template instance transforms, or runtime state. Importing it creates a new
entity module rather than reconstructing the original map. See the
[format support limitations](./format-support.md#entity-json-import).
