using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace UnderspaceAccess
{
    // Speaks the game's own texts while playing: messages, system names, conversations,
    // quests and objectives, tutorial popups, subtitles, alerts and the interaction prompt.
    // Each watched text is spoken when it appears or changes.
    //   In the game (no menu open): F10 = where am I and what to do, F11 = repeat the last message.
    internal static class InGame
    {
        static readonly Dictionary<string, string> last = new();
        static float nextPoll;
        static string lastMessage = "";
        static readonly HashSet<string> spokenTips = new();

        public static void Update()
        {
            try { Tick(); }
            catch (Exception e) { Log.Write("InGame error: " + e.Message); nextPoll = Time.unscaledTime + 2f; }
        }

        static void Tick()
        {
            if (!MenuNav.Active && MenuNav.Focused())
            {
                if (MenuNav.Pressed(MenuNav.VK_F10)) Speech.Say(MenuNav.Down(MenuNav.VK_SHIFT) ? Controls() : WhereAmI());
                else if (MenuNav.Pressed(MenuNav.VK_F11)) Speech.Say(lastMessage.Length > 0 ? lastMessage : "No message yet.");
                Navigator.Tick(PlayerController.Player);
            }
            Flight.Tick();
            Mission.Tick();
            Navigator.LateCheck();
            RoomChange();
            RoomLine();
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.1f;

            var msg = MessageHUD.mainHUD;
            if (msg != null)
            {
                Watch("message main", msg.mainDisplay, "");
                if (msg.allMessages != null)
                    foreach (var m in msg.allMessages)
                        if (m != null) Watch("message " + m.GetInstanceID(), m.itemName, "");
            }

            var sys = SystemNameReadout.mainReadout;
            if (sys != null)
            {
                Watch("system", sys.systemName, "Entering ", Text(sys.systemCategory));
                Watch("system big", sys.bigsystemName, "Entering ", Text(sys.bigsystemCategory));
            }

            var comms = CommsUI.mainComms;
            if (comms != null && comms.commsActive && comms.commWindow != null && comms.commWindow.gameObject.activeInHierarchy)
                WatchString("comms", comms.wantedDialogueString, Who(comms.currentCharacterName, comms.commWindow.commName));
            else last.Remove("comms");

            var quest = QuestUI.mainQuest;
            if (quest != null)
            {
                if (quest.questMenu != null && quest.questMenu.activeInHierarchy)
                    WatchString("talk", quest.wantedDialogueString, Who(null, quest.npcName));
                else last.Remove("talk");
                Watch("quest name", quest.questName, "Quest: ", Text(quest.questComplete));
                Watch("objective", quest.questTrackerObjective, "Objective: ");
            }

            var accept = AcceptQuestHUD.mainQuestHUD;
            if (accept != null) Watch("quest accepted", accept.questName, "");

            var cine = CinematicUI.mainCinema;
            if (cine != null) Watch("subtitle", cine.stationSubtitles, Who(null, cine.stationCharacterName));

            var station = StationUI.mainUI;
            if (station != null)
            {
                Watch("station subtitle", station.stationSubtitles, "");
                Watch("prompt", station.primaryInteractionUI, "", null, true);
                Watch("prompt 2", station.secondaryInteractionUI, "", null, true);
                // Whom the game thinks the player is looking at: F talks to them (proven 10/24:
                // F opened the Shipwright's shop while lookingNPC was the Shipwright). The prompt
                // line above already says the key and action ("F Trade").
                var look = station.lookingNPC;
                int lookId = look != null ? look.GetInstanceID() : 0;
                if (lookId != lastLook)
                {
                    lastLook = lookId;
                    Log.Write("looking at NPC: " + (look != null ? look.NPCName : "none"));
                    if (look != null && !MenuNav.Active && !string.IsNullOrWhiteSpace(look.NPCName)) Speech.Say("Looking at " + MenuNav.StripTags(look.NPCName) + ".", false);
                }
            }

            var stats = StatsHUD.mainHUD;
            if (stats != null)
            {
                Watch("alert", stats.alertText, "");
                Watch("tutorial", stats.tutorialText, "");
                Watch("advance", stats.advanceText, "");
                Watch("respawn", stats.respawnTitle, "", Text(stats.respawnDescription));
            }

            var tut = TutorialSystem.mainSystem;
            if (tut != null)
            {
                if (tut.allActiveTooltips != null)
                    foreach (var t in tut.allActiveTooltips)
                        if (t != null && Visible(t.linkedTooltip) && spokenTips.Add(Clean(t.linkedTooltip.text))) // each tip once per session
                            Watch("tip " + t.GetInstanceID(), t.linkedTooltip, "Tip: ");
                Watch("tutorial pause", tut.pausepopupDescription, "Tutorial: ");
            }
        }

        // Says the new room after a warp key (F2 to F7) or a door: "Now in the bar."
        public static string LastTask = "";
        public static float LastTaskAt;
        static int lastRoom, lastLook;
        public static float quietUntil;
        static void RoomChange()
        {
            var pc = PlayerController.Player;
            var interior = pc != null ? pc.currentInterior : null;
            var room = interior != null && interior.gameObject.activeInHierarchy ? interior.currentRoom : null;
            int id = room != null ? room.GetInstanceID() : 0;
            if (id == lastRoom) return;
            bool first = lastRoom == 0;
            lastRoom = id;
            if (room == null || first) return;
            if (Time.unscaledTime < quietUntil) return; // the game settles the room for a few seconds after loading
            string name = !string.IsNullOrWhiteSpace(room.roomName) ? MenuNav.StripTags(room.roomName) : RoomName(room.roomType);
            if (name == "room") name = RoomName(pc.currentRoom);
            int people = People(pc, room).Count;
            pendingRoomLine = $"Now in the {name}." + (people > 0 ? $" {people} {(people == 1 ? "person" : "people")} here." : "");
            // The game switches the room's Z and C buttons a moment after the room (10/25: the keys
            // said were the previous room's), so the line waits a second.
            pendingRoomAt = Time.unscaledTime + 1.2f;
        }

        static string pendingRoomLine;
        static float pendingRoomAt = -1f;
        static void RoomLine()
        {
            if (pendingRoomAt < 0 || Time.unscaledTime < pendingRoomAt) return;
            pendingRoomAt = -1f;
            string line = pendingRoomLine;
            string keys = RoomServiceKeys();
            if (keys.Length > 0) line += " " + keys;
            Log.Write("room changed: " + line);
            lastMessage = line;
            Speech.Say(line);
        }

        // Speaks the text when it is visible and different from what was said for it last.
        static void Watch(string key, TMP_Text t, string prefix, string extra = null, bool interrupt = false)
        {
            if (!Visible(t)) { last.Remove(key); return; }
            Say(key, Clean(t.text), prefix, extra, interrupt);
        }

        static void WatchString(string key, string text, string prefix)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Say(key, Clean(text), prefix, null, true);
        }

        static void Say(string key, string text, string prefix, string extra, bool interrupt)
        {
            if (string.IsNullOrWhiteSpace(text)) { last.Remove(key); return; }
            if (last.TryGetValue(key, out var old) && old == text) return;
            last[key] = text;
            if (key.StartsWith("tip") || key.StartsWith("tutorial") || key.StartsWith("prompt") || key == "advance")
            {
                text = DropMouse(text);
                if (text.Length < 3) { Log.Write($"game text [{key}] only mouse words, not said"); return; }
            }
            string line = prefix + text + (string.IsNullOrWhiteSpace(extra) ? "" : ". " + Clean(extra));
            Log.Write($"game text [{key}] {line}");
            // Remembered for Semicolon (what now): 11/3 the tutorial "Talk to the chaptermaster" was said once and lost.
            if (key == "objective" || (key == "tutorial" && (text.Contains("Talk to") || text.Contains("Go to") || text.Contains("Press")))) { LastTask = text; LastTaskAt = Time.unscaledTime; }
            if (key.StartsWith("message") || key == "comms") Flight.GameMessage(text);
            string hint = KeyboardHint(key, text);
            // The same news seconds ago from another part of the screen: said once only.
            if (!key.StartsWith("prompt") && key != "advance" && SaidRecently(line)) { Log.Write("  (already said, skipped)"); if (hint != null) Speech.Say(hint, false); return; }
            Remember(line);
            if (!key.StartsWith("prompt")) lastMessage = line;
            Speech.Say(line, interrupt);
            if (key == "tutorial" && text.Contains("Talk to")) Speech.Say("After closing this, press Semicolon: the mod picks the person to talk to and guides you there.", false);
            if (hint != null) Speech.Say(hint, false);
            // What a character says must not be cut off by the reply buttons read right after it.
            if (key == "talk" || key == "comms" || key.Contains("subtitle")) Speech.Protect(line);
        }

        // The game's tutorials talk about the mouse and the screen: add the mod's keys (11/25).
        static string KeyboardHint(string key, string text)
        {
            string t = text.ToUpperInvariant();
            if (key == "respawn")
                {
                    // 11/27: after dying in the campaign the player picked Mike (freeplay) and loaded the wrong game.
                    var cmd = CampaignManager.mainManager;
                    string pilot = cmd != null && cmd.currentCampaign > 0 ? "Kardoz, your campaign pilot, not Mike," : "your pilot";
                    return $"Reload is selected. Press Enter, then pick {pilot} and press Enter, then the newest save, the first one in the list, and Enter, then Load Game. Down arrow: Quit to Menu.";
                }
            if (key != "tutorial") return null;
            if (t.StartsWith("CONTACT WINDOW"))
                return "With the mod: Page Down and Page Up go through nearby things. Shift Page Down changes the category. Enter selects. F2 flies there.";
            if (t.StartsWith("COMBAT"))
                return "With the mod: Comma targets the nearest enemy. Hold Period: the mod turns you onto it, flies you closer, and fires when in range. In campaign fights stay and fight; in freeplay, 3 or more enemies: run, double-tap W. High engines stop your guns. Tap G for shields, hold G to repair the hull.";
            if (t.StartsWith("ABYSSDRIVE"))
                return "With the mod: press Semicolon to pick the mission waypoint, then F2, and the autopilot flies you there.";
            return null;
        }

        static bool Visible(TMP_Text t)
        {
            if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f) return false;
            foreach (var g in t.GetComponentsInParent<CanvasGroup>())
            {
                if (g.alpha < 0.05f) return false;
                if (g.ignoreParentGroups) break;
            }
            return true;
        }

        static string Text(TMP_Text t) => t != null ? MenuNav.StripTags(t.text) : "";

        static string Who(string name, TMP_Text nameText)
        {
            if (string.IsNullOrWhiteSpace(name)) name = Text(nameText);
            name = MenuNav.StripTags(name);
            return string.IsNullOrWhiteSpace(name) ? "" : name + ": ";
        }

        static readonly Regex brackets = new(@"\[([^\]]+)\]", RegexOptions.Compiled);
        // ---------- no double reading, no mouse talk (11/3) ----------
        // The game shows many texts twice (subtitle with and without the name, a message and the
        // objective, the tutorial box and the screen around it). Each is said once.
        static readonly List<(string norm, float at)> recent = new();
        static readonly Regex notLetters = new(@"[^a-z0-9]+", RegexOptions.Compiled);
        internal static string Norm(string s) => notLetters.Replace((s ?? "").ToLowerInvariant(), "");
        internal static void Remember(string s)
        {
            var n = Norm(s);
            if (n.Length == 0) return;
            recent.Add((n, Time.unscaledTime));
            if (recent.Count > 30) recent.RemoveAt(0);
        }
        // True when this text (or a longer line holding it) was said in the last few seconds.
        internal static bool SaidRecently(string s, float seconds = 6f)
        {
            var n = Norm(s);
            if (n.Length == 0) return true;
            // "Objective: X" and "X" are the same news.
            if (n.StartsWith("objective")) n = n.Substring(9);
            foreach (var (norm, at) in recent)
            {
                if (Time.unscaledTime - at > seconds) continue;
                if (norm == n || (n.Length >= 12 && norm.Contains(n)) || (norm.Length >= 12 && n.Contains(norm) && n.Length - norm.Length < 12)) return true;
            }
            return false;
        }

        // Mouse buttons mean nothing to a keyboard player: drop the sentences that need them.
        static readonly Regex mouseSentence = new(@"[^.!?]*\b(LMB|RMB|MMB|mouse|Mouse)\b[^.!?]*[.!?]?", RegexOptions.Compiled);
        internal static string DropMouse(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = mouseSentence.Replace(s, "");
            return Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '.', '*');
        }

        internal static string Clean(string s)
        {
            s = (s ?? "").Replace("<<HINT>>", " Hint: ");
            s = Regex.Replace(s, @"<<\w+>>", "");
            s = ManualReader.Clean(s);
            s = brackets.Replace(s, "$1");
            // Missions point at things by colour and icon; the player gets the keys instead.
            s = Regex.Replace(s, @"\s+by following the \w+ (exclamation points?|waypoint)", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+or\s+(press\s+.*?\s+and\s+)?select the [^.]*?icon", "", RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\.{2,}", ".");
        }

        // Station room buttons: each has a key (F1 to F9) that warps the player to that room.
        public static List<string> RoomKeys()
        {
            var list = new List<(string key, string text)>();
            foreach (var b in UnityEngine.Object.FindObjectsOfType<UIStationRoomButton>())
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                string key = b.linkedTutorialButton != null && b.linkedTutorialButton.linkedText != null ? Clean(b.linkedTutorialButton.linkedText.text) : "";
                string tip = b.linkedElement != null ? Clean(b.linkedElement.toolTip) : "";
                if (tip.Length == 0) tip = RoomName(b.roomType);
                if (key.Length == 0 && tip.Length == 0) continue;
                list.Add((key, tip.TrimEnd('.')));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.key.PadLeft(4), b.key.PadLeft(4)));
            var result = new List<string>();
            foreach (var (key, text) in list) result.Add(key.Length > 0 ? key + ": " + text : text);
            return result;
        }

        // Each room has shortcut buttons the game shows on screen: Z (Station Interaction 1) and
        // C (Station Interaction 2) open them from anywhere in the room, no walking needed.
        // Source: FirstPersonMovement.Update in the SDK (keyStation_Action1/2 -> launchToSpace,
        // openMissions, openEquipmentDealer, ...).
        public static string RoomServiceKeys()
        {
            var fpm = UnityEngine.Object.FindObjectOfType<FirstPersonMovement>();
            if (fpm == null) return "";
            var z = new List<string>(); var c = new List<string>();
            void Add(List<string> to, GameObject b, string what) { if (b != null && b.activeInHierarchy && !to.Contains(what)) to.Add(what); }
            Add(z, fpm.launchButton1, "launch your ship into space"); Add(z, fpm.launchButton2, "launch your ship into space");
            Add(z, fpm.jobButton, "Job Board"); Add(z, fpm.equipmentDealerButton, "equipment dealer");
            Add(z, fpm.traderButton, "cargo trader"); Add(z, fpm.shipdealerButton, "ship dealer"); Add(z, fpm.underspaceButton, "Underspace information");
            Add(c, fpm.trainTravelButton, "train travel"); Add(c, fpm.newsButton, "News Board"); Add(c, fpm.repairButton, "repair and restock");
            Add(c, fpm.storageButton, "storage"); Add(c, fpm.shipCustomizationButton, "ship customization"); Add(c, fpm.underspaceRegionButton, "storm regions");
            string r = (z.Count > 0 ? "Z: " + string.Join(", ", z) + ". " : "") + (c.Count > 0 ? "C: " + string.Join(", ", c) + "." : "");
            if (r.Length > 0) Log.Write("room service keys: " + r);
            return r.Trim();
        }

        public static string RoomName(Il2CppUnderspace.stationRoomTypes t) => t switch
        {
            Il2CppUnderspace.stationRoomTypes.dockingBay => "launch bay",
            Il2CppUnderspace.stationRoomTypes.equipmentDealer => "equipment dealer",
            Il2CppUnderspace.stationRoomTypes.cargoTrader => "cargo trader",
            Il2CppUnderspace.stationRoomTypes.jobBar => "bar, for jobs",
            Il2CppUnderspace.stationRoomTypes.shipDealer => "ship dealer",
            Il2CppUnderspace.stationRoomTypes.underspaceInfo => "Underspace information",
            Il2CppUnderspace.stationRoomTypes.PLAYERHOUSE => "your house",
            _ => "room"
        };

        static string WhereAmI()
        {
            var parts = new List<string>();
            var pc = PlayerController.Player;
            var interior = pc != null ? pc.currentInterior : null;
            if (interior != null && interior.gameObject.activeInHierarchy)
            {
                var room = interior.currentRoom;
                string roomName = room != null && !string.IsNullOrWhiteSpace(room.roomName) ? MenuNav.StripTags(room.roomName) : RoomName(pc.currentRoom);
                parts.Add($"Docked at {MenuNav.StripTags(interior.stationName)}, in the {roomName}");
                var people = People(pc, room);
                if (people.Count > 0) parts.Add("People here: " + string.Join(", ", people));
                string service = RoomServiceKeys();
                if (service.Length > 0) parts.Add(service.TrimEnd('.'));
                var keys = RoomKeys();
                if (keys.Count > 0) parts.Add("Room keys: " + string.Join(", ", keys));
            }
            var sys = SystemNameReadout.mainReadout;
            if (sys != null && sys.systemName != null && !string.IsNullOrWhiteSpace(sys.systemName.text))
                parts.Add("System: " + Clean(sys.systemName.text) + (sys.systemCategory != null ? ", " + Clean(sys.systemCategory.text) : ""));
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var quest = QuestUI.mainQuest;
            if (quest != null)
            {
                string q = Text(quest.questTrackerName), o = Text(quest.questTrackerObjective);
                if (q.Length > 0) parts.Add("Quest: " + q);
                if (o.Length > 0) parts.Add("Objective: " + Clean(o));
            }
            var station = StationUI.mainUI;
            if (Flight.InSpace) parts.Insert(0, Flight.Status().TrimEnd('.'));
            else if (station != null && Visible(station.primaryInteractionUI)) parts.Add(Clean(station.primaryInteractionUI.text));
            Log.Write("where am I, scene " + scene + ": " + string.Join(" | ", parts));
            return parts.Count > 0 ? string.Join(". ", parts) + "." : "No location or objective shown right now.";
        }

        // Named characters in the player's room, nearest first, with distance in meters.
        static List<string> People(PlayerController pc, BaseRoom room)
        {
            var found = new List<(float d, string name)>();
            Vector3 me = pc.playerController != null ? pc.playerController.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<StationNPC>())
            {
                if (npc == null || !npc.isActiveAndEnabled || string.IsNullOrWhiteSpace(npc.NPCName)) continue;
                if (room != null && !npc.transform.IsChildOf(room.transform)) continue;
                float d = Vector3.Distance(me, npc.transform.position);
                if (d > 60f) continue;
                found.Add((d, MenuNav.StripTags(npc.NPCName)));
            }
            found.Sort((a, b) => a.d.CompareTo(b.d));
            var list = new List<string>();
            foreach (var (d, name) in found.GetRange(0, Math.Min(6, found.Count))) list.Add($"{name} {Mathf.RoundToInt(d)} meters");
            return list;
        }

        // Every game control and its key, from the game's own key settings, written to a text
        // file the player can read, and the walking keys spoken.
        public static string ControlsFile = @"C:\Users\micha\Downloads\UnderspaceAccess\Underspace_Controls.txt";
        static string Controls()
        {
            var kb = OptionsMenu.mainMenu != null ? OptionsMenu.mainMenu.mainKeyboard : null;
            if (kb == null || kb.allKeys == null) return "The key list is not available here.";
            var lines = new List<string> { "UNDERSPACE CONTROLS (your current keys, from the game's settings)", "" };
            var walk = new List<string>();
            foreach (var c in kb.allKeys)
            {
                if (c == null) continue;
                string name = MenuNav.StripTags(c.commandName);
                if (c.isCategoryButton) { lines.Add(""); lines.Add("== " + name + " =="); continue; }
                string key = "";
                try { key = Clean(kb.parseKeyboardString("/" + c.parseReplaceName)); } catch { }
                if (key.Length == 0 || key == "/" + c.parseReplaceName) key = "not set";
                lines.Add(name + ": " + key);
                string low = name.ToLowerInvariant();
                if (low.Contains("walk") || low.Contains("forward") || low.Contains("backward") || low.Contains("interact") || low.Contains("jump") || low.Contains("sprint") || low.Contains("run"))
                    walk.Add(name + " " + key);
            }
            lines.AddRange(new[] { "", "== Underspace Access mod keys (in a station, menus closed) ==",
                "Z and C: the room's own services, no walking needed (launch bay Z = launch; bar Z = Job Board, C = News; equipment Z = dealer, C = repair; trader Z = trade, C = storage; ship dealer Z = ships, C = customize). The mod says them when you enter a room.",
                "Page Up / Page Down: category of places (People, Doors, Things, Chairs, Everything)",
                "Up / Down arrows: previous / next place (name, meters, direction)",
                "Hold Q / E (or Left / Right arrow): turn left / right (with Shift: turn slowly). You hear Facing <name> when you face the chosen place.",
                "Home: turn to face the chosen place", "End: where is the chosen place now",
                "Delete: stop the walking directions",
                "While you hold W A S D the mod says the direction (straight ahead, ahead to your left, to your right, behind you) and the meters.",
                "F10: where am I. Shift+F10: controls. F11: repeat last message.",
                "", "== Underspace Access mod keys (in space, flying) ==",
                "Page Down / Page Up: next / previous nearby thing from the game's contact list (name, kind, enemy or not, distance, direction)",
                "Shift+Page Down / Shift+Page Up: category: Important, Ships, Solars, Wreckage, Loot, All (what your scanner sees nearby),",
                "  then MAP, PLACES IN THIS SYSTEM (every known station, planet, jumpgate, laneline at any distance)",
                "  and MAP, OTHER STAR SYSTEMS (systems next door first).",
                "Enter: select the thing as your target (same as clicking it). On a map place: set a route there. On a star system: set a route to its jumpgate.",
                "F3: the autopilot follows the route (game key, auto-follow). F2: fly straight to the target. F: dock with the target (station, planet, laneline, jumpgate).",
                "L: manual docking on/off (game key). Keep it OFF so the autopilot docks for you.",
                "End: where is my target (distance, direction, degrees left/right and up/down, hull and shields)",
                "Hold Left / Right / Up / Down arrows: steer the nose. Shift: slowly. You hear On target when you point at the target.",
                "Hold Home: steer the nose onto your target by itself (for aiming).",
                "F10: ship status (hull, shields, energy, speed, throttle, autopilot, target). F11: repeat last message.",
                "Game keys in space: F2 autopilot fly to target, F dock with target, F3 follow route, F1 cancel autopilot, F4 fly in formation with target,",
                "W / S throttle up / down, double tap W high engines, X reverse, Shift thrust, Space mouse follow on/off,",
                "Comma target nearest enemy, hold Period aim and fire (or right mouse button), Semicolon what to do now and select the mission waypoint, G recharge shields, hold G repair hull, B collect cargo, Tab menu, U map, O quest log." });
            try { System.IO.File.WriteAllLines(ControlsFile, lines); } catch (Exception e) { Log.Write("controls file failed: " + e.Message); }
            Log.Write($"controls: {lines.Count} lines written; walk keys: {string.Join(" | ", walk)}");
            if (Flight.InSpace) return "In space: Page Down and Page Up, nearby things. Shift Page Down, category; the last two are the map. Enter, select target or set a route. F2, fly there. F, dock. F3, follow the route. F1, cancel. L, manual docking on or off. End, where is target. Arrows steer, Home aims at target. W and S throttle. Comma, nearest enemy. Hold Period, aim and fire. Semicolon, what to do now. All keys are in Underspace Controls, in Downloads, Underspace Access.";
            return (walk.Count > 0 ? string.Join(", ", walk) + ". " : "") + "In each room Z and C open its services, the mod says which when you enter. Mod keys in a station: Page Up and Page Down, category. Up and Down arrows, choose a place. Hold Q or E to turn left or right. Home, face the place. End, where is it. Delete, directions off. All keys are in Underspace Controls, in Downloads, Underspace Access.";
        }
    }
}
