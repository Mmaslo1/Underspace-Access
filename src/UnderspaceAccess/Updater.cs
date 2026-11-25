using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UnderspaceAccess
{
    /// <summary>
    /// Update check (added 2026-11-25). When the game starts, asks GitHub for the newest release.
    /// If it is newer it says so; Shift+F12 downloads the zip while you play, and a hidden
    /// PowerShell unpacks it into the game folder after the game closes, then a Windows voice
    /// says it is done. Nothing is downloaded without the key press.
    /// No check at all: create UserData\{OffFile}.
    /// The same file is used in Hoop Land Access and Underspace Access (only the settings at the top differ).
    /// </summary>
    internal static class Updater
    {
        // ---- per-mod settings ----
        private const string Repo = "Mmaslo1/Underspace-Access";
        private const string ModName = "Underspace Access";
        private const string AssetPrefix = "UnderspaceAccess_";   // release zip: UnderspaceAccess_<version>.zip
        private const string ModDll = "UnderspaceAccess.dll";     // must be inside the zip's Mods folder
        private const string OffFile = "UnderspaceAccess_update_off.txt";
        private const string KeyName = "Shift+F12";
        // --------------------------

        private static volatile string _state = "";   // checking, off, error, current, available, downloading, failed, ready
        private static string _spoken = "";
        private static string _have = "", _new = "", _url = "", _assetName = "";
        private static Action<string, bool> _say;
        private static Action<string> _log;

        public static void Start(string have, Action<string, bool> say, Action<string> log)
        {
            _have = have; _say = say; _log = log;
            string off = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, OffFile);
            if (File.Exists(off)) { _state = "off"; _spoken = "off"; return; }
            _state = "checking"; _spoken = "checking";
            Task.Run(Check);
        }

        private static HttpClient NewClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(AssetPrefix.TrimEnd('_') + "-updater");
            return c;
        }

        private static void Check()
        {
            try
            {
                using var c = NewClient();
                c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = c.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest").GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                string ver = tag.TrimStart('v', 'V');
                foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    if (name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    { _assetName = name; _url = a.GetProperty("browser_download_url").GetString() ?? ""; break; }
                }
                _log("Update check: newest " + ver + ", installed " + _have + (_url == "" ? ", no zip in the release" : ""));
                if (_url != "" && IsNewer(ver, _have)) { _new = ver; _state = "available"; }
                else _state = "current";
            }
            catch (Exception e) { _log("Update check failed: " + e.Message); _state = "error"; }
        }

        private static bool IsNewer(string a, string b)
        {
            string[] pa = a.Split('.'), pb = b.Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out int xi) ? xi : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out int yi) ? yi : 0;
                if (x != y) return x > y;
            }
            return false;
        }

        private const string ReadyText = "Update downloaded. It installs by itself after you close the game. If Windows asks for permission, press Alt+Y.";

        /// <summary>Call every frame (main thread): speaks state changes once.</summary>
        public static void Update()
        {
            string st = _state;
            if (st == _spoken) return;
            _spoken = st;
            if (st == "available") _say("Update available: " + ModName + " " + _new + ". You have " + _have + ". Press " + KeyName + " to download it.", false);
            else if (st == "ready") _say(ReadyText, false);
            else if (st == "failed") _say("The update download failed. Press " + KeyName + " to try again.", false);
        }

        /// <summary>The update key.</summary>
        public static void Key()
        {
            switch (_state)
            {
                case "available":
                case "failed":
                    _state = "downloading"; _spoken = "downloading";
                    _say("Downloading the update. Keep playing; I will tell you when it is ready.", true);
                    Task.Run(Download);
                    break;
                case "downloading": _say("Still downloading the update.", true); break;
                case "ready": _say(ReadyText, true); break;
                case "current": _say(ModName + " " + _have + " is the newest version.", true); break;
                case "error": _say("Could not check for updates. Maybe no internet. It checks again next time you start the game.", true); break;
                case "off": _say("Update check is off. Delete " + OffFile + " in the UserData folder to turn it on.", true); break;
                default: _say("Still checking for updates.", true); break;
            }
        }

        private static void Download()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), AssetPrefix + "update");
                Directory.CreateDirectory(dir);
                string zip = Path.Combine(dir, _assetName);
                using (var c = NewClient())
                {
                    byte[] data = c.GetByteArrayAsync(_url).GetAwaiter().GetResult();
                    File.WriteAllBytes(zip, data);
                }
                bool hasDll = false;
                using (var z = ZipFile.OpenRead(zip))
                    foreach (var e in z.Entries)
                        if (e.FullName.Replace('\\', '/').Equals("Mods/" + ModDll, StringComparison.OrdinalIgnoreCase)) hasDll = true;
                if (!hasDll) throw new Exception("the zip has no Mods/" + ModDll);
                StartInstallWhenGameCloses(zip, dir);
                _log("Update downloaded: " + zip);
                _state = "ready";
            }
            catch (Exception e) { _log("Update download failed: " + e.Message); _state = "failed"; }
        }

        private static string Q(string s) => s.Replace("'", "''");

        private static void StartInstallWhenGameCloses(string zip, string dir)
        {
            string game = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
            string ps1 = Path.Combine(dir, "install_when_game_closes.ps1");
            string unpackPs1 = Path.Combine(dir, "unpack.ps1");
            string unpack = "Expand-Archive -LiteralPath '" + Q(zip) + "' -DestinationPath '" + Q(game) + "' -Force -ErrorAction Stop";
            File.WriteAllText(unpackPs1, "try { " + unpack + " } catch { exit 1 }\r\nexit 0\r\n", new UTF8Encoding(true));
            string script =
                "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
                "Wait-Process -Id " + Process.GetCurrentProcess().Id + "\r\n" +
                "Start-Sleep -Seconds 3\r\n" +
                "Add-Type -AssemblyName System.Speech\r\n" +
                "$v = New-Object System.Speech.Synthesis.SpeechSynthesizer\r\n" +
                "$v.Speak('Installing the " + ModName + " update, version " + _new + ".')\r\n" +
                "$ok = $false\r\n" +
                "try { " + unpack + "; $ok = $true } catch { }\r\n" +
                "if (-not $ok) {\r\n" +
                "  $v.Speak('Windows will ask for permission. Press Alt plus Y.')\r\n" +
                "  $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','\"" + Q(unpackPs1) + "\"'\r\n" +
                "  $ok = ($p -and $p.ExitCode -eq 0)\r\n" +
                "}\r\n" +
                "if ($ok) { $v.Speak('" + ModName + " update installed. Start the game when you are ready.') }\r\n" +
                "else { $v.Speak('The " + ModName + " update was not installed. You can install it later from the GitHub page.') }\r\n";
            File.WriteAllText(ps1, script, new UTF8Encoding(true));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
            Process.Start(psi);
        }
    }
}
