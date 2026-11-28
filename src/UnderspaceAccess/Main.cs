using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MelonLoader;

[assembly: MelonInfo(typeof(UnderspaceAccess.Main), "UnderspaceAccess", UnderspaceAccess.Main.Version, "Claude")]
[assembly: MelonGame(null, "Underspace")]

namespace UnderspaceAccess
{
    public class Main : MelonMod
    {
        public const string Version = "0.10.8";
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        bool updateKeyWas;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public static string Dir;
        bool greeted;
        static long lastFrameTicks;

        public override void OnInitializeMelon()
        {
            Dir = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "UnderspaceAccess");
            Directory.CreateDirectory(Dir);
            Log.Path = Path.Combine(Dir, "UnderspaceAccess.log");
            File.WriteAllText(Log.Path, "");
            var stamp = File.GetLastWriteTime(typeof(Main).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss");
            Log.Write($"PROBE UnderspaceAccess {Version} build {stamp} loaded; {(Environment.Is64BitProcess ? 64 : 32)}-bit; exe {Environment.ProcessPath}; MelonLoader {MelonLoader.Properties.BuildInfo.Version}");
            Speech.Start(Path.Combine(Dir, "prism.dll"));
            Updater.Start(Version, (t, i) => Speech.Say(t, i), Log.Write);
            new Thread(Watchdog) { IsBackground = true, Name = "UnderspaceAccess loading watch" }.Start();
        }

        public override void OnUpdate()
        {
            Interlocked.Exchange(ref lastFrameTicks, DateTime.UtcNow.Ticks);
            if (!greeted)
            {
                greeted = true;
                Speech.Say("Underspace Access loaded.");
            }
            Updater.Update();
            // Shift+F12: download a new version of the mod (Updater.cs). F12 is free in the game and the mod.
            GetWindowThreadProcessId(GetForegroundWindow(), out uint fgPid);
            bool updateKey = fgPid == (uint)Environment.ProcessId && (GetAsyncKeyState(0x7B) & 0x8000) != 0 && (GetAsyncKeyState(0x10) & 0x8000) != 0;
            if (updateKey && !updateKeyWas) Updater.Key();
            updateKeyWas = updateKey;
            MenuNav.Update();
            Trade.Tick();
            InGame.Update();
        }

        public override void OnApplicationQuit() => Mission.ReleaseMouse();

        public override void OnFixedUpdate()
        {
            try { Flight.FixedTick(); } catch (Exception e) { Log.Write("FixedTick error: " + e.Message); }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Log.Write($"scene loaded {buildIndex} {sceneName}");
            MenuNav.SceneLoaded(sceneName);
            InGame.quietUntil = UnityEngine.Time.unscaledTime + 8f;
        }

        // Starting or loading a game freezes the game for up to two minutes, so the mod
        // cannot speak from the game itself. This thread notices the freeze and says so.
        static void Watchdog()
        {
            bool loading = false;
            DateTime nextSay = DateTime.MinValue, since = DateTime.MinValue;
            while (true)
            {
                Thread.Sleep(500);
                long t = Interlocked.Read(ref lastFrameTicks);
                if (t == 0) continue;
                double stalled = (DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc)).TotalSeconds;
                if (!loading && stalled > 3 && GameInFront())
                {
                    loading = true;
                    since = DateTime.UtcNow;
                    nextSay = DateTime.UtcNow.AddSeconds(20);
                    Log.Write("game frozen: loading");
                    Speech.Say("Loading, please wait. This can take up to two minutes.");
                }
                else if (loading && stalled < 1)
                {
                    loading = false;
                    Log.Write($"game running again after {(DateTime.UtcNow - since).TotalSeconds:0} seconds");
                    Speech.Say("Loading finished.");
                }
                else if (loading && DateTime.UtcNow >= nextSay)
                {
                    nextSay = DateTime.UtcNow.AddSeconds(20);
                    Speech.Say("Still loading.");
                }
            }
        }

        static bool GameInFront()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)Environment.ProcessId;
        }
    }
}
