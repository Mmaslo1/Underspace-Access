using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2Cpp;
using UnityEngine;

namespace UnderspaceAccess
{
    // Campaign missions and shooting from the keyboard (0.10.0).
    //   Semicolon: what to do now: mission, objective, and in space selects the mission waypoint.
    //   Hold Period: aim at the target and fire. The mod holds the real right mouse button, the
    //   game's own fire control. SDK ComponentGun.activateComponent: with TargetingUI.curveTarget
    //   set, the player's shot leads that target (same as a sighted player's mouse on the pip).
    internal static class Mission
    {
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        const uint RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
        const int VK_PERIOD = 0xBE, VK_SEMICOLON = 0xBA;

        static bool rmb;
        public static bool FireHeld;

        public static void Tick()
        {
            bool free = !MenuNav.Active && MenuNav.Focused();
            if (free && MenuNav.Pressed(VK_SEMICOLON)) Speech.Say(WhatNow());
            FireTick(free);
            try { Scanner.Tick(); } catch (Exception e) { Log.Write("scanner error: " + e.Message); }
            Watch();
            AreaWatch();
        }

        // ---------- firing ----------

        static bool throttleSaid, wasHeld, inRange, tooFarSaid, curveLogged;
        static float cruiseOffAt = -10f, tooFarRepeatAt, burstStart, startHull, startShield, usingLogAt = -1f;
        static int hullStep = 4, shieldStep = 4, lastTargetId;

