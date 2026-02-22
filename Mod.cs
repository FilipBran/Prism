using System.Reflection;
using HarmonyLib;

namespace Prism;

public abstract class Mod
{
    /// <summary>Friendly display name.</summary>
    public abstract string Name { get; }

    /// <summary>Mod version string.</summary>
    public virtual string Version => "1.0.0";

    /// <summary>Unique id. Default: mod type full name.</summary>
    public virtual string Id => GetType().FullName ?? GetType().Name;

    /// <summary>Set by Prism after loading.</summary>
    public Assembly? Assembly { get; internal set; }

    /// <summary>Set by Prism after loading.</summary>
    public string? PackagePath { get; internal set; }
    
    public Harmony? Harmony { get; internal set; }

    /// <summary>
    /// Called by Prism once the mod is constructed and metadata is assigned.
    /// Mod authors put their startup logic here.
    /// </summary>
    public virtual void Init()
    {
    }
}