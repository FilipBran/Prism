using System.IO.Compression;
using System.Reflection;
using Allumeria;
using HarmonyLib;
using Tommy;

namespace Prism;

public sealed class Prism : IExternalLoader
{
    // Game root directory
    public static string GameDir = Directory.GetCurrentDirectory();
    
    private static readonly List<Mod> Mods = new();
    private static readonly SortedDictionary<string, TomlTable> ConfigTables = new();
    
    // List of loaded mods
    public static IReadOnlyList<Mod> LoadedMods => Mods;
    
    void IExternalLoader.Init()
    {
        GameDir = Directory.GetCurrentDirectory();

        var logsDir = Path.Combine(GameDir, "logs");
        Directory.CreateDirectory(logsDir);

        // Setup AdvancedLogger
        AdvancedLogger.Init(Path.Combine(
            logsDir,
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
            }
            else if (Mods.Count > 1)
            {
                Game.FULL_VERSION = $"{Game.FULL_VERSION} [Prism {Constants.Version} | {Mods.Count} mods]";
            }
            else if (Mods.Count == 0)
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
        if (!Directory.Exists(Path.Combine(GameDir, Constants.PrismRoot, Constants.PackagesDirectory)))
        {
            Directory.CreateDirectory(Path.Combine(GameDir, Constants.PrismRoot, Constants.PackagesDirectory));
        }
        // config
        if (!Directory.Exists(Path.Combine(GameDir, Constants.PrismRoot, Constants.ConfigDirectory)))
        {
            Directory.CreateDirectory(Path.Combine(GameDir, Constants.PrismRoot, Constants.ConfigDirectory));
        }
        // cache
        if (!Directory.Exists(Path.Combine(GameDir, Constants.PrismRoot, Constants.CacheDirectory)))
        {
            Directory.CreateDirectory(Path.Combine(GameDir, Constants.PrismRoot, Constants.CacheDirectory));
        }
    }

    private void LoadMods()
    {
        var packagesDir = Path.Combine(GameDir, Constants.PrismRoot, Constants.PackagesDirectory);
        if (!Directory.Exists(packagesDir))
        {
            AdvancedLogger.Log($"Prism => Packages directory does not exist: {packagesDir}", AdvancedLogger.LogType.Warning);
            return;
        }

        // Find all packages with the (Constants.PackageExtension) extension
        foreach (var file in Directory.GetFiles(packagesDir, $"*{Constants.PackageExtension}"))
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

                Type[] exportedTypes;
                try
                {
                    exportedTypes = asm.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    AdvancedLogger.Log($"Prism => Failed to enumerate exported types in {file}: {rtle}", AdvancedLogger.LogType.Error);
                    foreach (var le in rtle.LoaderExceptions)
                        AdvancedLogger.Log($"Prism => LoaderException for {file}: {le}", AdvancedLogger.LogType.Error);
                    continue;
                }

                var modTypes = exportedTypes
                    .Where(t => typeof(Mod).IsAssignableFrom(t) && !t.IsAbstract &&
                                t.GetConstructor(Type.EmptyTypes) != null)
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
                        var mod = (Mod)Activator.CreateInstance(t)!;

                        if (string.IsNullOrWhiteSpace(mod.Id))
                        {
                            AdvancedLogger.Log($"Prism => Mod type {t.FullName} in {file} has empty Id. Skipping.", AdvancedLogger.LogType.Error);
                            continue;
                        }

                        var invalid = Path.GetInvalidFileNameChars();
                        var safeId = new string(mod.Id.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());

                        if (ConfigTables.ContainsKey(mod.Id) || Mods.Any(m => m.Id == mod.Id))
                        {
                            AdvancedLogger.Log($"Prism => Duplicate mod Id '{mod.Id}' detected in {file}. Skipping to avoid conflicts.", AdvancedLogger.LogType.Error);
                            continue;
                        }

                        var configPath = Path.Combine(GameDir, Constants.PrismRoot, Constants.ConfigDirectory, $"{safeId}.toml");

                        if (File.Exists(configPath))
                        {
                            using var reader = new StreamReader(configPath);
                            ConfigTables.TryAdd(mod.Id, TOML.Parse(reader));
                        }
                        else
                        {
                            var configEntry = archive.GetEntry("config.toml");
                            if (configEntry != null)
                            {
                                AdvancedLogger.Log($"Prism => Mod's {mod.Name}({mod.Id}) config file is missing! Creating it!", AdvancedLogger.LogType.Warning);

                                using (var src = configEntry.Open())
                                using (var dst = File.Create(configPath))
                                {
                                    src.CopyTo(dst);
                                }

                                using var reader = new StreamReader(configPath);
                                ConfigTables.TryAdd(mod.Id, TOML.Parse(reader));
                            }
                            else
                            {
                                AdvancedLogger.Log($"Prism => {file} does not contain config.toml (skipping config load)", AdvancedLogger.LogType.Warning);
                            }
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

    public static TomlTable? GetConfigTable(string tableId)
    {
        ConfigTables.TryGetValue(tableId, out var table);
        return table;
    }

    public static TomlNode? GetConfigValue(string modId, string key)
    {
        ConfigTables.TryGetValue(modId, out var table);
        var value = table?[key];
        return value;
    }
    private static class Constants
    {
        public const string Version = "0.3.1";
        public const string PrismRoot = "prism";
        public const string PackagesDirectory = "packages";
        public const string CacheDirectory = "cache";
        public const string ConfigDirectory = "config";
        public const string PackageExtension = ".prism";
        public const string TargetGameVersion = "0.14";
        // Allumeria/prism/packages
        //          /cache
        //          /config
    }
}