        public static void ReleaseMouse()
        {
            if (!rmb) return;
            rmb = false;
            mouse_event(RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        static float stillCheckAt;

        static float Hull(BaseSolar t)
        {
            float p = t.getHealthPercentage();
            return p <= 1.001f ? p * 100f : p;
        }

        static float Shield(BaseSolar t) => t.hasShield && t.shieldMax > 0 ? 100f * t.shieldCurrent / t.shieldMax : 0f;

        // 11/27: FireControl.MaxGunRange read 0 for the player, so the mod fired from 2-5 km
        // and never hit. Now: the longest ComponentGun.range on the ship (SDK default 600 m).
        static int rangeShipId;
        static float rangeCached, rangeAt;
        internal static float GunRange(Ship ship)
        {
            if (ship.GetInstanceID() == rangeShipId && Time.unscaledTime < rangeAt) return rangeCached;
            float r = 0f; string src = "FireControl";
            try { r = ship.FireControl.MaxGunRange; } catch { }
            if (r <= 0f)
            {
                src = "guns";
                try
                {
                    foreach (var g in ship.GetComponentsInChildren<ComponentGun>(true))
                        if (g != null && g.range > r) r = g.range;
                }
                catch (Exception e) { Log.Write("gun range read failed: " + e.Message); }
            }
            if (r <= 0f) { r = 600f; src = "default"; }
            if (ship.GetInstanceID() != rangeShipId || Mathf.Abs(r - rangeCached) > 1f) Log.Write($"gun range {r:0} m ({src})");
            rangeShipId = ship.GetInstanceID(); rangeCached = r; rangeAt = Time.unscaledTime + 15f;
            return r;
        }

        static void FireTick(bool free)
        {
            var ship = Ship.PlayerShip;
            bool held = free && Flight.InSpace && ship != null && MenuNav.Down(VK_PERIOD);
            FireHeld = held;
            if (!held)
            {
                ReleaseMouse();
                if (wasHeld)
                {
                    var e = Flight.CurrentTarget;
                    Log.Write($"fire end: {Time.unscaledTime - burstStart:0.0}s" + (e != null ? $", target hull {startHull:0}->{Hull(e):0}, shields {startShield:0}->{Shield(e):0}" : ", target gone"));
                }
                wasHeld = false;
                return;
            }

            var t = Flight.CurrentTarget;
            bool press = !wasHeld;
            wasHeld = true;
            if (press)
            {
                burstStart = Time.unscaledTime;
                tooFarSaid = false; inRange = false; curveLogged = false; throttleSaid = false;
                usingLogAt = Time.unscaledTime + 0.4f;
            }

            // 11/25: enemies attacked while the autopilot flew to a waypoint, and Period did
            // nothing. Holding Period now presses the game's own F1 (take over) first, once.
            if (ship.autoPilot == Il2CppUnderspace.pilotSetting.GOTO || ship.autoPilot == Il2CppUnderspace.pilotSetting.DOCK)
            {
                ReleaseMouse();
                if (press)
                {
                    keybd_event(0x70, 0x3B, 0, UIntPtr.Zero); keybd_event(0x70, 0x3B, 2, UIntPtr.Zero);
                    Log.Write("fire: autopilot " + ship.autoPilot + ", pressed F1 to take over");
                    Speech.Say("Autopilot off. Keep holding Period to fight.");
                }
                return;
            }
            // Flight's steering says "press F1" while the autopilot flies (lanelines, docking).
            if (ship.autoPilot != Il2CppUnderspace.pilotSetting.FREEFLIGHT) { ReleaseMouse(); return; }
            if (t == null)
            {
                ReleaseMouse();
                if (press) Speech.Say("No target. Press Comma for the nearest enemy.");
                return;
            }
            if (t.TryCast<Ship>() == null)
            {
                ReleaseMouse();
                if (press) Speech.Say($"{Flight.SolarName(t)} is not a ship. Press Comma for the nearest enemy.");
                return;
            }
            int id = t.GetInstanceID();
            if (press || id != lastTargetId)
            {
                lastTargetId = id; hullStep = 4; shieldStep = 4;
                startHull = Hull(t); startShield = Shield(t);
                Flight.Angles(t.transform.position, out float h0, out float v0);
                Log.Write($"fire start: {Flight.SolarName(t)} {Flight.Meters(t):0}m, off {h0:0} / {v0:0} deg, gun range {GunRange(ship):0}, cruise {ship.isInCruise}, mouse input {ship.input.useMouseInput}");

            }

            // Guns have a range: hold fire and say so until the target is inside it.
            float range = GunRange(ship);
            // 11/27: the player tapped W fast (as told), the double tap switched high engines on and
            // the guns stopped ("slow down", no key known). Near the target the mod switches them off.
            if (ship.isInCruise && Flight.Meters(t) < range * 1.5f && Time.unscaledTime > cruiseOffAt + 1f)
            {
                cruiseOffAt = Time.unscaledTime;
                try { ship.knockShipOutofCruise(); Log.Write("fire: knockShipOutofCruise"); }
                catch (Exception e) { Log.Write("knockShipOutofCruise failed: " + e.Message + ", pressing S"); keybd_event(0x53, 0x1F, 0, UIntPtr.Zero); keybd_event(0x53, 0x1F, 2, UIntPtr.Zero); }
                Speech.Say("High engines off, so you can shoot. Do not tap W fast: two quick taps switch them on.", false);
            }
            if (range > 0f && Flight.Meters(t) > range)
            {
                ReleaseMouse();
                inRange = false;
                // Holding Period also flies the ship closer: nose on target + full throttle.
                try { ship.input.throttle = 1f; } catch { }
                if (!tooFarSaid)
                {
                    tooFarSaid = true; tooFarRepeatAt = Time.unscaledTime + 2.5f;
                    Speech.Say($"Too far, {Flight.Distance(t)}. Guns reach {Mathf.RoundToInt(range)} meters. Keep holding Period: flying you closer.");
                }
                else if (Time.unscaledTime > tooFarRepeatAt)
                {
                    tooFarRepeatAt = Time.unscaledTime + 2.5f;
                    Speech.Say(Flight.Distance(t) + ".", false);
                }
                return;
            }
            // 11/27: died twice at speed 0 under 5 Nebulas (the autopilot leaves the throttle at 0).
            // While Period is held in range the ship keeps moving at 80 percent or more.
            try
            {
                if (ship.input.throttle < 0.6f)
                {
                    ship.input.throttle = 0.8f;
                    if (!throttleSaid) { throttleSaid = true; Speech.Say("Throttle up, keep moving.", false); Log.Write("fire: throttle raised to 0.8"); }
                }
            }
            catch { }
            if (!inRange)
            {
                inRange = true;
                if (tooFarSaid) Speech.Say("In range. Firing.");
            }

            var ui = TargetingUI.mainTargetingUI;
            if (ui != null)
            {
                // HYPOTHESIS: the game may clear this each frame; logged once per burst.
                if (!curveLogged && Time.unscaledTime > burstStart + 0.5f)
                {
                    curveLogged = true;
                    Log.Write("curveTarget before mod set: " + (ui.curveTarget != null ? ui.curveTarget.name : "none"));
                }
                ui.curveTarget = t.transform;
            }
            if (!rmb) { rmb = true; mouse_event(RIGHTDOWN, 0, 0, 0, UIntPtr.Zero); }
            if (usingLogAt > 0 && Time.unscaledTime > usingLogAt)
            {
                usingLogAt = -1f;
                Log.Write($"fire check: input.isUsingEquipment {ship.input.isUsingEquipment}, energy {ship.powerPlantCurrent:0}/{ship.powerPlantMax:0}");
            }

            // 11/25: died twice sitting still (speed 0 after the autopilot handed over).
            if (Time.unscaledTime > stillCheckAt)
            {
                stillCheckAt = Time.unscaledTime + 20f;
                float speed = 0f;
                try { speed = ship.physics.rbody.velocity.magnitude; } catch { }
                if (speed < 15f) Speech.Say("You are standing still. Hold W for one second to throttle up: a moving ship is harder to hit. Do not tap it twice quickly.", false);
            }

            // Tell the player the shots are doing something.
            int ss = Bucket(Shield(t)), hs = Bucket(Hull(t));
            if (t.hasShield && t.shieldMax > 0 && ss < shieldStep)
                Speech.Say(ss == 0 ? "Target shields down." : $"Target shields {Mathf.RoundToInt(Shield(t))} percent.", false);
            if (hs < hullStep) Speech.Say($"Target hull {Mathf.RoundToInt(Hull(t))} percent.", false);
            shieldStep = ss; hullStep = hs;
        }

        static int Bucket(float p) => p > 75 ? 4 : p > 50 ? 3 : p > 25 ? 2 : p > 0 ? 1 : 0;

        // ---------- what to do now ----------

        static string WhatNow()
        {
            var parts = new List<string>();
            var cm = CampaignManager.mainManager;
            if (cm != null && cm.currentCampaign > 0 && cm.currentCampaignMission != null && !string.IsNullOrWhiteSpace(cm.currentCampaignMission.campaignMissionName))
                parts.Add("Mission: " + InGame.Clean(cm.currentCampaignMission.campaignMissionName));
            var q = QuestUI.mainQuest;
            if (q != null)
            {
                string name = q.questTrackerName != null ? InGame.Clean(q.questTrackerName.text) : "";
                string obj = q.questTrackerObjective != null ? InGame.Clean(q.questTrackerObjective.text) : "";
                if (name.Length > 0) parts.Add("Quest: " + name);
                if (obj.Length > 0) parts.Add("Objective: " + obj);
            }
            if (Flight.InSpace)
            {
                var ch = ContactHUD.mainContactHUD;
                var w = ch != null ? (ch.currentMissionWaypoint ?? ch.currentQuestWaypoint ?? ch.currentWaypoint) : null;
                if (w != null)
                {
                    Flight.SelectFromMission(w);
                    parts.Add($"Waypoint selected: {Flight.SolarName(w)}, {Flight.Distance(w)}, {Flight.Direction(w)}. F2 flies there");
                }
                else parts.Add("No waypoint right now");
            }
            else if (InGame.LastTask.Length > 0 && InGame.LastTask.Contains("Talk to"))
            {
                // 11/3: "Talk to the chaptermaster" was said once; the player found doors, no chaptermaster.
                parts.Add("Game said: " + InGame.LastTask);
                string who = Navigator.ChooseTalkPerson(PlayerController.Player);
                parts.Add(who.Length > 0 ? who : "Nobody to talk to in this room. F10 lists the rooms and their F keys");
            }
            else if (InGame.LastTask.Length > 0 && Time.unscaledTime - InGame.LastTaskAt < 600f && (cm == null || cm.currentWantedRoomType == Il2CppUnderspace.stationRoomTypes.NONE))
                parts.Add("Game said: " + InGame.LastTask);
            else if (cm != null && cm.currentWantedRoomType != Il2CppUnderspace.stationRoomTypes.NONE)
            {
                parts.Add("Go to the " + InGame.RoomName(cm.currentWantedRoomType));
                var keys = InGame.RoomKeys();
                if (keys.Count > 0) parts.Add("Room keys: " + string.Join(", ", keys));
            }
            if (cm != null && cm.currentCampaign == 0 && cm.campaignState > 0)
            {
                string next = NextMissions(cm);
                if (next.Length > 0) parts.Add(next);
            }
            string line = parts.Count > 0 ? string.Join(". ", parts) + "." : "No objective shown right now. O opens the quest log.";
            Log.Write("what now: " + line);
            return line;
        }

        // 11/27: after Mission 1 the game says only "Gain more experience and await word from the
        // Idolsmashers". The mission list (allCampaignMissions) holds which missions can start at the
        // current campaignState and their difficulty. Dumped once; HYPOTHESIS: difficulty ~ level.
        static bool missionsDumped;
        static int openingLevel = -1;
        static List<string> ToList(Il2CppSystem.Collections.Generic.List<string> l)
        {
            var r = new List<string>();
            for (int i = 0; l != null && i < l.Count; i++) r.Add(l[i]);
            return r;
        }
        static string NextMissions(CampaignManager cm)
        {
            var found = new List<string>();
            try
            {
                var all = cm.allCampaignMissions;
                if (all == null) return "";
                for (int i = 0; i < all.Count; i++)
                {
                    var m = all[i];
                    if (m == null) continue;
                    var stages = new List<string>();
                    bool ok = false;
                    if (m.validCampaignStartingStages != null)
                        for (int j = 0; j < m.validCampaignStartingStages.Count; j++)
                        {
                            int st = m.validCampaignStartingStages[j];
                            stages.Add(st.ToString());
                            if (st == cm.campaignState) ok = true;
                        }
                    if (!missionsDumped)
                        Log.Write($"campaign mission {m.campaignMissionNumber} '{m.campaignMissionName}' id {m.campaignMissionID} difficulty '{m.campaignMissionDifficult}' reward '{m.campaignMissionReward}' faction '{m.campaignMissionFaction}' stages [{string.Join(",", stages)}] start '{m.campaignStartingAssignment}' desc '{m.campaignMissionDescription}'");
                    if (ok) found.Add($"{InGame.Clean(m.campaignMissionName)}, difficulty {InGame.Clean(m.campaignMissionDifficult)}");
                }
                if (!missionsDumped)
                {
                    try
                    {
                        Log.Write($"campaign state {cm.campaignState}, active events [{string.Join(",", ToList(cm.activeEventList))}], finished [{string.Join(",", ToList(cm.finishedEvents))}]");
                        var ev = cm.allEvents;
                        for (int i = 0; ev != null && i < ev.Count; i++)
                        {
                            var e = ev[i]; if (e == null) continue;
                            var req = new List<string>();
                            if (e.initialTrackingRequirements != null)
                                for (int j = 0; j < e.initialTrackingRequirements.Count; j++)
                                {
                                    var r = e.initialTrackingRequirements[j];
                                    req.Add($"{r.neededRequirementType} {r.operand} {r.requirementValue} '{r.requirementName}'");
                                }
                            bool met = false; try { met = e.meetsRequirements(); } catch { }
                            Log.Write($"event {e.dialogueEventID}: {e.trackingEventType} threshold {e.trackingEventThreshold}, met {met}, regions [{string.Join(",", ToList(e.validRegions))}], req [{string.Join("; ", req)}]");
                        }
                    }
                    catch (Exception ex) { Log.Write("event dump failed: " + ex.Message); }
                }
                openingLevel = -1;
                try
                {
                    var ev2 = cm.allEvents;
                    for (int i = 0; ev2 != null && i < ev2.Count; i++)
                    {
                        var e = ev2[i]; if (e == null || e.initialTrackingRequirements == null) continue;
                        bool stateOk = false; int lv = -1;
                        for (int j = 0; j < e.initialTrackingRequirements.Count; j++)
                        {
                            var r = e.initialTrackingRequirements[j];
                            string ty = r.neededRequirementType.ToString(), op = r.operand.ToString();
                            if (ty == "Campaign" && op == "EQUALS" && Mathf.RoundToInt(r.requirementValue) == cm.campaignState) stateOk = true;
                            if (ty == "Level" && op == "GREATEREQUALS") lv = Mathf.RoundToInt(r.requirementValue);
                        }
                        if (stateOk && lv > 0) openingLevel = lv;
                    }
                }
                catch (Exception ex) { Log.Write("opening level failed: " + ex.Message); }
                missionsDumped = true;
            }
            catch (Exception e) { Log.Write("mission list failed: " + e.Message); }
            int lvl = -1; try { lvl = PlayerController.Player.playerLevel; } catch { }
            if (found.Count == 0 && openingLevel > 0)
                return $"The next campaign mission opens at level {openingLevel}" + (lvl > 0 ? $", you are level {lvl}" : "") + ". Earn money with jobs from the bar's Job Board, bounties, trading or mining: your level grows with your net worth. When you reach it, fly through two star systems and the Idolsmashers call you";
            if (found.Count == 0) return "";
            return "Next campaign mission: " + string.Join("; ", found) + (lvl > 0 ? $". You are level {lvl}" : "") + ". The chaptermaster on Planet Vauldric offers it";
        }

        // ---------- campaign state log, stay-near warning ----------

        static string lastState = "";
        static bool consoleWas;
        static float nextWatch;
        static void Watch()
        {
            if (Time.unscaledTime < nextWatch) return;
            nextWatch = Time.unscaledTime + 1f;
            bool con = MenuNav.ConsoleOpen();
            if (con && !consoleWas) { Log.Write("developer console opened"); Speech.Say("The game's command box opened, probably by a stray key. Press Escape to close it."); }
            consoleWas = con;
            var cm = CampaignManager.mainManager;
            if (cm == null) return;
            string name = cm.currentCampaignMission != null ? cm.currentCampaignMission.campaignMissionName : "none";
            int cine = CinematicUI.mainCinema != null ? CinematicUI.mainCinema.cinematicState : -1;
            string state = $"mission {cm.currentCampaign}, state {cm.campaignState}, name {name}, assignment {cm.currentCampaignAssignment}, interior {cm.currentMarkedInterior}, room {cm.currentWantedRoomType}, system {cm.currentMarkedSystem}, stay near {cm.currentStayNearDistance:0}, menus locked {cm.restrictMenuOpening}, cinematic {cine}";
            if (state != lastState) { lastState = state; Log.Write("campaign: " + state); }
        }

        static bool areaSaid;
        static void AreaWatch()
        {
            var cm = CampaignManager.mainManager;
            var ship = Ship.PlayerShip;
            if (cm == null || ship == null || !Flight.InSpace || cm.currentStayNearDistance <= 0f) { areaSaid = false; return; }
            float d = Vector3.Distance(ship.transform.position, cm.currentStayNearPoint);
            if (d < 0.7f * cm.currentStayNearDistance) areaSaid = false;
            else if (d > 0.85f * cm.currentStayNearDistance && !areaSaid)
            {
                areaSaid = true;
                Flight.Angles(cm.currentStayNearPoint, out float h, out _);
                Log.Write($"leaving mission area: {d:0} of {cm.currentStayNearDistance:0}");
                Speech.Say($"Leaving the mission area. Turn back, {Mathf.RoundToInt(Mathf.Abs(h))} degrees {(h > 0 ? "right" : "left")}.");
            }
        }
    }
}
