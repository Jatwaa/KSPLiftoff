# 🚀 KSPLiftoff – KSP Loading, Scene & Craft Profiler  
**Version 23.0**

KSPLiftoff is a performance, load-time, and scene-transition profiler for **Kerbal Space Program 1.12.x** designed to dramatically improve visibility into game loading, mod performance, and scene switching overhead.

It is not a performance mod in the traditional sense—  
It is a **diagnostic powerhouse** that reveals EVERYTHING slowing down your KSP install.

---

# ✨ Features

## 📌 1. Game Load Profiler  
Automatically records detailed metrics every time the game loads:

### **Phase Timing:**
- CFG Load Time  
- DDS Header Scan  
- Model Scan  
- Asset Prefetch  
- PluginData Scan  
- Directory Scan  
- Shader Warmup Time  
- Total Load Time  

### **Disk Throughput Measurement:**
- MB read  
- Total ms spent  
- MB/sec throughput

### **Mod Change Detection (fast)**
Detects folder timestamp changes to show if a mod updated or changed.

Output is written to GameData/KSPLiftoff/LoadProfile.log

## 📌 2. Scene & Craft Load Profiler  
Logs EVERY scene transition with exact timestamps and durations:

Examples:
- PSYSTEM load time  
- MAINMENU load time  
- SPACECENTER load time  
- EDITOR load time  
- FLIGHT load time  

### **Craft Loading Metrics**
When a vessel loads:
- Vessel name  
- Time to load  
- Part count  

Output written to: GameData/KSPLiftoff/Scene-Craft_Stats.log

## 📌 3. Profiler UI Window (in-game)  
Accessible at any time with:

### **🔑 Default Hotkey: RightCtrl + P**
- Hotkey is editable inside the window  
- Hotkey is saved persistently to: GameData/KSPLiftoff/PluginData/Hotkey.cfg


### **UI Elements:**
- Toggle profiler visibility  
- Reload logs button  
- Hotkey editing field  
- Clean and minimal UI  
- Respects KSP's stock UI layering  

### **Removed (by design):**
- Dragging  
- Resizing  
- Scroll view  
(to reduce GUI errors such as `DrawMesh requires material.SetPass before!`)

## 📌 4. Toggle.cfg – Enable/Disable Entire Mod
If this file exists inside: GameData/KSPLiftoff/Toggle.cfg
Contains the text option 
  On the mod loads
  Off the mod does not load

 This allows the Mod to bypass all logic and KSP loads normally without profiling.

Perfect for:
- Benchmark A/B comparisons  
- Removing overhead for normal play  
- Troubleshooting mod interactions  

---

# 📊 Performance Summary (from testing with near vanilla KSP)

KSPLiftoff has recorded load times typically around:

### ⭐ **44–48 seconds total game load**  
(Down from much higher before profiling improvements)

### **Scene Load Benchmarks:**
- MAINMENU → ~6 sec  
- EDITOR → 5–10 sec  
- SPACECENTER → 12–25 sec  
- FLIGHT → 24–37 sec  
- Craft load → ~3 sec for 40–50 part vessels

---

# 📁 File Output Locations
GameData/KSPLiftoff/LoadProfile.log
GameData/KSPLiftoff/Scene-Craft_Stats.log
GameData/KSPLiftoff/PluginData/Hotkey.cfg
GameData/KSPLiftoff/Toggle.cfg

Source File: KSPLiftoffController.cs

Compile against:
- .NET Framework 3.5 or 4.x  
- Assembly-CSharp.dll  
- UnityEngine.dll  
- UnityEngine.CoreModule.dll  
- UnityEngine.IMGUIModule.dll  

Output DLL should be placed in: GameData/KSPLiftoff/Plugins/

# 🌐 GitHub Workflow Support
KSPLiftoff includes documentation and optional scripts to automatically push each new version to GitHub with versioned file naming.

# 🔧 License
MIT License 

# 🤝 Contributions
PRs welcome!  
Feel free to submit improvements, optimizations, or profiling extensions.

# 🚀 Enjoy smoother insights into KSP!
KSPLiftoff is designed to help diagnose, optimize, and monitor KSP performance—especially in heavily modded installs.



