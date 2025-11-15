using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;
using KSP;

namespace KSPLiftoff
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPLiftoffController : MonoBehaviour
    {
        private const string CurrentVersion = "23.0";

        private static bool s_initialized = false;

        // --------------------------------------------------------------------
        // Startup load timer
        // --------------------------------------------------------------------
        private float loadStartTime;
        private float loadEndTime;
        private bool loadComplete = false;

        private bool startupTimerFrozen = false;
        private double frozenTotalLoadTime = 0.0f;
        private bool logWritten = false;

        private bool shadersWarmed = false;
        private static readonly string[] ShaderWarmList = new string[]
        {
            "KSP/Bumped Specular",
            "KSP/Specular",
            "KSP/Diffuse",
            "KSP/Emissive",
            "KSP/Alpha/Unlit Transparent",
            "KSP/Unlit"
        };

        // --------------------------------------------------------------------
        // Small on-screen window
        // --------------------------------------------------------------------
        private Rect windowRect = new Rect(20, 20, 320, 400);
        private bool showDetails = false;
        private bool showProfilerWindow = true;

        // Movable profiler toggle button
        private Rect profilerButtonRect = new Rect(5, 5, 130, 25);
        private bool draggingProfilerButton = false;
        private Vector2 profilerButtonDragOffset = Vector2.zero;

        // --------------------------------------------------------------------
        // Scene & craft timing (Scene-Craft_Stats.log)
        // --------------------------------------------------------------------
        private float sceneStartTime = 0f;
        private float vesselLoadStart = 0f;

        // --------------------------------------------------------------------
        // Paths
        // --------------------------------------------------------------------
        private readonly string logFolder;
        private readonly string loadLogFile;
        private readonly string sceneLogFile;

        private readonly string gameDataRoot;

        // --------------------------------------------------------------------
        // Mod folders to check for changes
        // --------------------------------------------------------------------
        private static readonly string[] ModFoldersToCheck = new string[]
        {
            "GameData/KSPLiftoff",
            "GameData/KSPVoxelSystem",
            "GameData/OceanCurrents",
            "GameData/Squad",
            "GameData/SquadExpansion"
        };

        // --------------------------------------------------------------------
        // Directory caches
        // --------------------------------------------------------------------
        private static readonly ConcurrentBag<string> cachedCFG = new ConcurrentBag<string>();
        private static readonly ConcurrentBag<string> cachedDDS = new ConcurrentBag<string>();
        private static readonly ConcurrentBag<string> cachedModels = new ConcurrentBag<string>();
        private static readonly ConcurrentBag<string> cachedPluginData = new ConcurrentBag<string>();
        private static readonly ConcurrentBag<string> cachedBundles = new ConcurrentBag<string>();

        private static readonly ConcurrentBag<string> cachedLargeTextures = new ConcurrentBag<string>();
        private static readonly ConcurrentBag<string> cachedLargeAudio = new ConcurrentBag<string>();

        // --------------------------------------------------------------------
        // RAM caches
        // --------------------------------------------------------------------
        private static readonly ConcurrentDictionary<string, string> cfgCache =
            new ConcurrentDictionary<string, string>();

        private static readonly ConcurrentDictionary<string, byte[]> ddsHeaderCache =
            new ConcurrentDictionary<string, byte[]>();

        private static readonly ConcurrentDictionary<string, byte[]> modelHeaderCache =
            new ConcurrentDictionary<string, byte[]>();

        private static readonly ConcurrentDictionary<string, string> pluginDataCache =
            new ConcurrentDictionary<string, string>();

        private static readonly ConcurrentDictionary<string, byte[]> assetBundleHeaderCache =
            new ConcurrentDictionary<string, byte[]>();

        // --------------------------------------------------------------------
        // Disk throughput counters
        // --------------------------------------------------------------------
        private long totalBytesRead = 0;
        private long totalReadTimeMs = 0;

        private int ReadAndCount(FileStream fs, byte[] buffer)
        {
            double s = DateTime.Now.TimeOfDay.TotalMilliseconds;
            int read = fs.Read(buffer, 0, buffer.Length);
            double e = DateTime.Now.TimeOfDay.TotalMilliseconds;

            if (read > 0)
            {
                Interlocked.Add(ref totalBytesRead, read);
                Interlocked.Add(ref totalReadTimeMs, (long)(e - s));
            }

            return read;
        }

        // --------------------------------------------------------------------
        // Phase timings
        // --------------------------------------------------------------------
        private readonly ConcurrentDictionary<string, double> timings =
            new ConcurrentDictionary<string, double>();

        private void TimePhase(string name, Action action)
        {
            double s = DateTime.Now.TimeOfDay.TotalMilliseconds;
            try { action(); }
            catch { }
            double e = DateTime.Now.TimeOfDay.TotalMilliseconds;
            timings[name] = e - s;
        }

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------
        public KSPLiftoffController()
        {
            string root = KSPUtil.ApplicationRootPath;
            logFolder = Path.Combine(root, "GameData", "KSPLiftoff", "Logs");
            loadLogFile = Path.Combine(logFolder, "LoadProfile.log");
            sceneLogFile = Path.Combine(logFolder, "Scene-Craft_Stats.log");

            gameDataRoot = Path.Combine(root, "GameData");
        }

        // --------------------------------------------------------------------
        // Awake
        // --------------------------------------------------------------------
        public void Awake()
        {
            // External toggle via Toggle.cfg
            try
            {
                string togglePath = Path.Combine(
                    KSPUtil.ApplicationRootPath,
                    "GameData",
                    "KSPLiftoff",
                    "Toggle.cfg"
                );

                bool enabled = true; // default ON

                if (File.Exists(togglePath))
                {
                    string content = File.ReadAllText(togglePath)
                                         .Trim()
                                         .ToLowerInvariant();
                    if (content == "off")
                        enabled = false;
                }

                if (!enabled)
                {
                    Debug.Log("[KSPLiftoff] Toggle.cfg set to OFF — KSPLiftoff disabled.");
                    Destroy(this);
                    return;
                }
            }
            catch
            {
                // If toggle check fails, just let the mod run
            }

            // Singleton guard
            if (s_initialized)
            {
                Destroy(this);
                return;
            }

            DontDestroyOnLoad(this);
            s_initialized = true;

            loadStartTime = Time.realtimeSinceStartup;
            sceneStartTime = loadStartTime;

            // Background startup optimization
            Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(gameDataRoot))
                        return;

                    TimePhase("Scan", ScanAndIndex);

                    Thread.Sleep(10);

                    TimePhase("Prefetch", DiskPrefetchBooster);
                    Thread.Sleep(10);

                    TimePhase("CFG", PreWarmCFG);
                    Thread.Sleep(10);

                    TimePhase("DDS", PreWarmDDS);
                    Thread.Sleep(10);

                    TimePhase("Models", PreWarmModels);
                    Thread.Sleep(10);

                    TimePhase("PluginData", PreWarmPluginData);
                    Thread.Sleep(10);

                    TimePhase("BundleHeader", PreWarmBundleHeaders);
                    Thread.Sleep(10);

                    TimePhase("BundleBody", PreWarmBundleBodies);
                }
                catch { }
            });

            // Events
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
            try
            {
                GameEvents.onVesselLoaded.Add(OnVesselChangeEvent);
                GameEvents.onVesselChange.Add(OnVesselChangeEvent);
            }
            catch { }
        }

        // =====================================================================
        // SCAN + INDEX HANDLING
        // =====================================================================

        private void ScanAndIndex()
        {
            try
            {
                HybridScanSafe(gameDataRoot);
            }
            catch { }
        }

        private void HybridScanSafe(string root)
        {
            try
            {
                ConcurrentQueue<string> q = new ConcurrentQueue<string>();
                q.Enqueue(root);

                int workers = Math.Max(2, SystemInfo.processorCount - 2);
                Task[] tasks = new Task[workers];

                for (int i = 0; i < workers; i++)
                {
                    tasks[i] = Task.Run(delegate
                    {
                        while (true)
                        {
                            string dir;
                            if (!q.TryDequeue(out dir))
                            {
                                if (q.IsEmpty)
                                    break;
                                Thread.Sleep(1);
                                continue;
                            }

                            try
                            {
                                string[] subs = new string[0];
                                string[] files = new string[0];

                                try { subs = Directory.GetDirectories(dir); } catch { }
                                try { files = Directory.GetFiles(dir); } catch { }

                                for (int s = 0; s < subs.Length; s++)
                                    q.Enqueue(subs[s]);

                                for (int f = 0; f < files.Length; f++)
                                    ClassifyFile(files[f]);
                            }
                            catch { }
                        }
                    });
                }

                Task.WaitAll(tasks);
            }
            catch { }
        }

        private void ClassifyFile(string f)
        {
            string ext = Path.GetExtension(f);
            if (ext == null) return;
            ext = ext.ToLowerInvariant();

            switch (ext)
            {
                case ".cfg":
                    cachedCFG.Add(f);
                    break;

                case ".dds":
                    cachedDDS.Add(f);
                    break;

                case ".mu":
                case ".ksp":
                    cachedModels.Add(f);
                    break;

                case ".json":
                case ".xml":
                case ".txt":
                case ".dat":
                case ".ini":
                    cachedPluginData.Add(f);
                    break;

                case ".bundle":
                case ".unity3d":
                case ".assetbundle":
                case ".kspbundle":
                    cachedBundles.Add(f);
                    break;

                case ".png":
                case ".tga":
                case ".jpg":
                case ".jpeg":
                    TryClassifyLargeTexture(f);
                    break;

                case ".wav":
                case ".ogg":
                    TryClassifyLargeAudio(f);
                    break;
            }
        }

        private void TryClassifyLargeTexture(string f)
        {
            try
            {
                long len = new FileInfo(f).Length;
                if (len >= 300 * 1024)
                    cachedLargeTextures.Add(f);
            }
            catch { }
        }

        private void TryClassifyLargeAudio(string f)
        {
            try
            {
                long len = new FileInfo(f).Length;
                if (len >= 400 * 1024)
                    cachedLargeAudio.Add(f);
            }
            catch { }
        }

        // =====================================================================
        // FILTERS
        // =====================================================================

        private List<string> FilterLargeDDS()
        {
            List<string> list = new List<string>();
            foreach (string f in cachedDDS)
            {
                try
                {
                    long size = new FileInfo(f).Length;
                    if (size >= 300 * 1024)
                        list.Add(f);
                }
                catch { }
            }
            return list;
        }

        private List<string> FilterLargeBundles()
        {
            List<string> list = new List<string>();
            foreach (string f in cachedBundles)
            {
                try
                {
                    long size = new FileInfo(f).Length;
                    if (size >= 1 * 1024 * 1024)
                        list.Add(f);
                }
                catch { }
            }
            return list;
        }

        private List<string> FilterLargeModels()
        {
            List<string> list = new List<string>();
            foreach (string f in cachedModels)
            {
                try
                {
                    long size = new FileInfo(f).Length;
                    if (size >= 300 * 1024)
                        list.Add(f);
                }
                catch { }
            }
            return list;
        }

        // =====================================================================
        // STARTUP DISK PREFETCH BOOSTER
        // =====================================================================

        private void DiskPrefetchBooster()
        {
            try
            {
                List<string> items = new List<string>();
                items.AddRange(FilterLargeDDS());
                items.AddRange(FilterLargeBundles());
                items.AddRange(FilterLargeModels());
                items.AddRange(cachedLargeTextures);
                items.AddRange(cachedLargeAudio);

                if (items.Count == 0)
                    return;

                var groups = items.GroupBy(f => Path.GetDirectoryName(f)).ToList();

                Parallel.ForEach(groups, delegate (IGrouping<string, string> group)
                {
                    try
                    {
                        byte[] buffer = new byte[1024 * 1024];
                        int maxPerDir = 2;

                        List<string> sorted = new List<string>(group);
                        sorted.Sort(delegate (string a, string b)
                        {
                            double sa = 0.0;
                            double sb = 0.0;

                            try { sa = new FileInfo(a).Length / (1024.0 * 1024.0); } catch { }
                            try { sb = new FileInfo(b).Length / (1024.0 * 1024.0); } catch { }

                            return sb.CompareTo(sa);
                        });

                        int taken = 0;

                        for (int i = 0; i < sorted.Count; i++)
                        {
                            if (taken >= maxPerDir)
                                break;

                            string f = sorted[i];

                            try
                            {
                                FileInfo fi = new FileInfo(f);
                                long len = fi.Length;
                                if (len < 300 * 1024) continue;

                                using (FileStream fs = File.OpenRead(f))
                                {
                                    ReadAndCount(fs, buffer);
                                }

                                taken++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        // =====================================================================
        // WARMUPS
        // =====================================================================

        private void PreWarmCFG()
        {
            Parallel.ForEach(cachedCFG, delegate (string f)
            {
                try
                {
                    double s = DateTime.Now.TimeOfDay.TotalMilliseconds;
                    string text = File.ReadAllText(f);
                    double e = DateTime.Now.TimeOfDay.TotalMilliseconds;

                    Interlocked.Add(ref totalBytesRead, text.Length);
                    Interlocked.Add(ref totalReadTimeMs, (long)(e - s));

                    cfgCache[f] = text;
                }
                catch { }
            });
        }

        private void PreWarmDDS()
        {
            var groups = cachedDDS.GroupBy(f => Path.GetDirectoryName(f)).ToList();

            Parallel.ForEach(groups, delegate (IGrouping<string, string> group)
            {
                try
                {
                    byte[] buffer = new byte[256];

                    foreach (string f in group)
                    {
                        try
                        {
                            using (FileStream fs = File.OpenRead(f))
                            {
                                int read = ReadAndCount(fs, buffer);
                                if (read > 0)
                                {
                                    byte[] copy = new byte[read];
                                    Buffer.BlockCopy(buffer, 0, copy, 0, read);
                                    ddsHeaderCache[f] = copy;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private void PreWarmModels()
        {
            Parallel.ForEach(cachedModels, delegate (string f)
            {
                try
                {
                    using (FileStream fs = File.OpenRead(f))
                    {
                        byte[] buffer = new byte[512];
                        ReadAndCount(fs, buffer);
                        modelHeaderCache[f] = buffer;
                    }
                }
                catch { }
            });
        }

        private void PreWarmPluginData()
        {
            Parallel.ForEach(cachedPluginData, delegate (string f)
            {
                try
                {
                    double s = DateTime.Now.TimeOfDay.TotalMilliseconds;
                    string text = File.ReadAllText(f);
                    double e = DateTime.Now.TimeOfDay.TotalMilliseconds;

                    Interlocked.Add(ref totalBytesRead, text.Length);
                    Interlocked.Add(ref totalReadTimeMs, (long)(e - s));

                    pluginDataCache[f] = text;
                }
                catch { }
            });
        }

        private void PreWarmBundleHeaders()
        {
            Parallel.ForEach(cachedBundles, delegate (string f)
            {
                try
                {
                    using (FileStream fs = File.OpenRead(f))
                    {
                        byte[] buffer = new byte[4096];
                        ReadAndCount(fs, buffer);
                        assetBundleHeaderCache[f] = buffer;
                    }
                }
                catch { }
            });
        }

        private void PreWarmBundleBodies()
        {
            Parallel.ForEach(cachedBundles, delegate (string f)
            {
                try
                {
                    using (FileStream fs = File.OpenRead(f))
                    {
                        long len = fs.Length;
                        if (len <= 0) return;

                        int chunk = 64 * 1024;
                        byte[] buffer = new byte[chunk];

                        ReadAndCount(fs, buffer);

                        if (len > chunk * 2)
                        {
                            fs.Seek(len - chunk, SeekOrigin.Begin);
                            ReadAndCount(fs, buffer);
                        }
                    }
                }
                catch { }
            });
        }

        // =====================================================================
        // SHADER + MESH WARM (SAFE)
        // =====================================================================

        private void WarmShadersAndMeshesSafe()
        {
            if (shadersWarmed)
                return;

            shadersWarmed = true;

            try
            {
                // Small set of common KSP shaders, already listed in ShaderWarmList.
                for (int i = 0; i < ShaderWarmList.Length; i++)
                {
                    string shaderName = ShaderWarmList[i];
                    try
                    {
                        Shader s = Shader.Find(shaderName);
                        if (s == null)
                            continue;

                        // Base material shared across variants.
                        Material baseMat = new Material(s);
                        baseMat.hideFlags = HideFlags.DontSave;

                        // Define safe keyword sets for a few common variants.
                        string[][] keywordSets = new string[][]
                        {
                            new string[0],                        // no keywords
                            new string[] { "_NORMALMAP" },
                            new string[] { "_EMISSION" },
                            new string[] { "_ALPHATEST_ON" },
                            new string[] { "_NORMALMAP", "_EMISSION" }
                        };

                        // Tiny mesh to exercise CPU-side mesh/material paths.
                        Mesh mesh = new Mesh();
                        mesh.vertices = new Vector3[]
                        {
                            Vector3.zero,
                            Vector3.right * 0.1f,
                            Vector3.up * 0.1f
                        };
                        mesh.triangles = new int[] { 0, 1, 2 };
                        mesh.RecalculateBounds();

                        for (int k = 0; k < keywordSets.Length; k++)
                        {
                            try
                            {
                                string[] kws = keywordSets[k];

                                Material m = new Material(baseMat);
                                m.hideFlags = HideFlags.DontSave;

                                for (int j = 0; j < kws.Length; j++)
                                {
                                    m.EnableKeyword(kws[j]);
                                }

                                // Render once to warm variant usage. No camera dependency, immediate mode.
                                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        
        // =====================================================================
        // PARTMODULE WARM (SAFE REFLECTION)
        // =====================================================================

        private void WarmPartModulesSafe()
        {
            try
            {
                if (PartLoader.LoadedPartsList == null)
                    return;

                HashSet<Type> moduleTypes = new HashSet<Type>();

                foreach (AvailablePart ap in PartLoader.LoadedPartsList)
                {
                    if (ap == null || ap.partPrefab == null)
                        continue;

                    Part p = ap.partPrefab;
                    if (p.Modules == null)
                        continue;

                    for (int i = 0; i < p.Modules.Count; i++)
                    {
                        PartModule pm = p.Modules[i];
                        if (pm == null) continue;

                        Type t = pm.GetType();
                        if (!moduleTypes.Contains(t))
                            moduleTypes.Add(t);
                    }
                }

                foreach (Type t in moduleTypes)
                {
                    try
                    {
                        BindingFlags flags = BindingFlags.Instance |
                                             BindingFlags.Public |
                                             BindingFlags.NonPublic |
                                             BindingFlags.DeclaredOnly;

                        FieldInfo[] fields = t.GetFields(flags);
                        PropertyInfo[] props = t.GetProperties(flags);
                        MethodInfo[] methods = t.GetMethods(flags);

                        int dummy = (fields != null ? fields.Length : 0)
                                  + (props != null ? props.Length : 0)
                                  + (methods != null ? methods.Length : 0);

                        if (dummy < 0)
                        {
                            // never hit; keeps compiler happy
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

// =====================================================================
        // SCENE & CRAFT TIMING
        // =====================================================================

        private void AppendSceneRecord(string sceneName, float duration)
        {
            try
            {
                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);

                using (StreamWriter sw = new StreamWriter(sceneLogFile, true))
                {
                    sw.WriteLine(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                        "  [SCENE] " + sceneName + " loaded in " +
                        duration.ToString("F2") + " sec");
                }
            }
            catch { }
        }

        private void OnVesselChangeEvent(Vessel v)
        {
            if (v == null) return;

            vesselLoadStart = Time.realtimeSinceStartup;
            StartCoroutine(FinishVesselLoad(v));
        }

        private System.Collections.IEnumerator FinishVesselLoad(Vessel v)
        {
            yield return null; // wait 1 frame

            if (v == null) yield break;

            float dur = Time.realtimeSinceStartup - vesselLoadStart;
            int parts = (v.parts != null) ? v.parts.Count : 0;

            AppendCraftRecord(v.vesselName, dur, parts);
        }

        private void AppendCraftRecord(string name, float dur, int parts)
        {
            try
            {
                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);

                using (StreamWriter sw = new StreamWriter(sceneLogFile, true))
                {
                    sw.WriteLine(
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [CRAFT] \"{name}\" loaded in {dur:F2} sec, Parts: {parts}");
                }
            }
            catch { }
        }


        // =====================================================================
        // LOGGING
        // =====================================================================

        private void WriteLogFile(double totalTime)
        {
            try
            {
                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);

                using (StreamWriter sw = new StreamWriter(loadLogFile, true))
                {
                    sw.WriteLine("============================================================");
                    sw.WriteLine("KSPLiftoff Load Profile - Version " + CurrentVersion + " - " + DateTime.Now);
                    sw.WriteLine("============================================================");
                    sw.WriteLine("Total Load Time: " + totalTime.ToString("F2") + " sec");
                    sw.WriteLine();
                    sw.WriteLine("-- Phase Timing (ms) --");

                    foreach (KeyValuePair<string, double> kvp in timings.OrderBy(k => k.Key))
                        sw.WriteLine(kvp.Key + ": " + kvp.Value.ToString("F1") + " ms");

                    sw.WriteLine();
                    sw.WriteLine("-- File Counts --");
                    sw.WriteLine("CFG:          " + cfgCache.Count);
                    sw.WriteLine("DDS headers:  " + ddsHeaderCache.Count);
                    sw.WriteLine("Models:       " + modelHeaderCache.Count);
                    sw.WriteLine("PluginData:   " + pluginDataCache.Count);
                    sw.WriteLine("BundleHdr:    " + assetBundleHeaderCache.Count);

                    sw.WriteLine();
                    sw.WriteLine("-- Disk Throughput --");
                    double mb = totalBytesRead / (1024.0 * 1024.0);
                    sw.WriteLine("Read MB:   " + mb.ToString("F2"));
                    sw.WriteLine("Time ms:   " + totalReadTimeMs.ToString("F2"));
                    if (totalReadTimeMs > 0)
                    {
                        double mbps = mb / (totalReadTimeMs / 1000.0);
                        sw.WriteLine("MB/sec:    " + mbps.ToString("F1"));
                    }
                    else
                    {
                        sw.WriteLine("MB/sec:    n/a");
                    }

                    sw.WriteLine();
                    sw.WriteLine("-- Mod Changes Detected --");
                    WriteModChangeSection(sw);

                    sw.WriteLine();
                    sw.WriteLine("-- System Info --");
                    sw.WriteLine("CPU Cores: " + SystemInfo.processorCount);

                    int maxT, availT, dummy;
                    ThreadPool.GetMaxThreads(out maxT, out dummy);
                    ThreadPool.GetAvailableThreads(out availT, out dummy);
                    sw.WriteLine("ThreadPool: " + (maxT - availT) + "/" + maxT + " busy");

                    sw.WriteLine("============================================================");
                    sw.WriteLine();
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------------
        // Mod change section
        // ---------------------------------------------------------------------
        private void WriteModChangeSection(StreamWriter sw)
        {
            bool any = false;

            for (int i = 0; i < ModFoldersToCheck.Length; i++)
            {
                string rel = ModFoldersToCheck[i];
                try
                {
                    string full = System.IO.Path.Combine(
                        KSPUtil.ApplicationRootPath,
                        rel.Replace('/', System.IO.Path.DirectorySeparatorChar));

                    if (!Directory.Exists(full))
                        continue;

                    DirectoryInfo di = new DirectoryInfo(full);
                    DateTime dt = di.LastWriteTime;

                    sw.WriteLine(full + " (LastModified: " + dt.ToString("yyyy-MM-dd HH:mm:ss") + ")");
                    any = true;
                }
                catch { }
            }

            if (!any)
            {
                sw.WriteLine("None");
            }
        }

        // =====================================================================
        // LEVEL LOADED
        // =====================================================================

        private void OnLevelLoaded(GameScenes scene)
        {
            // Freeze startup timer on first MAINMENU
            if (!startupTimerFrozen && scene == GameScenes.MAINMENU)
            {
                loadComplete = true;
                loadEndTime = Time.realtimeSinceStartup;
                frozenTotalLoadTime = loadEndTime - loadStartTime;
                startupTimerFrozen = true;
            }

            // Record scene timing (skip pure LOADING pseudo-scene)
            if (scene != GameScenes.LOADING)
            {
                float now = Time.realtimeSinceStartup;
                float dur = now - sceneStartTime;

                AppendSceneRecord(scene.ToString(), dur);
                sceneStartTime = now;
            }
        }

        private void OnDestroy()
        {
            try { GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded); } catch { }
            try { GameEvents.onVesselLoaded.Remove(OnVesselChangeEvent); } catch { }
            try { GameEvents.onVesselChange.Remove(OnVesselChangeEvent); } catch { }
        }

        // =====================================================================
        // UPDATE: trigger shader/mesh warm & delayed log write
        // =====================================================================

        public void Update()
        {
            try
            {
                if (startupTimerFrozen && !logWritten)
                {
                    if (!shadersWarmed && HighLogic.LoadedScene == GameScenes.MAINMENU)
                    {
                        // Warm shaders first (variant-aware).
                        TimePhase("ShaderMeshWarm", WarmShadersAndMeshesSafe);
                        // Then warm reflection metadata for PartModules.
                        TimePhase("PartModuleWarm", WarmPartModulesSafe);
                    }

                    WriteLogFile(frozenTotalLoadTime);
                    logWritten = true;
                }
            }
            catch { }
        }

        // =====================================================================
        // GUI WINDOW
        // =====================================================================

        public void OnGUI()
        {
            // ==============================================
            // Movable Profiler Toggle Button
            // ==============================================

            Event e = Event.current;

            // Draw button at its draggable position
            if (GUI.Button(profilerButtonRect, showProfilerWindow ? "Hide Profiler" : "Show Profiler"))
            {
                showProfilerWindow = !showProfilerWindow;
            }

            // Drag logic
            if (profilerButtonRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    draggingProfilerButton = true;
                    profilerButtonDragOffset = e.mousePosition - new Vector2(profilerButtonRect.x, profilerButtonRect.y);
                    e.Use();
                }
            }

            if (draggingProfilerButton)
            {
                if (e.type == EventType.MouseDrag)
                {
                    profilerButtonRect.x = e.mousePosition.x - profilerButtonDragOffset.x;
                    profilerButtonRect.y = e.mousePosition.y - profilerButtonDragOffset.y;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    draggingProfilerButton = false;
                }
            }

            if (!showProfilerWindow)
                return;

            windowRect = GUI.Window(
                GetInstanceID(),
                windowRect,
                DrawWindow,
                "KSPLiftoff – Load Profiler v" + CurrentVersion);
        }

        private void DrawWindow(int id)
        {
            GUIStyle label = new GUIStyle(GUI.skin.label);
            label.fontSize = 14;
            label.normal.textColor = Color.white;

            float now = Time.realtimeSinceStartup;
            float elapsed = loadComplete
                ? (float)frozenTotalLoadTime
                : now - loadStartTime;

            GUI.Label(new Rect(10, 25, 280, 20),
                "Load Time: " + elapsed.ToString("F2") + " sec", label);

            GUI.Label(new Rect(10, 45, 280, 20),
                "Hotkey: RightCtrl + P", label);

            GUI.Label(new Rect(10, 65, 280, 20), "", label); // blank line

            // side-by-side buttons
            if (GUI.Button(new Rect(10, 90, 140, 22),
                showDetails ? "Hide Details" : "Show Details"))
                showDetails = !showDetails;

            if (GUI.Button(new Rect(160, 90, 140, 22),
                "Close"))
                showProfilerWindow = false;

            if (showDetails)
            {
                int y = 120;
                foreach (KeyValuePair<string, double> kvp in timings.OrderBy(k => k.Key))
                {
                    GUI.Label(new Rect(10, y, 290, 20),
                        kvp.Key + ": " + kvp.Value.ToString("F1") + " ms", label);
                    y += 20;
                }
            }

            GUI.DragWindow();
        }
    }
}
