using System.IO.Compression;
using System.Reflection;
using Allumeria;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Prism;


public sealed class Prism : IExternalLoader
{
    public static string GameDir;

    private static readonly List<Mod> Mods = new();

    public static IReadOnlyList<Mod> LoadedMods => Mods;

    private static string PackagesDir => Path.Combine(GameDir, "mods", Constants.PackagesDirectory);

    void IExternalLoader.Init()
    {
        GameDir = Directory.GetCurrentDirectory();

        // Setup AdvancedLogger
        AdvancedLogger.Init(Path.Combine(
            GameDir,
            "logs",
            $"prism-{DateTime.Now.Year}_{DateTime.Now.Month}_{DateTime.Now.Day}_{DateTime.Now.Hour}_{DateTime.Now.Minute}_{DateTime.Now.Second}.txt"));

        AdvancedLogger.Log("Prism => Prism initialized successfully!", AdvancedLogger.LogType.Info);

        CreateMissingPrismDirs();
        LoadMods();

        Game.FULL_VERSION = $"{Game.FULL_VERSION} [Prism {Constants.Version}  | {Mods.Count} mods]";
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

    public void RegisterMod(Mod mod)
    {
        Mods.Add(mod);
    }

    private void LoadMods()
    {
        if (!Directory.Exists(PackagesDir))
        {
            AdvancedLogger.Log($"Prism => Packages directory missing: {PackagesDir}", AdvancedLogger.LogType.Warning);
            return;
        }

        foreach (var file in Directory.GetFiles(PackagesDir, $"*{Constants.PackageExtension}"))
        {
            AdvancedLogger.Log($"Prism => Found mod package {file}", AdvancedLogger.LogType.Info);

            try
            {
                using ZipArchive archive = ZipFile.Open(file, ZipArchiveMode.Read);

                var entry = archive.GetEntry("Mod.dll");
                if (entry == null)
                {
                    AdvancedLogger.Log($"Prism => {file} does not contain Mod.dll", AdvancedLogger.LogType.Warning);
                    continue;
                }

                byte[] asmBytes;
                using (var s = entry.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    asmBytes = ms.ToArray();
                }

                var asm = Assembly.Load(asmBytes);

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
                            AdvancedLogger.Log($"Prism => Failed to create mod instance for {t.FullName}", AdvancedLogger.LogType.Warning);
                            continue;
                        }

                        mod.Assembly = asm;
                        mod.PackagePath = file;
                        mod.Harmony = new Harmony(mod.Id);
                        mod.Harmony.PatchAll();
                        mod.Init();
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
        public const string Version = "0.2";
        public const string PackagesDirectory = "prism-packages";
        public const string CacheDirectory = "prism-cache";
        public const string AllumeriaId = "Allumeria";
        public const string PackageExtension = ".prism.zip";
    }
}