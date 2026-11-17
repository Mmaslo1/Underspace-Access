using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UnderspaceAccess
{
    // All speech goes through Speech.Say. Only this file calls Prism, from its own thread.
    internal static class Speech
    {
        [DllImport("prism", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr prism_init(IntPtr cfg);
        [DllImport("prism", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr prism_registry_create_best(IntPtr ctx);
        [DllImport("prism", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr prism_backend_name(IntPtr backend);
        [DllImport("prism", CallingConvention = CallingConvention.Cdecl)] static extern void prism_backend_free(IntPtr backend);
        [DllImport("prism", CallingConvention = CallingConvention.Cdecl)] static extern int prism_backend_output(IntPtr backend, byte[] text, [MarshalAs(UnmanagedType.U1)] bool interrupt);
        [DllImport("prism", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr prism_error_string(int err);
        [DllImport("prism", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr prism_version_string();

        static readonly BlockingCollection<(string text, bool interrupt)> queue = new();
        static IntPtr prismHandle, ctx, backend;
        static Thread thread;
        static string lastText = "";
        static DateTime lastTime;

        public static void Start(string dllPath)
        {
            if (thread != null) return;
            if (!NativeLibrary.TryLoad(dllPath, out prismHandle)) { Log.Write("could not load prism.dll at " + dllPath); return; }
            NativeLibrary.SetDllImportResolver(typeof(Speech).Assembly, (name, asm, path) => name == "prism" ? prismHandle : IntPtr.Zero);
            thread = new Thread(Run) { IsBackground = true, Name = "UnderspaceAccess speech" };
            thread.Start();
        }

        // interrupt=true replaces what is being said; false queues after it.
        public static void Say(string text, bool interrupt = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            // While a spoken line of dialogue plays, nothing the mod says on its own cuts it off:
            // it waits its turn. The player's next key press lifts this (see Unprotect).
            if (interrupt && DateTime.UtcNow < protectUntil) interrupt = false;
            // Drop exact repeats within a short time (two hooks reporting the same item).
            if (text == lastText && (DateTime.UtcNow - lastTime).TotalMilliseconds < 250) return;
            lastText = text; lastTime = DateTime.UtcNow;
            queue.Add((text, interrupt));
        }

        static DateTime protectUntil;

        // Keeps a line of dialogue from being cut off for about as long as it takes to say it.
        public static void Protect(string text)
        {
            double seconds = Math.Min(8.0, 0.6 + 0.065 * (text?.Length ?? 0));
            protectUntil = DateTime.UtcNow.AddSeconds(seconds);
        }

        public static void Unprotect() => protectUntil = DateTime.MinValue;

        static void Run()
        {
            try
            {
                Log.Write("prism version " + Marshal.PtrToStringUTF8(prism_version_string()));
                ctx = prism_init(IntPtr.Zero);
                backend = prism_registry_create_best(ctx);
                if (backend == IntPtr.Zero) { Log.Write("prism: no speech backend"); return; }
                Log.Write("prism backend " + Marshal.PtrToStringUTF8(prism_backend_name(backend)));
            }
            catch (Exception e) { Log.Write("prism start failed: " + e); return; }

            foreach (var (text, interrupt) in queue.GetConsumingEnumerable())
            {
                try
                {
                    int r = prism_backend_output(backend, Encoding.UTF8.GetBytes(text + "\0"), interrupt);
                    Log.Write($"say [{(interrupt ? "i" : "q")}] {text}" + (r == 0 ? "" : " -> " + Marshal.PtrToStringUTF8(prism_error_string(r))));
                    if (r != 0 && Marshal.PtrToStringUTF8(prism_error_string(r)).ToLowerInvariant().Contains("internal"))
                    {
                        // Screen reader restarted: rebuild the backend once.
                        prism_backend_free(backend);
                        backend = prism_registry_create_best(ctx);
                    }
                }
                catch (Exception e) { Log.Write("say failed: " + e.Message); }
            }
        }
    }

    internal static class Log
    {
        static readonly object gate = new();
        public static string Path;
        public static void Write(string line)
        {
            lock (gate)
            {
                try { File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine); } catch { }
            }
        }
    }
}
