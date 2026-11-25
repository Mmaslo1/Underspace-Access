using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace UnderspaceAccess
{
    // Flying in space. The mod reads the game's own contact window (the list of nearby
    // objects a sighted player clicks) and selects targets the same way a click does; the
    // game's own autopilot (F2 Goto Target) then flies there.
    //   Page Down / Page Up: next / previous contact (name, distance, direction, enemy or not).
    //   Shift+Page Down / Shift+Page Up: contact category (Important, Ships, Solars, Wreckage, Loot, All).
    //   Enter: select the contact as your target (same as clicking it).
    //   End: where is my target now (distance, direction, hull and shields).
    //   Arrows (hold): steer the nose left, right, up, down. Shift: slow.
    //   Home (hold): steer the nose onto the target. "On target" when the guns point at it.
    //   F10: ship status. Game keys: F2 fly to target, F1 cancel autopilot, W/S throttle,
    //   Comma nearest enemy, right mouse fire, G shields, hold G hull, B collect cargo.
    // Spoken by itself: autopilot changes, high engines, new enemies, damage, target destroyed.
    internal static class Flight
    {
        const int VK_F1 = 0x70, VK_F2 = 0x71, VK_F3 = 0x72, VK_F = 0x46, VK_L = 0x4C, VK_W = 0x57, VK_S = 0x53, VK_COMMA = 0xBC;

        static bool inSpace, introSaid;
        static float changeSince = -1f;
        static int index = -1;
        static BaseSolar target;
        static string targetName = "";
        static bool targetWasAlive;

        public static bool InSpace => inSpace;

        public static void Tick()
        {
            var ship = Ship.PlayerShip;
            var hud = StatsHUD.mainHUD;
            bool now = ship != null && ship.gameObject.activeInHierarchy && hud != null && hud.currentState == Il2CppUnderspace.playerState.SPACE;
            // While a save loads the game says "space" for a moment before the station appears:
            // only believe a change that lasts a second.
            if (now != inSpace)
            {
                if (changeSince < 0) changeSince = Time.unscaledTime;
                if (Time.unscaledTime - changeSince < 1f) now = inSpace;
            }
            else changeSince = -1f;
            if (now != inSpace)
            {
                changeSince = -1f;
                inSpace = now;
                Log.Write("flight: in space " + now);
                ResetState();
                if (now)
                {
                    Speech.Say(introSaid ? "In space. F10 for status." :
                        "You are in space. F10: ship status. Page Down and Page Up: nearby things. Shift Page Down: category; the last two are the map of this system and of other star systems. Enter: select. F2: fly there. F: dock. F3: follow a map route. End: where is my target. Arrows steer.", false);
                    introSaid = true;
                }
            }
            if (!inSpace) return;
            if (!MenuNav.Active && MenuNav.Focused() && Busy(ship))
            {
                // Docking or jumping: key presses broke it on 10/27 (F3 right at "docked" undocked
                // the ship; Enter re-routed it during "entering the dock"). Hold the mod's keys.
                if (MenuNav.Pressed(MenuNav.VK_RETURN) | MenuNav.Pressed(VK_F2) | MenuNav.Pressed(VK_F3) | MenuNav.Pressed(VK_F) |
                    MenuNav.Pressed(MenuNav.VK_NEXT) | MenuNav.Pressed(MenuNav.VK_PRIOR))
                    Speech.Say(BusyWords(ship));
                if (MenuNav.Pressed(MenuNav.VK_END)) Speech.Say(TargetInfo());
            }
            else if (!MenuNav.Active && MenuNav.Focused())
            {
                bool shift = MenuNav.Down(MenuNav.VK_SHIFT);
                if (MenuNav.Pressed(MenuNav.VK_NEXT, true)) { if (shift) Category(1); else Step(1); }
                else if (MenuNav.Pressed(MenuNav.VK_PRIOR, true)) { if (shift) Category(-1); else Step(-1); }
                else if (MenuNav.Pressed(MenuNav.VK_RETURN)) Select();
                else if (MenuNav.Pressed(MenuNav.VK_END)) Speech.Say(TargetInfo());
                F2Check(ship);
                DockCheck(ship);
                FollowCheck();
                ThrottleCheck(ship);
                if (MenuNav.Pressed(VK_COMMA)) commaAt = Time.unscaledTime + 0.4f;
            }
            if (pendingCatAt > 0 && Time.unscaledTime >= pendingCatAt) { pendingCatAt = -1f; AnnounceCategory(); }
            WatchTarget();
            WatchWaypoint();
            if (grantedAt > 0 && Time.unscaledTime > grantedAt + 3f)
            {
                grantedAt = -1f;
                if (ship.autoPilot == Il2CppUnderspace.pilotSetting.FREEFLIGHT)
                {
                    Log.Write("dock granted but autopilot stayed FREEFLIGHT: manual docking on");
                    Speech.Say("Docking was granted, but the ship does not fly in by itself: manual docking is on. Press L once and wait for Manual docking disabled. Then press F again.");
                }
            }
            WatchAutopilot(ship);
            JumpTick(ship);
            LaneTick(ship);
            DockWatch(ship);
            WatchDamage(ship);
            WatchEnemies(ship);
            SteerSpeech(ship);
        }

        // ---------- docking and jumping in progress ----------

        static bool Busy(Ship ship)
        {
            var p = ship.autoPilot;
            return p == Il2CppUnderspace.pilotSetting.ENTERDOCK || p == Il2CppUnderspace.pilotSetting.FULLDOCK ||
                   p == Il2CppUnderspace.pilotSetting.INJUMP || p == Il2CppUnderspace.pilotSetting.INQUEUE ||
                   p == Il2CppUnderspace.pilotSetting.LEAVEDOCK;
        }

        static string BusyWords(Ship ship) => ship.autoPilot switch
        {
            Il2CppUnderspace.pilotSetting.INJUMP => "Jumping to the next system. Please wait.",
            Il2CppUnderspace.pilotSetting.FULLDOCK => "Docked. The station opens in a moment. Please wait.",
            Il2CppUnderspace.pilotSetting.LEAVEDOCK => "Leaving the dock. Please wait.",
            Il2CppUnderspace.pilotSetting.INQUEUE => "Waiting in line to dock. Please wait.",
            _ => "The ship is flying into the dock by itself. Please wait, keys are paused until it is done."
        };

        // 10/27: entering the dock took 13 s the first time; the second time it sat in ENTERDOCK
        // for a minute after the player re-routed. Say so if it lasts long.
        static float enterDockSince = -1f;
        static bool enterDockLongSaid;
        static void DockWatch(Ship ship)
        {
            if (ship.autoPilot != Il2CppUnderspace.pilotSetting.ENTERDOCK) { enterDockSince = -1f; enterDockLongSaid = false; return; }
            if (enterDockSince < 0) enterDockSince = Time.unscaledTime;
            if (!enterDockLongSaid && Time.unscaledTime - enterDockSince > 45f)
            {
                enterDockLongSaid = true;
                Log.Write($"ENTERDOCK for 45 s, target {SolarName(target)} {(target != null ? Mathf.RoundToInt(Meters(target)) + "m" : "")}");
                Speech.Say("Docking is taking long. Press F1 to cancel the autopilot, then choose the station again and press F2.");
            }
        }

        // ---------- flying through a jumpgate ----------
        // 10/27: the game's Goto stops 1.5 to 2.5 km before a gate, and the gate only accepts a dock
        // request inside its dockDistance (1500 m), so F did nothing and the ship drifted past.
        // F2 or F on a gate now runs this: Goto while far, then the mod aims at the gate and holds the
        // throttle until it is close, asks the gate to dock (that starts the jump, seen 10/25: INJUMP),
        // and if that does not start, keeps flying straight into the middle of the gate.
        static BaseSolar jumpGate;
        static int jumpStage; // 0 off, 1 game autopilot flying, 2 dock asked, 3 mod flying in
        static float jumpStageAt, jumpStartedAt, jumpClosest;
        static bool autoAim, jumpDockTried;
        static float jumpAskedAt = float.MaxValue;

        static bool IsGate(BaseSolar s) => s != null && (s.currentCategory == Il2CppUnderspace.solarType.Jumpgate ||
                                                         s.currentCategory == Il2CppUnderspace.solarType.Jumphole || s.TryCast<JumpPoint>() != null);

        static void StartJump(Ship ship, bool fromF2)
        {
            jumpGate = target; jumpStartedAt = Time.unscaledTime; jumpClosest = float.MaxValue; jumpDockTried = false; jumpAskedAt = float.MaxValue;
            var jp = jumpGate.TryCast<JumpPoint>();
            bool req = false;
            try { req = jumpGate.meetsDockingRequirements(); } catch { }
            Log.Write($"jump start ({(fromF2 ? "F2" : "F")}): {targetName} {Mathf.RoundToInt(Meters(jumpGate))}m, dockDistance {jumpGate.dockDistance}, canDock {jumpGate.canDock}, meets requirements {req}" +
                      (jp != null ? $", jumpDistance {jp.jumpDistance}, extents {jp.jumpExtents}, to {jp.jumpSystemID}, jumpState {jp.jumpState}" : ", not a JumpPoint") + $", autopilot {ship.autoPilot}");
            SetJumpStage(1);
            if (!fromF2 && ship.autoPilot == Il2CppUnderspace.pilotSetting.FREEFLIGHT && Meters(jumpGate) > jumpGate.dockDistance * 0.9f)
                try { ship.enterGoto(jumpGate); } catch (Exception e) { Log.Write("jump enterGoto failed: " + e.Message); }
            Speech.Say($"Flying into {targetName}. The jump starts by itself. Arrow keys or F1 stop it.");
        }

        static void SetJumpStage(int s)
        {
            if (s != jumpStage) Log.Write($"jump stage {jumpStage} -> {s}, {(jumpGate != null ? Mathf.RoundToInt(Meters(jumpGate)) + "m" : "")}, autopilot {Ship.PlayerShip.autoPilot}");
            jumpStage = s; jumpStageAt = Time.unscaledTime;
            autoAim = s == 3;
        }

        static void StopJump(string why, bool say = true)
        {
            Log.Write($"jump stopped: {why}, closest {Mathf.RoundToInt(jumpClosest)}m");
            jumpStage = 0; jumpGate = null; autoAim = false;
            if (say) Speech.Say(why);
        }

        static void JumpTick(Ship ship)
        {
            if (jumpStage == 0) return;
            var p = ship.autoPilot;
            if (p == Il2CppUnderspace.pilotSetting.INJUMP) { StopJump("jump started", false); return; }
            if (jumpGate == null || !jumpGate.gameObject.activeInHierarchy || !Same(target, jumpGate)) { StopJump("jump: target changed", false); return; }
            if (MenuNav.Focused() && !MenuNav.Active && (MenuNav.Down(MenuNav.VK_LEFT) || MenuNav.Down(MenuNav.VK_RIGHT) || MenuNav.Down(MenuNav.VK_UP) || MenuNav.Down(MenuNav.VK_DOWN) || MenuNav.Down(VK_F1)))
            { StopJump("Stopped flying into the gate. You are flying."); return; }
            float d = Meters(jumpGate);
            jumpClosest = Mathf.Min(jumpClosest, d);
            if (Time.unscaledTime - jumpStartedAt > 150f) { StopJump($"Could not get through {SolarName(jumpGate)}. It is {Distance(d)}, {Direction(jumpGate)}. Press F2 to try again."); return; }
            float since = Time.unscaledTime - jumpStageAt;
            bool free = p == Il2CppUnderspace.pilotSetting.FREEFLIGHT;
            switch (jumpStage)
            {
                case 1: // the game's autopilot flies; when it lets go, close enough to ask, or the mod flies on
                    if (!free || since < 1.2f) return;
                    if (d <= jumpGate.dockDistance * 0.9f && !jumpDockTried) AskGate(ship);
                    else SetJumpStage(3);
                    break;
                case 2: // dock asked: the game's autopilot should take over and jump
                    if (!free) { jumpStageAt = Time.unscaledTime; return; }
                    if (since > 3f) { Log.Write("jump: dock request did not start the autopilot, flying in"); SetJumpStage(3); }
                    break;
                case 3: // the mod aims at the gate and holds full throttle
                    if (!free) { SetJumpStage(2); return; }
                    try { ship.input.throttle = 1f; } catch { }
                    // ask again each time the ship got another 400 m closer
                    if ((!jumpDockTried && d <= jumpGate.dockDistance * 0.9f) || d < jumpAskedAt - 400f) AskGate(ship);
                    break;
            }
        }

        static void AskGate(Ship ship)
        {
            jumpDockTried = true; jumpAskedAt = Meters(jumpGate);
            Log.Write($"jump: asking gate to dock at {Mathf.RoundToInt(Meters(jumpGate))}m");
            try { ship.enterDock(true, jumpGate); } catch (Exception e) { Log.Write("jump enterDock failed: " + e.Message); }
            SetJumpStage(2);
        }

        // ---------- flying through a laneline ring ----------
        // 11/3: campaign "Dock with the laneline, fly through the bottom ring": F did nothing at
        // 900-1027 m (log), the game's Goto stops about 1 km out. SDK SolarLaneLinePortal: the
        // player enters when the ship touches the portal (OnTriggerEnter) and canEnterLane() is
        // true (in a campaign only the allowed ring and direction). enterPortal(ship, 0) does the
        // same as touching it. The mod flies to the right ring, like a sighted player steering in,
        // and touches it for the player only once very close.
        static SolarLaneLine lane;
        static SolarLaneLinePortal lanePortal;
        static float laneStartedAt, laneNextSay, laneClosest, laneTriedAt = float.MaxValue;
        static bool laneEnterTried;
        internal static Vector3? AimPoint;

        static bool IsLane(BaseSolar s) => s != null && s.TryCast<SolarLaneLine>() != null;

        static string PortalInfo(SolarLaneLinePortal p)
        {
            if (p == null) return "none";
            bool can = false; try { can = p.canEnterLane(); } catch { }
            return $"dir {p.myDirection}, {Mathf.RoundToInt(Vector3.Distance(Ship.PlayerShip.transform.position, p.transform.position))}m, canEnter {can}, active {p.isActive}";
        }

        static void StartLane(Ship ship)
        {
            lane = target.TryCast<SolarLaneLine>();
            var a = lane.nextPortal; var b = lane.previousPortal;
            Log.Write($"lane start: {targetName}, next portal {PortalInfo(a)}, previous portal {PortalInfo(b)}, disrupted {lane.isDisrupted}, autopilot {ship.autoPilot}");
            bool Can(SolarLaneLinePortal p) { try { return p != null && p.canEnterLane(); } catch { return false; } }
            float D(SolarLaneLinePortal p) => p == null ? float.MaxValue : Vector3.Distance(ship.transform.position, p.transform.position);
            if (Can(a) && Can(b)) lanePortal = D(a) <= D(b) ? a : b;
            else lanePortal = Can(a) ? a : Can(b) ? b : null;
            if (lanePortal == null)
            {
                lane = null;
                Speech.Say($"{targetName} cannot be entered from here. Choose the next waypoint with Semicolon.");
                return;
            }
            laneStartedAt = Time.unscaledTime; laneNextSay = Time.unscaledTime + 6f; laneClosest = float.MaxValue; laneEnterTried = false; laneTriedAt = float.MaxValue;
            Speech.Say($"Flying into the laneline ring, {Distance(D(lanePortal))}. Hands off: it starts by itself. F1 or the arrow keys stop it.");
        }

        static void StopLane(string why, bool say = true)
        {
            Log.Write($"lane stopped: {why}, closest {Mathf.RoundToInt(laneClosest)}m");
            lane = null; lanePortal = null; AimPoint = null;
            if (say) Speech.Say(why);
        }

        static void LaneTick(Ship ship)
        {
            if (lane == null) return;
            var p = ship.autoPilot;
            if (p == Il2CppUnderspace.pilotSetting.GOTO) { AimPoint = null; return; } // the game's own autopilot brings the ship closer first
            if (p != Il2CppUnderspace.pilotSetting.FREEFLIGHT)
            {
                // Touching the ring sets DOCK, then the laneline carries the ship.
                StopLane("In the laneline. The ship travels by itself now. Please wait.");
                return;
            }
            if (lanePortal == null || !lanePortal.gameObject.activeInHierarchy) { StopLane("lane: portal gone", false); return; }
            if (MenuNav.Focused() && !MenuNav.Active && (MenuNav.Down(MenuNav.VK_LEFT) || MenuNav.Down(MenuNav.VK_RIGHT) || MenuNav.Down(MenuNav.VK_UP) || MenuNav.Down(MenuNav.VK_DOWN) || MenuNav.Down(VK_F1)))
            { StopLane("Stopped flying into the laneline. You are flying."); return; }
            Vector3 point = lanePortal.transform.position;
            float d = Vector3.Distance(ship.transform.position, point);
            laneClosest = Mathf.Min(laneClosest, d);
            if (Time.unscaledTime - laneStartedAt > 120f) { StopLane($"Could not get into the laneline. It is {Distance(d)}, {Direction(lane)}. Press F2 to try again."); return; }
            AimPoint = point;
            try { ship.input.throttle = d < 400f ? 0.6f : 1f; } catch { }
            if (Time.unscaledTime > laneNextSay) { laneNextSay = Time.unscaledTime + 6f; Speech.Say(Distance(d), false); }
            // Close to the ring: enter it the same way touching it does (once, then again every 50 m closer).
            if (d < 250f && (!laneEnterTried || d < laneTriedAt - 50f))
            {
                laneEnterTried = true; laneTriedAt = d;
                Log.Write($"lane: enterPortal at {Mathf.RoundToInt(d)}m, {PortalInfo(lanePortal)}");
                try { lanePortal.enterPortal(ship, 0); } catch (Exception e) { Log.Write("lane enterPortal failed: " + e.Message); }
            }
        }

        static void ResetState()
        {
            lane = null; lanePortal = null; AimPoint = null;
            jumpStage = 0; jumpGate = null; autoAim = false;
            entries.Clear(); index = -1; listed = null; target = null; targetName = ""; targetWasAlive = false;
            lastPilot = -1; lastCruise = false; hullStep = shieldStep = 4; knownEnemies.Clear();
            steerYaw = steerPitch = 0; homing = false;
        }

        // ---------- contact list and map ----------

        // Categories 0-5 are the game's own contact window filters (only what the scanner sees
        // nearby). 6 and 7 are the map: every known place in this system, and the known systems.
        static readonly string[] allCats = { "Important", "Ships", "Solars", "Wreckage", "Loot", "All", "Map, places in this system", "Map, ways to other star systems", "Map, all star systems A to Z" };
        static int cat;
        static float pendingCatAt = -1f;

        class Entry { public BaseSolar solar; public ContactButton button; public SolarSystemRecord system; public string gateTo; }
        static List<Entry> entries = new();
        static Entry listed;

        static void Collect()
        {
            if (cat == 6) { CollectPlaces(); return; }
            if (cat == 7) { CollectSystems(); return; }
            if (cat == 8) { CollectAllSystems(); return; }
            entries.Clear();
            var ch = ContactHUD.mainContactHUD;
            if (ch == null) return;
            var area = ch.contentArea;
            if (area != null)
                for (int i = 0; i < area.childCount; i++)
                {
                    var b = area.GetChild(i).GetComponent<ContactButton>();
                    if (b == null || !b.gameObject.activeInHierarchy || b.isGhost || b.linkedSolar == null) continue;
                    entries.Add(new Entry { solar = b.linkedSolar, button = b });
                }
            if (entries.Count == 0 && ch.allDisplayedSolars != null)
                foreach (var s in ch.allDisplayedSolars) if (s != null) entries.Add(new Entry { solar = s });
            SortByDistance();
            // Waypoints first: they are where quests, jobs and routes want you to go.
            entries.Sort((x, y) => WaypointRank(y.solar).CompareTo(WaypointRank(x.solar)));
            LogEntries(ch.currentCategory.ToString());
        }

        static readonly HashSet<Il2CppUnderspace.solarType> placeKinds = new()
        {
            Il2CppUnderspace.solarType.SpaceStation, Il2CppUnderspace.solarType.Planet, Il2CppUnderspace.solarType.Jumpgate,
            Il2CppUnderspace.solarType.Jumphole, Il2CppUnderspace.solarType.LaneLine, Il2CppUnderspace.solarType.DockingRing,
            Il2CppUnderspace.solarType.BattleshipStationary, Il2CppUnderspace.solarType.Wreck, Il2CppUnderspace.solarType.Sun
        };

        // Places the map shows: stations, planets, gates, lanelines... that are on the map
        // (mapIcon set or mapped from the start). Lanelines are many rings: one per name.
        static void CollectPlaces()
        {
            entries.Clear();
            var me = Ship.PlayerShip.transform.position;
            var seen = new Dictionary<string, Entry>();
            int all = 0;
            foreach (var s in UnityEngine.Object.FindObjectsOfType<BaseSolar>())
            {
                if (s == null || !placeKinds.Contains(s.currentCategory)) continue;
                all++;
                string key = SolarName(s) + "|" + s.currentCategory;
                if (seen.TryGetValue(key, out var old) && Vector3.Distance(me, old.solar.transform.position) <= Vector3.Distance(me, s.transform.position)) continue;
                seen[key] = new Entry { solar = s };
            }
            entries.AddRange(seen.Values);
            SortByDistance();
            // Stations and planets first, then gates, then lanelines and the rest (stable: distance kept).
            var ranked = new List<Entry>();
            for (int r = 0; r < 3; r++) foreach (var e in entries) if (PlaceRank(e.solar) == r) ranked.Add(e);
            entries = ranked;
            Log.Write($"map places: {entries.Count} of {all} places");
            LogEntries("map");
        }

        static int PlaceRank(BaseSolar s)
        {
            var c = s.currentCategory;
            if (c == Il2CppUnderspace.solarType.SpaceStation || c == Il2CppUnderspace.solarType.Planet) return 0;
            if (c == Il2CppUnderspace.solarType.Jumpgate || c == Il2CppUnderspace.solarType.Jumphole) return 1;
            return 2;
        }

        // The ways out of this system: every jumpgate and jumphole here, with the system it leads
        // to (JumpPoint.jumpSystemID). 10/25: the game's own system route (getPathToSystem) said
        // "No path available" for every system, because the player has not discovered any
        // connections yet; the gates themselves still know where they go.
        static void CollectSystems()
        {
            entries.Clear();
            var names = SystemNames();
            var me = Ship.PlayerShip.transform.position;
            foreach (var j in UnityEngine.Object.FindObjectsOfType<JumpPoint>())
            {
                if (j == null || !j.gameObject.activeInHierarchy) continue;
                string id = j.jumpSystemID ?? "";
                names.TryGetValue(id, out var r);
                entries.Add(new Entry { solar = j, system = r, gateTo = r != null ? MenuNav.StripTags(r.systemName) : (id.Length > 0 ? id : "an unknown system") });
            }
            entries.Sort((x, y) => Vector3.Distance(me, x.solar.transform.position).CompareTo(Vector3.Distance(me, y.solar.transform.position)));
            var log = new List<string>();
            foreach (var e in entries) log.Add($"{SolarName(e.solar)} -> {e.gateTo} (id {((JumpPoint)e.solar.TryCast<JumpPoint>())?.jumpSystemID}) {Mathf.RoundToInt(Vector3.Distance(me, e.solar.transform.position))}m");
            Log.Write($"map systems: here {(CurrentSystem() != null ? CurrentSystem().systemName : "?")}, {entries.Count} ways out | " + string.Join(" | ", log));
        }

        // All named star systems of the galaxy map, A to Z. The game plans a route only through
        // gates the player has already used, so far ones answer "No path available" at first.
        static void CollectAllSystems()
        {
            entries.Clear();
            var here = CurrentSystem();
            var seen = new HashSet<string>();
            var map = SystemMap.mainMap;
            if (map != null && map.knownSystems != null)
                foreach (var r in map.knownSystems)
                {
                    if (r == null) continue;
                    string n = MenuNav.StripTags(r.systemName ?? "").Trim();
                    if (n.Length == 0 || n == "Unknown" || (here != null && r.linkedSolarSystemID == here.linkedSolarSystemID) || !seen.Add(n)) continue;
                    entries.Add(new Entry { system = r });
                }
            entries.Sort((x, y) => string.Compare(x.system.systemName, y.system.systemName, StringComparison.OrdinalIgnoreCase));
            int conns = map != null && map.knownConnections != null ? map.knownConnections.Count : -1;
            var ids = new List<string>();
            if (map != null && map.knownConnections != null) for (int i = 0; i < map.knownConnections.Count && i < 20; i++) ids.Add(map.knownConnections[i].connectionID);
            Log.Write($"map all systems: {entries.Count}; known connections {conns}: {string.Join(", ", ids)}");
        }

        static Dictionary<string, SolarSystemRecord> SystemNames()
        {
            var d = new Dictionary<string, SolarSystemRecord>();
            var map = SystemMap.mainMap;
            if (map != null && map.knownSystems != null)
                foreach (var r in map.knownSystems)
                {
                    if (r == null) continue;
                    if (!string.IsNullOrEmpty(r.linkedSolarSystemID)) d[r.linkedSolarSystemID] = r;
                    if (!string.IsNullOrEmpty(r.systemAbbreviation) && !d.ContainsKey(r.systemAbbreviation)) d[r.systemAbbreviation] = r;
                }
            return d;
        }

        static SolarSystemRecord CurrentSystem()
        {
            var sys = UnityEngine.Object.FindObjectOfType<SolarSystem>();
            return sys != null ? sys.linkedRecord : null;
        }

        static void SortByDistance()
        {
            var me = Ship.PlayerShip.transform.position;
            entries.Sort((x, y) => Vector3.Distance(me, x.solar.transform.position).CompareTo(Vector3.Distance(me, y.solar.transform.position)));
        }

        static void LogEntries(string what)
        {
            var me = Ship.PlayerShip.transform.position;
            var names = new List<string>();
            foreach (var e in entries) if (e.solar != null) names.Add($"{SolarName(e.solar)} {Mathf.RoundToInt(Vector3.Distance(me, e.solar.transform.position))}m {e.solar.currentCategory} {Reaction(e.solar)}");
            Log.Write($"contacts {what}: {entries.Count} | {string.Join(" | ", names)}");
        }

        static int WaypointRank(BaseSolar s) => WaypointTag(s).Length > 0 ? 1 : 0;

        static string WaypointTag(BaseSolar s)
        {
            var ch = ContactHUD.mainContactHUD;
            if (ch == null || s == null) return "";
            if (Same(ch.currentQuestWaypoint, s)) return "quest waypoint";
            if (Same(ch.currentMissionWaypoint, s)) return "mission waypoint";
            if (Same(ch.currentWaypoint, s)) return "waypoint";
            return "";
        }

        static bool Same(BaseSolar a, BaseSolar b) => a != null && b != null && a.GetInstanceID() == b.GetInstanceID();

        static void Category(int dir)
        {
            cat = (cat + dir + allCats.Length) % allCats.Length;
            index = -1; listed = null;
            if (cat < 6)
            {
                var ch = ContactHUD.mainContactHUD;
                if (ch == null) { Speech.Say("Contact list not available."); return; }
                try
                {
                    switch (cat)
                    {
                        case 0: ch.switchToImportant(); break;
                        case 1: ch.switchToShips(); break;
                        case 2: ch.switchToSolars(); break;
                        case 3: ch.switchToWrecks(); break;
                        case 4: ch.switchToCargo(); break;
                        default: ch.switchToAll(); break;
                    }
                }
                catch (Exception e) { Log.Write("category switch failed: " + e.Message); }
                // The game refills its window a moment later (10/25: the first read was the old list).
                pendingCatAt = Time.unscaledTime + 0.35f;
                return;
            }
            AnnounceCategory();
        }

        static void AnnounceCategory()
        {
            Collect();
            string head = allCats[cat] + ", " + entries.Count + ".";
            if (cat == 7) head = $"Ways to other star systems, {entries.Count}.";
            if (cat == 8) head = $"All star systems, A to Z, {entries.Count}. Enter asks the game for a route.";
            if (entries.Count == 0) { Speech.Say(head + (cat < 6 ? " Nothing near you. The map categories list everything: Shift Page Down." : (cat == 7 ? " No jumpgate or jumphole in this system." : " None known yet."))); return; }
            index = 0; listed = entries[0];
            Speech.Say(head + " " + Describe(listed) + " 1 of " + entries.Count + ".");
        }

        static void Step(int dir)
        {
            Collect();
            if (entries.Count == 0) { Speech.Say("Nothing in this category. Shift Page Down changes the category."); return; }
            // Keep the place in the list on the same object even though the list re-sorts.
            int at = -1;
            if (index >= 0 && listed != null)
                for (int i = 0; i < entries.Count; i++)
                    if ((listed.solar != null && Same(entries[i].solar, listed.solar)) || (listed.solar == null && listed.system != null && entries[i].system != null && entries[i].system.systemName == listed.system.systemName)) at = i;
            index = Mathf.Clamp((at < 0 ? (dir > 0 ? -1 : 0) : at) + dir, 0, entries.Count - 1);
            listed = entries[index];
            Speech.Say(Describe(listed) + $" {index + 1} of {entries.Count}.");
        }

        static string Describe(Entry e)
        {
            if (e.gateTo != null)
                return $"To {e.gateTo}, through {SolarName(e.solar)}, {Distance(e.solar)}, {Direction(e.solar)}.";
            if (e.system != null)
            {
                string d = MenuNav.StripTags(e.system.systemDescription ?? "");
                return MenuNav.StripTags(e.system.systemName) + (d.Length > 0 ? ". " + (d.Length > 200 ? d.Substring(0, 200) + "..." : d) : "") + ".";
            }
            return Describe(e.solar);
        }

        static void Select()
        {
            if (listed == null) { Speech.Say("Choose something first with Page Down."); return; }
            if (listed.gateTo != null) { RouteToPlace(listed.solar); Speech.Say($"This gate leads to {listed.gateTo}. Press F2: the ship flies there and through the gate, and you jump.", false); return; }
            if (listed.system != null) { RouteToSystem(listed.system); return; }
            var s = listed.solar;
            if (s == null) { Speech.Say("That is gone."); return; }
            if (cat == 6) { RouteToPlace(s); return; }
            try { if (listed.button != null) listed.button.activateButton(); } catch (Exception e) { Log.Write("activateButton failed: " + e.Message); }
            Target(s);
            bool hostile = s.getReactionToPlayer() == Il2CppUnderspace.SolarReaction.Hostile;
            Speech.Say($"Target: {SolarName(s)}. " + (hostile ? "Enemy. Hold Period to aim and fire." : IsGate(s) ? "F2 flies you through it to the next system." : IsLane(s) ? "Laneline. F2 flies you into its ring." : CanDock(s) ? "F2 flies you there and docks." : "Press F2 to fly there."));
        }

        static void Target(BaseSolar s)
        {
            var tu = TargetingUI.mainTargetingUI;
            if (tu != null && !Same(tu.targetedSolar, s))
                try { tu.openTarget(s); } catch (Exception e) { Log.Write("openTarget failed: " + e.Message); }
            Log.Write($"select {SolarName(s)}: targeted {tu != null && Same(tu.targetedSolar, s)}, ship target {SolarName(Ship.PlayerShip.currentTarget)}");
            SetTarget(s);
        }

        // The map's "best path" button: the game sets waypoints along lanelines; F3 (auto-follow)
        // lets the autopilot fly the whole route.
        static void RouteToPlace(BaseSolar s)
        {
            var wm = WaypointManager.mainManager;
            try { if (wm != null) wm.getBestPath(s, true, false); } catch (Exception e) { Log.Write("getBestPath failed: " + e.Message); }
            Target(s);
            var ch = ContactHUD.mainContactHUD;
            Log.Write($"route to {SolarName(s)}: waypoints {(wm != null && wm.wayPointList != null ? wm.wayPointList.Count : -1)}, current waypoint {SolarName(ch != null ? ch.currentWaypoint : null)}");
            Speech.Say($"Route set to {SolarName(s)}, {Distance(s)}. Press F3: the autopilot follows the route, using lanelines. Or F2: fly straight there." + (IsGate(s) ? "" : CanDock(s) ? " When you arrive, F2 docks." : ""));
        }

        static void RouteToSystem(SolarSystemRecord r)
        {
            var wm = WaypointManager.mainManager;
            var here = CurrentSystem();
            try { if (wm != null && here != null) wm.getPathToSystem(r, here); } catch (Exception e) { Log.Write("getPathToSystem failed: " + e.Message); }
            var gate = wm != null ? wm.currentWantedGate : null;
            Log.Write($"route to system {r.systemName}: gate {SolarName(gate)}, connections {(wm != null && wm.outOfSystemConnections != null ? wm.outOfSystemConnections.Count : -1)}");
            if (gate != null)
            {
                RouteToPlace(gate);
                Speech.Say($"This is the way to {MenuNav.StripTags(r.systemName)}. At the gate press F to jump through.", false);
            }
            else
            {
                noPathFor = MenuNav.StripTags(r.systemName);
                noPathUntil = Time.unscaledTime + 1.5f;
                Speech.Say($"Asking for a route to {noPathFor}.");
            }
        }

        static void SetTarget(BaseSolar s)
        {
            target = s; targetName = SolarName(s); targetWasAlive = s != null;
            onTargetSaid = false;
        }

        // The game also picks targets itself (Comma = nearest enemy, a click, a quest): follow it.
        static float commaAt;
        static void WatchTarget()
        {
            var tu = TargetingUI.mainTargetingUI;
            var t = tu != null ? tu.targetedSolar : null;
            // 11/25: a dying gunboat stayed the game's target and was re-picked every frame:
            // "destroyed" said 15 times. A dying or already-announced ship is not picked again.
            if (t != null && (t.isDying || t.GetInstanceID() == lastGoneId)) t = null;
            if (t != null && !Same(t, target))
            {
                SetTarget(t);
                Log.Write("game target changed: " + SolarName(t) + (Time.unscaledTime < commaAt + 1.5f ? " (after Comma)" : ""));
                Speech.Say("Target: " + Describe(t), false);
            }
            if (target != null && (target.isDying || !target.gameObject.activeInHierarchy))
            {
                if (targetWasAlive)
                {
                    bool wasEnemy = knownEnemies.Contains(target.GetInstanceID());
                    lastGoneId = target.GetInstanceID();
                    Speech.Say(targetName + (wasEnemy ? (cargoHintSaid ? " destroyed." : " destroyed. If it dropped cargo, press B to collect it.") : " is gone."));
                    if (wasEnemy) cargoHintSaid = true;
                    Log.Write("target gone: " + targetName);
                }
                target = null; targetWasAlive = false;
            }
        }

        static int lastGoneId;
        static bool cargoHintSaid;

        // ---------- describing things ----------

        internal static string SolarName(BaseSolar s)
        {
            if (s == null) return "none";
            string n = MenuNav.StripTags(s.itemName ?? "").Trim();
            return n.Length > 0 ? n : s.gameObject.name.Replace("(Clone)", "").Trim();
        }

        static string Reaction(BaseSolar s)
        {
            try { return s.getReactionToPlayer().ToString(); } catch { return "?"; }
        }

        static string Kind(BaseSolar s)
        {
            switch (s.currentCategory)
            {
                case Il2CppUnderspace.solarType.SpaceStation: return "station";
                case Il2CppUnderspace.solarType.Planet: return "planet";
                case Il2CppUnderspace.solarType.LaneLine: return "laneline, a fast travel lane";
                case Il2CppUnderspace.solarType.Jumpgate: return "jumpgate to another system";
                case Il2CppUnderspace.solarType.Jumphole: return "jumphole to another system";
                case Il2CppUnderspace.solarType.DockingRing: return "docking ring";
                case Il2CppUnderspace.solarType.Cargo: return "loot";
                case Il2CppUnderspace.solarType.Wreck: case Il2CppUnderspace.solarType.ShipWreck: return "wreck";
                case Il2CppUnderspace.solarType.Asteroid: return "asteroid";
                case Il2CppUnderspace.solarType.Sun: return "sun";
                case Il2CppUnderspace.solarType.Waypoint: return "waypoint";
                case Il2CppUnderspace.solarType.LightFighter: return "light fighter";
                case Il2CppUnderspace.solarType.MediumFighter: return "medium fighter";
                case Il2CppUnderspace.solarType.HeavyFighter: return "heavy fighter";
                case Il2CppUnderspace.solarType.Freighter: return "freighter";
                case Il2CppUnderspace.solarType.Transport: return "transport";
                case Il2CppUnderspace.solarType.Cruiser: return "cruiser";
                case Il2CppUnderspace.solarType.Battleship: case Il2CppUnderspace.solarType.BattleshipStationary: return "battleship";
                case Il2CppUnderspace.solarType.WeaponPlatform: return "weapon platform";
                case Il2CppUnderspace.solarType.Alien: return "creature";
                case Il2CppUnderspace.solarType.AlienBoss: case Il2CppUnderspace.solarType.AlienBossShip: return "boss";
                default: return "";
            }
        }

        static string Describe(BaseSolar s)
        {
            var parts = new List<string> { SolarName(s) };
            string tag = WaypointTag(s);
            if (tag.Length > 0) parts.Add(tag);
            string kind = Kind(s);
            if (kind.Length > 0) parts.Add(kind);
            var r = s.getReactionToPlayer();
            if (r == Il2CppUnderspace.SolarReaction.Hostile) parts.Add("ENEMY");
            else if (r == Il2CppUnderspace.SolarReaction.Friendly) parts.Add("friendly");
            parts.Add(Distance(s) + ", " + Direction(s));
            return string.Join(", ", parts) + ".";
        }

        internal static float Meters(BaseSolar s) => Vector3.Distance(Ship.PlayerShip.transform.position, s.transform.position);

        internal static string Distance(float m)
        {
            if (m < 1000f) return Mathf.RoundToInt(m) + " meters";
            return (m / 1000f).ToString(m < 10000f ? "0.0" : "0") + " kilometers";
        }
        internal static string Distance(BaseSolar s) => Distance(Meters(s));

        // Angles of a point from the ship's nose: h positive = right, v positive = up.
        internal static void Angles(Vector3 point, out float h, out float v)
        {
            var t = Ship.PlayerShip.transform;
            var l = t.InverseTransformDirection(point - t.position);
            h = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            v = Mathf.Atan2(l.y, new Vector2(l.x, l.z).magnitude) * Mathf.Rad2Deg;
        }

        internal static string Direction(BaseSolar s)
        {
            Angles(s.transform.position, out float h, out float v);
            float m = Mathf.Abs(h);
            if (m < 8 && Mathf.Abs(v) < 8) return "dead ahead";
            string side = h > 0 ? "right" : "left";
            string d = m < 20 ? "ahead" : m < 60 ? "ahead " + side : m < 120 ? "to your " + side : m < 160 ? "behind, " + side : "behind you";
            if (v > 25) d += ", above";
            else if (v < -25) d += ", below";
            return d;
        }

        static bool CanDock(BaseSolar s)
        {
            if (s == null) return false;
            var c = s.currentCategory;
            return s.canDock || c == Il2CppUnderspace.solarType.LaneLine || c == Il2CppUnderspace.solarType.Jumpgate || c == Il2CppUnderspace.solarType.Jumphole || c == Il2CppUnderspace.solarType.DockingRing;
        }

        static string TargetInfo()
        {
            if (target == null) return "No target. Page Down to choose something, Enter to select it.";
            Angles(target.transform.position, out float h, out float v);
            string s = $"{targetName}, {Distance(target)}, {Direction(target)}. {Mathf.RoundToInt(Mathf.Abs(h))} degrees {(h > 0 ? "right" : "left")}, {Mathf.RoundToInt(Mathf.Abs(v))} {(v > 0 ? "up" : "down")}.";
            if (target.canTakeDamage || target.TryCast<Ship>() != null)
            {
                int hull = Mathf.RoundToInt(target.getHealthPercentage() * (target.getHealthPercentage() <= 1.001f ? 100f : 1f));
                s += $" Hull {hull} percent.";
                if (target.hasShield && target.shieldMax > 0) s += $" Shields {Mathf.RoundToInt(100f * target.shieldCurrent / target.shieldMax)} percent.";
            }
            return s;
        }

        // ---------- F10 status ----------

        public static string Status()
        {
            var ship = Ship.PlayerShip;
            if (ship == null) return "";
            var parts = new List<string>();
            parts.Add($"Hull {Pct(ship.currentHealth, ship.itemHealth)} percent, shields {Pct(ship.shieldCurrent, ship.shieldMax)} percent, energy {Pct(ship.powerPlantCurrent, ship.powerPlantMax)} percent");
            float speed = 0f;
            try { speed = ship.physics != null && ship.physics.rbody != null ? ship.physics.rbody.velocity.magnitude : 0f; } catch { }
            parts.Add($"Speed {Mathf.RoundToInt(speed)}, throttle {Mathf.RoundToInt(ship.input.throttle * 100f)} percent" + (ship.isInCruise ? ", high engines on" : ""));
            parts.Add("Autopilot: " + PilotWords(ship.autoPilot));
            parts.Add(target != null ? "Target: " + targetName + ", " + Distance(target) + ", " + Direction(target) : "No target");
            var wm = WaypointManager.mainManager;
            if (wm != null && wm.autoFollow) parts.Add("Following a route");
            if (ManualDocking()) parts.Add("Manual docking is on: press L to switch it off so the autopilot docks for you");
            return string.Join(". ", parts) + ".";
        }

        static int Pct(float cur, float max) => max > 0 ? Mathf.RoundToInt(100f * Mathf.Clamp01(cur / max)) : 0;

        static string PilotWords(Il2CppUnderspace.pilotSetting p) => p switch
        {
            Il2CppUnderspace.pilotSetting.FREEFLIGHT => "off, you are flying",
            Il2CppUnderspace.pilotSetting.GOTO => "flying to the target",
            Il2CppUnderspace.pilotSetting.GOTODOCK => "flying to the target to dock",
            Il2CppUnderspace.pilotSetting.DOCK => "docking",
            Il2CppUnderspace.pilotSetting.ENTERDOCK => "entering the dock",
            Il2CppUnderspace.pilotSetting.FULLDOCK => "docked",
            Il2CppUnderspace.pilotSetting.LEAVEDOCK => "leaving the dock",
            Il2CppUnderspace.pilotSetting.FORMATION => "flying in formation with the target",
            Il2CppUnderspace.pilotSetting.LANELINE => "travelling on a laneline",
            Il2CppUnderspace.pilotSetting.DECELLANELINE => "slowing down at the end of the laneline",
            Il2CppUnderspace.pilotSetting.LEAVINGLANELINE => "leaving the laneline",
            Il2CppUnderspace.pilotSetting.PREEXITLANELINE => "reaching the end of the laneline",
            Il2CppUnderspace.pilotSetting.INJUMP => "jumping to another system",
            Il2CppUnderspace.pilotSetting.INQUEUE => "waiting in line to dock",
            _ => p.ToString()
        };

        // ---------- autopilot, high engines, throttle ----------

        static int lastPilot = -1;
        static bool lastCruise;
        static float pilotChangedAt, gotoStartedAt;
        static void WatchAutopilot(Ship ship)
        {
            int p = (int)ship.autoPilot;
            if (p != lastPilot)
            {
                bool first = lastPilot < 0;
                var before = (Il2CppUnderspace.pilotSetting)Math.Max(0, lastPilot);
                lastPilot = p;
                if (ship.autoPilot == Il2CppUnderspace.pilotSetting.GOTO) gotoStartedAt = Time.unscaledTime;
                pilotChangedAt = Time.unscaledTime;
                Log.Write("autopilot: " + ship.autoPilot + " target " + SolarName(target));
                if (!first)
                {
                    var now = ship.autoPilot;
                    if (jumpStage != 0 && now != Il2CppUnderspace.pilotSetting.INJUMP) return; // the gate helper speaks
                    if (now == Il2CppUnderspace.pilotSetting.ENTERDOCK) { Speech.Say("Entering the dock. This takes about 15 seconds. Please wait, do not press keys."); return; }
                    if (now == Il2CppUnderspace.pilotSetting.FULLDOCK) { Speech.Say("Docked. The station opens in a moment. Please wait."); return; }
                    if (now == Il2CppUnderspace.pilotSetting.INJUMP) { Speech.Say("Jumping through the gate. Please wait."); return; }
                    if (now == Il2CppUnderspace.pilotSetting.FREEFLIGHT)
                    {
                        string where = target != null ? $" {targetName} is {Distance(target)}, {Direction(target)}." : "";
                        bool wasGoto = before == Il2CppUnderspace.pilotSetting.GOTO || before == Il2CppUnderspace.pilotSetting.GOTODOCK;
                        if (wasGoto && Time.unscaledTime - gotoStartedAt < 1.5f && target != null && CanDock(target))
                        {
                            Speech.Say($"You are already close to {targetName}. Press F to dock.");
                            return;
                        }
                        Speech.Say((wasGoto ? "Autopilot stopped." : "Autopilot off, you are flying.") + where +
                                   (wasGoto && target != null && CanDock(target) ? " Press F2 or F to dock." : ""));
                    }
                    else Speech.Say("Autopilot: " + PilotWords(now) + ".");
                }
            }
            if (ship.isInCruise != lastCruise)
            {
                lastCruise = ship.isInCruise;
                Speech.Say(lastCruise ? "High engines on. Guns are off while they run." : "High engines off.", false);
            }
        }

        // Say the throttle when W or S is let go.
        static bool throttleHeld;
        static void ThrottleCheck(Ship ship)
        {
            bool held = MenuNav.Down(VK_W) || MenuNav.Down(VK_S);
            if (held) { throttleHeld = true; return; }
            if (!throttleHeld) return;
            throttleHeld = false;
            if (ship.autoPilot != Il2CppUnderspace.pilotSetting.FREEFLIGHT) return;
            Speech.Say($"Throttle {Mathf.RoundToInt(ship.input.throttle * 100f)} percent.", false);
        }

        // F2 is the game's Goto Target: it toggles (pressed while flying to the target it stops).
        // Only if it was off and the game did nothing does the mod start the same autopilot.
        static float f2CheckAt;
        static bool f2WasFree;
        static void F2Check(Ship ship)
        {
            if (MenuNav.Pressed(VK_F2))
            {
                Log.Write($"F2 pressed: autopilot {ship.autoPilot}, target {SolarName(target)}, ship target {SolarName(ship.currentTarget)}, dock key {DockKey()}");
                if (target == null) { Speech.Say("No target. Choose one with Page Down and Enter first."); return; }
                if (IsGate(target)) { if (jumpStage == 0) StartJump(ship, true); else StopJump("Stopped flying into the gate."); }
                if (IsLane(target)) { if (lane == null) StartLane(ship); else StopLane("Stopped flying into the laneline."); return; }
                f2WasFree = ship.autoPilot == Il2CppUnderspace.pilotSetting.FREEFLIGHT;
                f2CheckAt = Time.unscaledTime + 0.8f;
            }
            if (MenuNav.Pressed(VK_F1)) Log.Write("F1 pressed: autopilot " + ship.autoPilot);
            if (f2CheckAt <= 0 || Time.unscaledTime < f2CheckAt) return;
            f2CheckAt = 0;
            if (!f2WasFree || ship.autoPilot != Il2CppUnderspace.pilotSetting.FREEFLIGHT || target == null) return;
            try { Log.Write("F2 fallback: enterGoto " + targetName); ship.enterGoto(target); }
            catch (Exception e) { Log.Write("F2 fallback failed: " + e.Message); }
        }

        // F is the game's "dock with target" (the game's own key list: /dockwithtarget = F).
        // 10/25: the docking request was granted but the ship never flew in. HYPOTHESIS: manual
        // docking (L) was on, so the game expects the player to fly into the dock.
        static float dockCheckAt;
        static void DockCheck(Ship ship)
        {
            if (MenuNav.Pressed(VK_F))
            {
                Log.Write($"F pressed in space: autopilot {ship.autoPilot}, target {SolarName(target)} {(target != null ? Mathf.RoundToInt(Meters(target)) + "m dockDistance " + target.dockDistance : "")}, manual docking {ManualDocking()}");
                if (target == null) { Speech.Say("No target to dock with. Choose a station, planet, laneline or gate first."); return; }
                if (!CanDock(target)) { Speech.Say(targetName + " cannot be docked with."); return; }
                if (IsGate(target)) { if (jumpStage == 0) StartJump(ship, false); return; }
                if (IsLane(target)) { if (lane == null) StartLane(ship); return; }
                dockCheckAt = Time.unscaledTime + 2f;
            }
            if (MenuNav.Pressed(VK_L)) Speech.Say("You pressed L: manual docking switches on or off. Off means the autopilot docks for you.", false);
            if (dockCheckAt <= 0 || Time.unscaledTime < dockCheckAt) return;
            dockCheckAt = 0;
            Log.Write($"after F: autopilot {ship.autoPilot}, manual docking {ManualDocking()}");
            if (ship.autoPilot != Il2CppUnderspace.pilotSetting.FREEFLIGHT || target == null) return;
            float d = Meters(target);
            if (d > Mathf.Max(target.dockDistance, 2500f)) Speech.Say($"Too far to dock, {Distance(d)}. Press F2 to fly closer, then F again.");
            else if (ManualDocking()) Speech.Say("Manual docking is on, so the game wants you to fly into the dock yourself. Press L once to switch manual docking off, then press F again.");
            else Speech.Say("Nothing happened yet. Wait a moment, or press F again.");
        }

        // The game shows a manual docking marker while manual docking is on (StatsHUD.manualDockingIndicator),
        // and says "Manual docking enabled/disabled" when L is pressed (seen 10/25).
        public static bool manualDockingSaidOn;
        static bool ManualDocking()
        {
            var hud = StatsHUD.mainHUD;
            bool marker = hud != null && hud.manualDockingIndicator != null && hud.manualDockingIndicator.activeInHierarchy;
            return manualDockingSaidOn; // marker was active while autopilot docking worked (10/25 7:14)
        }

        static string noPathFor;
        static float noPathUntil, grantedAt = -1f;

        public static void GameMessage(string text)
        {
            if (text.Contains("No path available") && Time.unscaledTime < noPathUntil)
            {
                Speech.Say($"No known route to {noPathFor} yet. The game only plans routes through gates you have already flown through. Use Ways to other star systems and go gate by gate; the system descriptions say what is there.", false);
                noPathUntil = 0;
            }
            if (text.Contains("Dock Request Granted")) grantedAt = Time.unscaledTime;
            if (text.Contains("Manual docking enabled")) manualDockingSaidOn = true;
            else if (text.Contains("Manual docking disabled")) manualDockingSaidOn = false;
        }

        // F3 is the game's auto-follow: the autopilot follows the waypoint route (the map's best path).
        static float f3CheckAt;
        static bool f3Before, lastFollow;
        static void FollowCheck()
        {
            var wm = WaypointManager.mainManager;
            if (wm == null) return;
            if (MenuNav.Pressed(VK_F3))
            {
                f3Before = wm.autoFollow;
                f3CheckAt = Time.unscaledTime + 0.8f;
                Log.Write($"F3 pressed: autoFollow {wm.autoFollow}, state {wm.autoFollowState}, waypoints {(wm.wayPointList != null ? wm.wayPointList.Count : -1)}");
            }
            if (f3CheckAt > 0 && Time.unscaledTime >= f3CheckAt)
            {
                f3CheckAt = 0;
                if (wm.autoFollow == f3Before)
                {
                    Log.Write("F3 fallback: toggleAutoFollower");
                    try { wm.toggleAutoFollower(); } catch (Exception e) { Log.Write("toggleAutoFollower failed: " + e.Message); }
                }
            }
            if (wm.autoFollow != lastFollow)
            {
                lastFollow = wm.autoFollow;
                Log.Write("autoFollow " + lastFollow);
                Speech.Say(lastFollow ? "Following the route. The autopilot flies from waypoint to waypoint." : "Route following off.", false);
            }
        }

        // Says the next waypoint of a route (from the map, a quest or a job) when it changes.
        static int lastWaypoint;
        static void WatchWaypoint()
        {
            var ch = ContactHUD.mainContactHUD;
            var w = ch != null ? (ch.currentWaypoint ?? ch.currentMissionWaypoint ?? ch.currentQuestWaypoint) : null;
            int id = w != null ? w.GetInstanceID() : 0;
            if (id == lastWaypoint) return;
            lastWaypoint = id;
            if (w == null) return;
            Log.Write("waypoint: " + SolarName(w) + " " + Mathf.RoundToInt(Meters(w)) + "m");
            Speech.Say($"Next waypoint: {SolarName(w)}, {Distance(w)}, {Direction(w)}.", false);
        }

        static string DockKey()
        {
            try
            {
                var kb = OptionsMenu.mainMenu != null ? OptionsMenu.mainMenu.mainKeyboard : null;
                return kb != null ? MenuNav.StripTags(kb.parseKeyboardString("/dockwithtarget")) : "?";
            }
            catch { return "?"; }
        }

        // ---------- damage ----------

        static int hullStep = 4, shieldStep = 4;
        static float repairHintAt;
        static void WatchDamage(Ship ship)
        {
            int hull = Pct(ship.currentHealth, ship.itemHealth), shield = Pct(ship.shieldCurrent, ship.shieldMax);
            int hs = Bucket(hull), ss = Bucket(shield);
            if (ss < shieldStep) Speech.Say(shield <= 0 ? "Shields down! Tap G." : $"Shields {shield} percent.", false);
            if (hs < hullStep)
            {
                LogThreats(ship, hull);
                Speech.Say($"Hull {hull} percent!", false);
                if (hull > 0 && hull < 60 && Time.unscaledTime > repairHintAt) { repairHintAt = Time.unscaledTime + 15f; Speech.Say("Hold G to repair the hull.", false); }
            }
            if (ss == 4 && shieldStep < 4) Speech.Say("Shields full.", false);
            hullStep = hs; shieldStep = ss;
        }

        // Diagnostic (11/25: hull 100 -> 0 in one second in mission 1, cause unknown):
        // what is near the ship when the hull drops.
        static void LogThreats(Ship ship, int hull)
        {
            try
            {
                var me = ship.transform.position;
                var near = new List<(float d, string line)>();
                foreach (var s in UnityEngine.Object.FindObjectsOfType<Ship>())
                {
                    if (s == null || s.isPlayer || !s.gameObject.activeInHierarchy) continue;
                    float d = Vector3.Distance(me, s.transform.position);
                    if (d > 3000f) continue;
                    string r; try { r = s.getReactionToPlayer().ToString(); } catch { r = "?"; }
                    near.Add((d, $"{SolarName(s)} {d:0}m {r}"));
                }
                near.Sort((a, b) => a.d.CompareTo(b.d));
                var parts = new List<string>();
                for (int i = 0; i < near.Count && i < 6; i++) parts.Add(near[i].line);
                float speed = -1f;
                try { speed = ship.physics.rbody.velocity.magnitude; } catch { }
                Log.Write($"damage: hull {hull}, autopilot {ship.autoPilot}, speed {speed:0}, near: {string.Join("; ", parts)}");
            }
            catch (Exception e) { Log.Write("damage log failed: " + e.Message); }
        }

        // 4 = above 75 percent, 3 = above 50, 2 = above 25, 1 = above 0, 0 = empty.
        static int Bucket(int pct) => pct > 75 ? 4 : pct > 50 ? 3 : pct > 25 ? 2 : pct > 0 ? 1 : 0;

        // ---------- enemies ----------

        static readonly HashSet<int> knownEnemies = new();
        static float nextEnemyScan;
        static bool enemyHintSaid;
        static void WatchEnemies(Ship ship)
        {
            if (Time.unscaledTime < nextEnemyScan) return;
            nextEnemyScan = Time.unscaledTime + 1f;
            var me = ship.transform.position;
            var fresh = new List<Ship>();
            foreach (var s in UnityEngine.Object.FindObjectsOfType<Ship>())
            {
                if (s == null || s.isPlayer || s.isDying || !s.gameObject.activeInHierarchy) continue;
                if (Vector3.Distance(me, s.transform.position) > 6000f) continue;
                Il2CppUnderspace.SolarReaction r;
                try { r = s.getReactionToPlayer(); } catch { continue; }
                if (r != Il2CppUnderspace.SolarReaction.Hostile) continue;
                if (knownEnemies.Add(s.GetInstanceID())) fresh.Add(s);
            }
            if (fresh.Count == 0) return;
            fresh.Sort((a, b) => Vector3.Distance(me, a.transform.position).CompareTo(Vector3.Distance(me, b.transform.position)));
            var n = fresh[0];
            string line = fresh.Count == 1 ? $"Enemy: {SolarName(n)}, {Distance(n)}, {Direction(n)}." : $"{fresh.Count} enemies. Nearest {SolarName(n)}, {Distance(n)}, {Direction(n)}.";
            if (!enemyHintSaid) { enemyHintSaid = true; line += " Comma targets the nearest enemy. Hold Period to aim and fire."; }
            if (ship.autoPilot == Il2CppUnderspace.pilotSetting.GOTO || ship.autoPilot == Il2CppUnderspace.pilotSetting.DOCK)
                line += " The autopilot is still flying you: holding Period switches it off so you can fight.";
            Log.Write("enemies: " + line);
            Speech.Say(line);
        }

        // ---------- steering ----------

        // The game steers only with the mouse. The mod turns the ship's body while the arrows
        // (or Home) are held, at a fixed rate, from FixedUpdate so physics keeps the new heading.
        static float steerYaw, steerPitch;
        static bool homing, onTargetSaid, steerLogged, autopilotSteerSaid;

        static void SteerSpeech(Ship ship)
        {
            if (MenuNav.Active || !MenuNav.Focused()) { steerYaw = steerPitch = 0; homing = false; return; }
            bool l = MenuNav.Down(MenuNav.VK_LEFT), r = MenuNav.Down(MenuNav.VK_RIGHT), u = MenuNav.Down(MenuNav.VK_UP), d = MenuNav.Down(MenuNav.VK_DOWN);
            bool home = MenuNav.Down(MenuNav.VK_HOME) || Mission.FireHeld;
            float slow = MenuNav.Down(MenuNav.VK_SHIFT) ? 0.33f : 1f;
            bool wasSteering = steerYaw != 0 || steerPitch != 0 || homing;
            steerYaw = ((r ? 1 : 0) - (l ? 1 : 0)) * slow;
            steerPitch = ((u ? 1 : 0) - (d ? 1 : 0)) * slow;
            homing = home && target != null && steerYaw == 0 && steerPitch == 0;
            if (MenuNav.Down(MenuNav.VK_HOME) && target == null && MenuNav.Pressed(MenuNav.VK_HOME)) Speech.Say("No target to aim at.");

            bool steering = steerYaw != 0 || steerPitch != 0 || homing;
            if (steering && ship.autoPilot != Il2CppUnderspace.pilotSetting.FREEFLIGHT)
            {
                steerYaw = steerPitch = 0; homing = false;
                if (!autopilotSteerSaid && !Mission.FireHeld) { autopilotSteerSaid = true; Speech.Say("The autopilot is flying. Press F1 to take over."); }
                return;
            }
            autopilotSteerSaid = false;

            if (target != null && steering)
            {
                Angles(target.transform.position, out float h, out float v);
                bool on = Mathf.Abs(h) < 3f && Mathf.Abs(v) < 3f;
                if (on && !onTargetSaid) { onTargetSaid = true; Speech.Say("On target."); }
                else if (Mathf.Abs(h) > 8f || Mathf.Abs(v) > 8f) onTargetSaid = false;
            }
            if (wasSteering && !steering && target != null) Speech.Say(TargetInfoShort(), false);
        }

        public static BaseSolar CurrentTarget => target;

        // Semicolon: select the mission waypoint the way Enter selects a contact.
        public static void SelectFromMission(BaseSolar w)
        {
            if (w == null || w == target) return;
            Target(w);
        }

        static string TargetInfoShort()
        {
            Angles(target.transform.position, out float h, out float v);
            if (Mathf.Abs(h) < 3f && Mathf.Abs(v) < 3f) return $"{targetName} dead ahead, {Distance(target)}.";
            return $"{targetName}: {Mathf.RoundToInt(Mathf.Abs(h))} {(h > 0 ? "right" : "left")}, {Mathf.RoundToInt(Mathf.Abs(v))} {(v > 0 ? "up" : "down")}, {Distance(target)}.";
        }

        const float TurnRate = 60f; // degrees per second at full key
        const float AimTurnRate = 90f; // while Home or Period holds the nose on a target (11/25)

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        struct RECT { public int L, T, R, B; }
        struct POINT { public int X, Y; }
        static bool pointerOff, pointerLogged;
        static int lastPx = int.MinValue, lastPy;

        // Puts the real pointer where a sighted player would hold the mouse to turn.
        static void PointerStick(float yaw, float pitch)
        {
            var w = GetForegroundWindow();
            if (!GetClientRect(w, out var r)) return;
            var c = new POINT { X = (r.R - r.L) / 2, Y = (r.B - r.T) / 2 };
            ClientToScreen(w, ref c);
            int reach = (int)((r.B - r.T) * 0.3f);
            int x = c.X + (int)(Mathf.Clamp(yaw, -1f, 1f) * reach), y = c.Y - (int)(Mathf.Clamp(pitch, -1f, 1f) * reach);
            bool off = yaw != 0 || pitch != 0;
            // Centre once when steering stops; leave the pointer alone otherwise (no fight with JAWS).
            if (!off && !pointerOff && lastPx != int.MinValue) return;
            if (x == lastPx && y == lastPy && off == pointerOff) return;
            SetCursorPos(x, y);
            lastPx = x; lastPy = y; pointerOff = off;
            if (off && !pointerLogged) { pointerLogged = true; Log.Write($"steer: cursor unlocked, steering with the pointer ({x},{y}), centre ({c.X},{c.Y}), reach {reach}"); }
        }
        static int aimTurnSign;
        static float steerDiagAt;

        public static void FixedTick()
        {
            if (!inSpace) return;
            var ship = Ship.PlayerShip;
            if (ship == null) return;
            float yaw = steerYaw, pitch = steerPitch;
            if ((homing || autoAim || AimPoint.HasValue) && target != null && ship.autoPilot == Il2CppUnderspace.pilotSetting.FREEFLIGHT)
            {
                Angles(AimPoint ?? target.transform.position, out float h, out float v);
                // Target nearly behind: "180 right" and "180 left" swap every frame and the
                // ship never turned (11/25). Keep turning the way we started until it is in front.
                if (Mathf.Abs(h) > 150f && aimTurnSign != 0) h = 180f * aimTurnSign;
                else aimTurnSign = h >= 0 ? 1 : -1;
                yaw = Mathf.Clamp(h / 15f, -1f, 1f);
                pitch = Mathf.Abs(h) > 150f ? 0f : Mathf.Clamp(v / 15f, -1f, 1f);
            }
            else aimTurnSign = 0;
            // 11/25 PROVEN: with the cursor unlocked (lock None, which the game sets after some
            // popups and during autopilot flights) the game steers by where the pointer sits
            // on screen, like a joystick, and holds the ship against the mod's turning (1 degree
            // a second instead of 60). Then the mod steers the game's own way: pointer off the
            // centre while turning, back in the centre otherwise. Locked/Confined: turn the body.
            if (!MenuNav.Active && MenuNav.Focused() && Cursor.lockState == CursorLockMode.None)
            {
                PointerStick(yaw, pitch);
                return;
            }
            pointerOff = false; lastPx = int.MinValue;   // centre again next time the cursor is unlocked
            if (yaw == 0 && pitch == 0) return;
            float dt = Time.fixedDeltaTime;
            float rate = (homing || autoAim) ? AimTurnRate : TurnRate;
            var rot = Quaternion.Euler(-pitch * rate * dt, yaw * rate * dt, 0f);
            Rigidbody rb = null;
            try { rb = ship.physics != null ? ship.physics.rbody : null; } catch { }
            if (rb != null)
            {
                rb.MoveRotation(rb.rotation * rot);
                rb.angularVelocity = Vector3.zero;
            }
            else ship.transform.rotation = ship.transform.rotation * rot;
            // Diagnostic (11/25: arrows stopped turning the ship after a tutorial popup).
            if (Time.unscaledTime > steerDiagAt)
            {
                steerDiagAt = Time.unscaledTime + 1f;
                var e = ship.transform.rotation.eulerAngles;
                Log.Write($"steer diag: yaw {yaw:0.00} pitch {pitch:0.00}, ship heading {e.y:0} pitch {e.x:0}, rb {(rb != null)} kinematic {(rb != null && rb.isKinematic)}, input yaw {ship.input.yaw:0.00} pitch {ship.input.pitch:0.00}, physics suspended {ship.input.isPhysicsSuspended}, lock {Cursor.lockState}"
                    + (rb != null ? $", rb heading {rb.rotation.eulerAngles.y:0}, rb is ship transform {rb.transform == ship.transform}, move heading {(rb.velocity.sqrMagnitude > 1 ? Quaternion.LookRotation(rb.velocity).eulerAngles.y : -1):0}, constraints {rb.constraints}" : "")
                    + (Camera.main != null ? $", camera heading {Camera.main.transform.eulerAngles.y:0}" : ""));
            }
            if (!steerLogged)
            {
                steerLogged = true;
                Log.Write($"steer: first turn yaw {yaw:0.00} pitch {pitch:0.00}, rigidbody {(rb != null)}, mouse input {ship.input.useMouseInput}, free flight {ship.input.trueFreeFlight}");
            }
        }
    }
}
