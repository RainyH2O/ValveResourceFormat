using System.ComponentModel;
using System.Linq;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Utils;

/// <summary>
///     Entity property manager for unified property management
/// </summary>
public static class EntityPropertyManager
{
    /// <summary>
    ///     Entity reference properties for relationship finding
    /// </summary>
    public static readonly HashSet<string> ReferenceProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "parentname", "entitytemplate", "filtername", "target",

        "filter01", "filter02", "filter03", "filter04", "filter05",
        "filter06", "filter07", "filter08", "filter09", "filter10",

        "template01", "template02", "template03", "template04", "template05",
        "template06", "template07", "template08", "template09", "template10",
        "template11", "template12", "template13", "template14", "template15", "template16",

        "damagetarget", "targetentityname", "sourceentityname", "breakableentity",
        "constraintsystem", "entity", "entity_name", "entityfiltername",

        "branch01", "branch02", "branch03", "branch04", "branch05",
        "branch06", "branch07", "branch08",

        "goalentity", "pathcorner", "nextpathcorner", "previouspathcorner",
        "first_path_node", "fighttarget",

        "fallbacktarget", "master"
    };

    /// <summary>
    ///     Connection properties for entity I/O
    /// </summary>
    public static readonly HashSet<string> ConnectionProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "m_outputName", "m_targetName", "m_inputName", "m_overrideParam", "m_flDelay", "m_nTimesToFire"
    };

    /// <summary>
    ///     Base properties for all entities
    /// </summary>
    public static readonly HashSet<string> BaseProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "classname", "targetname", "hammeruniqueid", "origin", "angles", "spawnflags"
    };

    /// <summary>
    ///     Rendering properties
    /// </summary>
    public static readonly HashSet<string> RenderProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "rendermode", "renderamt", "renderfx", "rendercolor", "skin", "body",
        "fadedist", "fademindist", "fademaxdist", "fadescale", "fadeplayervisibilityfarz"
    };

    /// <summary>
    ///     Physics properties
    /// </summary>
    public static readonly HashSet<string> PhysicsProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "health", "mass", "material", "physdamagescale", "inertiaScale", "massScale",
        "damagetoenabledmotion", "explosive_damage", "explosive_force", "explosive_radius"
    };

    /// <summary>
    ///     Audio properties
    /// </summary>
    public static readonly HashSet<string> AudioProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "message", "volume", "attenuation", "pitch", "radius", "preset",
        "firesound", "startsound", "stopsound", "movesound"
    };

    /// <summary>
    ///     Prefix to property mapping
    /// </summary>
    public static readonly Dictionary<string, HashSet<string>> PrefixProperties =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["logic_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spawnflags", "delay", "initialstate", "enabled", "disabled",
                "startvalue", "min", "max", "threshold",
                "case01", "case02", "case03", "case04", "case05", "case06",
                "case07", "case08", "case09", "case10", "case11", "case12",
                "case13", "case14", "case15", "case16", "case17", "case18",
                "case19", "case20", "case21", "case22", "case23", "case24",
                "case25", "case26", "case27", "case28", "case29", "case30"
            },
            ["trigger_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spawnflags", "wait", "filtername", "onstartouch", "onendtouch",
                "damage", "damagecap", "damagetype", "damagemodel", "nodmgforce", "damageforce",
                "pushdir", "pushforce", "speed", "thinkalways", "startdisabled"
            },
            ["math_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "startvalue", "min", "max", "startdisabled"
            },
            ["point_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "damage", "damageradius", "damagetype", "damagedelay", "damagetarget"
            },
            ["filter_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "damagetype", "negated", "filtername", "classname", "filterclass",
                "filtermass", "filterteam", "filtertype"
            },
            ["prop_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "model", "skin", "body", "health", "mass", "material",
                "physdamagescale", "inertiaScale", "massScale", "damagetoenabledmotion"
            },
            ["light_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_light", "brightness", "color", "range", "style", "pattern",
                "_constant_attn", "_linear_attn", "_quadratic_attn", "_fifty_percent_distance"
            },
            ["env_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "model", "rendermode", "renderamt", "renderfx", "rendercolor",
                "message", "spawnflags", "health",
                "explosionmagnitude", "explosionradius", "explosion_magnitude", "explosion_radius"
            },
            ["ai_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "target", "squadname", "efficiency", "hinttype", "nodeid"
            },
            ["npc_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "model", "health", "spawnequipment", "squadname",
                "spawnflags", "sleepstate", "wakeradius", "wakesquad"
            },
            ["ambient_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "message", "volume", "attenuation", "pitch",
                "radius", "sourceentityname", "preset"
            },
            ["func_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spawnflags", "speed", "wait", "lip", "health",
                "movedir", "distance", "blockdamage", "dmg",
                "startsound", "stopsound", "movesound", "returndelay"
            },
            ["phys_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spawnflags", "magnitude", "radius", "targetentityname", "impulse",
                "explosive_damage", "explosive_force", "explosive_radius"
            },
            ["game_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "master", "globalstate", "triggermode", "initialstate"
            },
            ["info_"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spawnflags", "enabled", "message"
            }
        };

    /// <summary>
    ///     Special entity properties mapping
    /// </summary>
    public static readonly Dictionary<string, HashSet<string>> SpecialEntityProperties =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["worldspawn"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "skyname", "mapversion", "maxpropscreenwidth", "minpropscreenwidth",
                "detailvbsp", "detailmaterial"
            },
            ["player"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "targetname", "origin", "angles", "model"
            }
        };

    /// <summary>
    ///     Get recommended properties for classname
    /// </summary>
    public static HashSet<string> GetPropertiesForClassname(string classname)
    {
        var result = new HashSet<string>(BaseProperties, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(classname))
        {
            return result;
        }

        if (SpecialEntityProperties.TryGetValue(classname, out var specialProps))
        {
            result.UnionWith(specialProps);
        }

        foreach (var (prefix, properties) in PrefixProperties)
        {
            if (classname.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(properties);
                break;
            }
        }

        result.UnionWith(ReferenceProperties);

        return result;
    }

    /// <summary>
    ///     Get smart properties filtered by existing properties
    /// </summary>
    public static HashSet<string> GetSmartProperties(List<EntityLump.Entity> entities)
    {
        var existingProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            foreach (var prop in entity.Properties.Properties)
            {
                existingProperties.Add(prop.Key);
            }
        }

        var recommendedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classnameGroups = entities.GroupBy(e => e.GetProperty<string>("classname", "unknown"));

        foreach (var group in classnameGroups)
        {
            var classname = group.Key;
            var prefixProperties = GetPropertiesForClassname(classname);
            recommendedProperties.UnionWith(prefixProperties);
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recommendedProp in recommendedProperties)
        {
            if (existingProperties.Contains(recommendedProp))
            {
                result.Add(recommendedProp);
            }
        }

        return result;
    }

    /// <summary>
    ///     Check if property is reference type
    /// </summary>
    public static bool IsReferenceProperty(string propertyName)
    {
        return ReferenceProperties.Contains(propertyName);
    }

    /// <summary>
    ///     Check if property is connection related
    /// </summary>
    public static bool IsConnectionProperty(string propertyName)
    {
        return ConnectionProperties.Contains(propertyName);
    }

    /// <summary>
    ///     Get property category
    /// </summary>
    public static PropertyCategory GetPropertyCategory(string propertyName)
    {
        if (BaseProperties.Contains(propertyName))
        {
            return PropertyCategory.Base;
        }

        if (ReferenceProperties.Contains(propertyName))
        {
            return PropertyCategory.Reference;
        }

        if (ConnectionProperties.Contains(propertyName))
        {
            return PropertyCategory.Connection;
        }

        if (RenderProperties.Contains(propertyName))
        {
            return PropertyCategory.Render;
        }

        if (PhysicsProperties.Contains(propertyName))
        {
            return PropertyCategory.Physics;
        }

        if (AudioProperties.Contains(propertyName))
        {
            return PropertyCategory.Audio;
        }

        return PropertyCategory.Custom;
    }
}

/// <summary>
///     Property category enum
/// </summary>
public enum PropertyCategory
{
    [Description("Base Properties")] Base,

    [Description("Reference Properties")] Reference,

    [Description("Connection Properties")] Connection,

    [Description("Render Properties")] Render,

    [Description("Physics Properties")] Physics,

    [Description("Audio Properties")] Audio,

    [Description("Custom Properties")] Custom
}
