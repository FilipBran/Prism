using System.IO.Compression;
using System.Reflection;
using Allumeria;
using HarmonyLib;

namespace Prism;

public sealed class Prism : IExternalLoader
{
    // Game root directory
    public static string GameDir;
    
    private static readonly List<Mod> Mods = new();
    
    // List of loaded mods
    public static IReadOnlyList<Mod> LoadedMods => Mods;
    
    // Packages directory
    private static string PackagesDir => Path.Combine(GameDir, "mods", Constants.PackagesDirectory);
    
    void IExternalLoader.Init()
    {
        // Set game root directory
        GameDir = Directory.GetCurrentDirectory();

        // Setup AdvancedLogger
        AdvancedLogger.Init(Path.Combine(
            GameDir,
            "logs",
            $"prism-{DateTime.Now.Year}_{DateTime.Now.Month}_{DateTime.Now.Day}_{DateTime.Now.Hour}_{DateTime.Now.Minute}_{DateTime.Now.Second}.txt"));
        
        AdvancedLogger.Log("Prism => Prism initialized successfully!", AdvancedLogger.LogType.Info);

        CreateMissingPrismDirs();
        LoadMods();

        // Check if the game version is valid
        if (Constants.TargetGameVersion.Equals(Game.VERSION))
        {
            // Add Prism watermark to the game version and make it more readable and use proper English!
            if (Mods.Count == 1)
            {
                Game.FULL_VERSION = $"{Game.FULL_VERSION} [Prism {Constants.Version} | 1 mod]";
            }else if (Mods.Count > 1)
            {
                Game.FULL_VERSION = $"{Game.FULL_VERSION} [Prism {Constants.Version} | {Mods.Count} mods]";
            }else if (Mods.Count == 0)
            {
                Game.FULL_VERSION = $"{Game.FULL_VERSION} [Prism {Constants.Version}]";
            }
        }
        else
        {
            // Add Prism watermark to the game version
            Game.FULL_VERSION = $"{Game.FULL_VERSION} [Prism {Constants.Version} | INVALID GAME VERSION! | {Mods.Count} mod/s]";
            AdvancedLogger.Log($"Prism => Prism version {Constants.Version} was made for {Constants.TargetGameVersion} but current game version is {Game.VERSION}. Loader will continue to load mods, but note that something may break!", AdvancedLogger.LogType.Warning);
        }
        
        // Change demoMode to true in a release version!
            Game.demoMode = false;
        
    }
    
    private void CreateMissingPrismDirs()
    {
        // packages
        if (!Directory.Exists(PackagesDir))
        {
            AdvancedLogger.Log($"Prism => Directory '{Constants.PackagesDirectory}' doesn't exist!", AdvancedLogger.LogType.Warning);
            AdvancedLogger.Log($"Prism => Creating '{Constants.PackagesDirectory}' at {PackagesDir}", AdvancedLogger.LogType.Warning);
            Directory.CreateDirectory(PackagesDir);
        }
    }

    private void LoadMods()
    {
        // Find all packages with the (Constants.PackageExtension) extension
        foreach (var file in Directory.GetFiles(PackagesDir, $"*{Constants.PackageExtension}"))
        {
            AdvancedLogger.Log($"Prism => Found mod package {file}", AdvancedLogger.LogType.Info);

            try
            {
                // Open found .prism
                using ZipArchive archive = ZipFile.Open(file, ZipArchiveMode.Read);
                
                // Get Mod.dll if exists
                var entry = archive.GetEntry("Mod.dll");
                if (entry == null)
                {
                    AdvancedLogger.Log($"Prism => {file} does not contain Mod.dll", AdvancedLogger.LogType.Warning);
                    continue;
                }
                
                // Load assembly Mod.dll
                byte[] asmBytes;
                using (var s = entry.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    asmBytes = ms.ToArray();
                }

                var asm = Assembly.Load(asmBytes);
                
                // Find all classes inherited from Prism.Mod
                var modTypes = asm
                    .GetExportedTypes()
                    .Where(t => typeof(Mod).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                    .ToArray();

                if (modTypes.Length == 0)
                {
                    AdvancedLogger.Log($"Prism => No Prism.Mod implementations found in {file}", AdvancedLogger.LogType.Warning);
                    continue;
                }

                foreach (var t in modTypes)
                {
                    try
                    {
                        var mod = (Mod?)Activator.CreateInstance(t);
                        if (mod == null)
                        {
                            // Load non-Mod.dll dll
                            byte[] asmBytes2;
                            using (var s = entry.Open())
                            using (var ms = new MemoryStream())
                            {
                                s.CopyTo(ms);
                                asmBytes2 = ms.ToArray();
                            }

                            var asm2 = Assembly.Load(asmBytes);
                            continue;
                        }
                        
                        // Mod-loading magic!
                        mod.Assembly = asm;
                        mod.PackagePath = file;
                        mod.Harmony = new Harmony(mod.Id);
                        mod.Harmony.PatchAll();
                        mod.Init();
                        // Patch again just to be sure...
                        mod.Harmony.PatchAll();
                        Mods.Add(mod);

                        AdvancedLogger.Log($"Prism => Loaded mod '{mod.Name}' v{mod.Version} ({mod.Id})", AdvancedLogger.LogType.Info);
                    }
                    catch (Exception e)
                    {
                        AdvancedLogger.Log($"Prism => Mod init failed for type {t.FullName} in {file}: {e}", AdvancedLogger.LogType.Error);
                    }
                }
            }
            catch (Exception e)
            {
                AdvancedLogger.Log($"Prism => Failed to load mod package {file}: {e}", AdvancedLogger.LogType.Error);
            }
        }
    }
    private static class Constants
    {
        public const string Version = "0.2.1";
        public const string PackagesDirectory = "prism-packages";
        public const string CacheDirectory = "prism-cache";
        public const string AllumeriaId = "Allumeria";
        public const string PackageExtension = ".prism";
        public const string TargetGameVersion = "0.14";
    }
}