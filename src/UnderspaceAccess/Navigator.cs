using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace UnderspaceAccess
{
    // Places list for walking inside a station. The player walks with the game's own keys
    // (W A S D); the mod only says where the chosen place is, compared to the way they face.
    //   Page Up / Page Down: category (People, Doors, Things, Chairs, Everything).
    //   Up / Down arrows: previous / next place in the category (name, meters, direction).
    //   Q / E or Left / Right arrows (hold): turn left / right. Home: turn to face the chosen place.
    //   End: where is the chosen place now. Delete: stop the walking directions.
    // While W A S D are held, the direction and meters are spoken whenever they change.
    internal static class Navigator
    {
        const int VK_DELETE = 0x2E, VK_W = 0x57, VK_A = 0x41, VK_S = 0x53, VK_D = 0x44, VK_Q = 0x51, VK_E = 0x45;
        static readonly string[] categories = { "People", "Doors", "Things", "Chairs", "Everything" };

        class Place { public string name; public string kind; public Transform t; public Component src; }

        static int category;
        static List<Place> places = new();
        static int index = -1;
        static Place target;
        static bool guiding;
        static string lastSaid = "";
        static float nextGuide, lastGuideSpoken, nextStuckCheck, lastStuckSay;
        static Vector3 stuckFrom;
        static bool arrivedSaid;
        static bool turning, facingSaid;
        static bool turnLogged;
        static int roomId;

        public static void Tick(PlayerController pc)
        {
            var interior = pc != null ? pc.currentInterior : null;
            bool inStation = interior != null && interior.gameObject.activeInHierarchy && pc.playerController != null;
            if (!inStation) { if (target != null) Stop(false); return; }
            var room = interior.currentRoom;
            int id = room != null ? room.GetInstanceID() : 0;
            if (id != roomId) { roomId = id; places.Clear(); index = -1; if (target != null) Stop(false); }

            if (MenuNav.Pressed(MenuNav.VK_NEXT)) ChangeCategory(pc, room, 1);
            else if (MenuNav.Pressed(MenuNav.VK_PRIOR)) ChangeCategory(pc, room, -1);
            else if (MenuNav.Pressed(MenuNav.VK_DOWN, true)) Step(pc, room, 1);
            else if (MenuNav.Pressed(MenuNav.VK_UP, true)) Step(pc, room, -1);
            else if (MenuNav.Pressed(MenuNav.VK_HOME)) FaceTarget(pc);
            else if (MenuNav.Pressed(MenuNav.VK_END)) Speech.Say(target != null ? Where(pc, target, true) : "No place chosen. Use Page Up or Page Down, then the Up and Down arrows.");
            else if (MenuNav.Pressed(VK_DELETE)) Stop(true);

            // Hold Q / E (or Left / Right arrow) to turn. Passing the chosen place says "Facing";
            // when let go, say where the place is now.
            bool left = MenuNav.Down(VK_Q) || MenuNav.Down(MenuNav.VK_LEFT), right = MenuNav.Down(VK_E) || MenuNav.Down(MenuNav.VK_RIGHT);
            if (left != right)
            {
                if (!turning) { turning = true; facingSaid = target != null && Mathf.Abs(Angle(pc, target)) < 12f; }
                Turn(pc, (right ? 1f : -1f) * (MenuNav.Down(MenuNav.VK_SHIFT) ? 30f : 90f) * Time.unscaledDeltaTime);
                if (target != null && !facingSaid && Mathf.Abs(Angle(pc, target)) < 12f)
                {
                    facingSaid = true;
                    Speech.Say($"Facing {target.name}. Let go.");
                }
            }
            else if (turning)
            {
                turning = false;
                if (target == null) Speech.Say("Facing " + Compass(pc) + ".");
                else { lastSaid = Bucket(pc, target); Speech.Say(Where(pc, target, false) + (Mathf.Abs(Angle(pc, target)) < 20f ? " Walk with W." : "")); }
            }

            if (target != null && guiding) Guide(pc);
            InteractCheck(pc);
        }

        // ---------- F (interact) ----------

        // The game decides itself what F talks to (HYPOTHESIS: what the view points at). The mod
        // logs what the game had at the moment of F; when the player stands at the chosen place and
        // nothing opened, the mod starts that talk or use the same way the game does.
        const int VK_F = 0x46;
        const float TalkReach = 18f;
        static float fCheckAt;
        static Place fTarget;
        static void InteractCheck(PlayerController pc)
        {
            if (MenuNav.Pressed(VK_F))
            {
                var ui = StationUI.mainUI;
                string prompt = ui != null && ui.primaryInteractionUI != null ? MenuNav.StripTags(ui.primaryInteractionUI.text) : "";
                string hover = ui != null && ui.hoverIconText != null ? MenuNav.StripTags(ui.hoverIconText.text) : "";
                string tgt = target != null ? $"{target.name} {Meters(pc, target):0.0}m angle {Angle(pc, target):0}" : "none";
                Log.Write($"F pressed: lookingNPC {Name(ui?.lookingNPC)}, currentNPC {Name(ui?.currentNPC)}, inNPCMenu {ui != null && ui.inNPCMenu}, prompt '{prompt}', hover '{hover}', target {tgt}");
                // People often stand behind a counter: from up to 10 meters F still talks (10/25: stuck 9 m from the Wreckage Vendor).
                // 11/3: blocked 13-15 m from Hantaonailati in the chapterhouse, F did nothing: reach now 18 m.
                fTarget = target != null && Meters(pc, target) < (target.kind == "person" || target.kind == "thing" ? TalkReach : 4f) ? target : null;
                fCheckAt = Time.unscaledTime + 0.7f;
            }
            if (fCheckAt <= 0 || Time.unscaledTime < fCheckAt) return;
            // Checked late: a menu opened in between (this code does not run while one is open),
            // so F worked. 10/24: the late check reopened the Shipwright's shop after Close.
            bool stale = Time.unscaledTime > fCheckAt + 0.5f;
            fCheckAt = 0;
            if (stale) { Log.Write("after F: a menu opened meanwhile, no fallback"); return; }
            var u = StationUI.mainUI;
            var quest = QuestUI.mainQuest;
            bool opened = MenuNav.Active || pc.conversationOngoing || (u != null && u.inNPCMenu)
                          || (CommsUI.mainComms != null && CommsUI.mainComms.commsActive)
                          || (quest != null && quest.questMenu != null && quest.questMenu.activeInHierarchy);
            Log.Write($"after F: opened {opened} (menu {MenuNav.Active}, inNPCMenu {u != null && u.inNPCMenu}, conversation {pc.conversationOngoing})");
            if (opened || fTarget == null || fTarget.src == null) return;
            try
            {
                var npc = fTarget.src.TryCast<StationNPC>();
                var act = fTarget.src.TryCast<StationActivator>();
                if (npc != null) { Log.Write("F fallback: doInteraction on " + npc.NPCName); Speech.Say("Talking to " + fTarget.name + "."); npc.doInteraction(false); }
                else if (act != null) { Log.Write("F fallback: activateObject on " + fTarget.name); Speech.Say("Using " + fTarget.name + "."); act.activateObject(0f); }
                else Speech.Say("For a door, walk into it.");
            }
            catch (Exception e) { Log.Write("F fallback failed: " + e.Message); }
        }

        static string Name(StationNPC n) => n == null ? "none" : MenuNav.StripTags(n.NPCName);

        // ---------- the list ----------

        static void ChangeCategory(PlayerController pc, BaseRoom room, int dir)
        {
            category = (category + dir + categories.Length) % categories.Length;
            Collect(pc, room);
            index = places.Count > 0 ? 0 : -1;
            if (index < 0) { Speech.Say(categories[category] + ": none in this room."); return; }
            Choose(pc, places[0], $"{categories[category]}, {places.Count}. ");
        }

        static void Step(PlayerController pc, BaseRoom room, int dir)
        {
            if (places.Count == 0) { Collect(pc, room); index = -1; }
            if (places.Count == 0) { Speech.Say(categories[category] + ": none in this room. Page Up or Page Down changes the category."); return; }
            index = Mathf.Clamp(index + dir, 0, places.Count - 1);
            Choose(pc, places[index], "");
        }

        static void Choose(PlayerController pc, Place p, string prefix)
        {
            target = p;
            guiding = true;
            arrivedSaid = false;
            lastSaid = "";
            nextGuide = Time.unscaledTime + 1f;
            string where = Where(pc, p, false);
            lastSaid = Bucket(pc, p);
            Speech.Say($"{prefix}{p.name}, {where} {index + 1} of {places.Count}.{TurnHint(Angle(pc, p))}");
        }

        static void Stop(bool say)
        {
            target = null; guiding = false;
            if (say) Speech.Say("Directions off.");
        }

        static void Collect(PlayerController pc, BaseRoom room)
        {
            var all = new List<Place>();
            string cat = categories[category];
            bool every = cat == "Everything";
            Vector3 me = pc.playerController.transform.position;
            bool InRoom(Component c) => c != null && c.gameObject.activeInHierarchy && (room == null || c.transform.IsChildOf(room.transform));

            if (every || cat == "People")
                foreach (var npc in UnityEngine.Object.FindObjectsOfType<StationNPC>())
                    if (InRoom(npc) && !string.IsNullOrWhiteSpace(npc.NPCName))
                    {
                        // The quest marker a sighted player sees over a head (StationNPC.hasQuest).
                        string extra = HasQuest(npc) ? ", has something for you" : "";
                        all.Add(new Place { name = MenuNav.StripTags(npc.NPCName) + extra, kind = "person", t = npc.transform, src = npc });
                        Log.Write($"person {MenuNav.StripTags(npc.NPCName)}: interaction {npc.currentInteraction}, quest marker {HasQuest(npc)}, quest id '{npc.npcQuestID}', canInteract {npc.canInteract}, desc '{MenuNav.StripTags(npc.NPCDescription ?? "")}'");
                    }

            if (every || cat == "Doors")
                foreach (var w in UnityEngine.Object.FindObjectsOfType<RoomWarp>())
                {
                    if (!InRoom(w)) continue;
                    var to = w.roomToWarpTo;
                    string n = to != null && !string.IsNullOrWhiteSpace(to.roomName) ? MenuNav.StripTags(to.roomName) : to != null ? InGame.RoomName(to.roomType) : "";
                    if (Vector3.Distance(me, w.transform.position) > 300f) continue; // warp nodes far outside the room
                    all.Add(new Place { name = n.Length > 0 ? "Door to the " + n : "Door", kind = "door", t = w.transform, src = w });
                }

            if (every || cat == "Things" || cat == "Chairs")
                foreach (var a in UnityEngine.Object.FindObjectsOfType<StationActivator>())
                {
                    if (!InRoom(a) || !a.playerCanActivate) continue;
                    var chair = a.TryCast<StationChair>();
                    if (chair != null && chair.currentSitter != null) continue; // someone sits there
                    bool isChair = chair != null;
                    if ((cat == "Chairs") != isChair) continue; // 160 chairs would bury everything else
                    all.Add(new Place { name = ThingName(a, isChair), kind = isChair ? "chair" : "thing", t = a.transform, src = a });
                }

            all.Sort((x, y) => Vector3.Distance(me, x.t.position).CompareTo(Vector3.Distance(me, y.t.position)));
            places = all;
            var names = new List<string>();
            foreach (var p in all) names.Add($"{p.name} ({p.kind}) {Mathf.RoundToInt(Vector3.Distance(me, p.t.position))}m");
            Log.Write($"places {cat}: {all.Count} | {string.Join(" | ", names)}");
        }

        static bool HasQuest(StationNPC n)
        {
            try { return n.hasQuest != null && n.hasQuest.activeInHierarchy; } catch { return false; }
        }

        // Semicolon: when the game says "talk to the chaptermaster" (or anyone), choose the person
        // in this room who most likely is meant, so the walking directions start at once.
        // Order: quest marker, then a job or quest giver, then the nearest who can talk.
        public static string ChooseTalkPerson(PlayerController pc)
        {
            var interior = pc != null ? pc.currentInterior : null;
            if (interior == null || pc.playerController == null) return "";
            var room = interior.currentRoom;
            Vector3 me = pc.playerController.transform.position;
            StationNPC best = null; int bestScore = -1; float bestDist = 0;
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<StationNPC>())
            {
                if (npc == null || !npc.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(npc.NPCName)) continue;
                if (room != null && !npc.transform.IsChildOf(room.transform)) continue;
                int score = HasQuest(npc) ? 3 : (npc.currentInteraction == Il2CppUnderspace.StationInteractions.JOB || npc.currentInteraction == Il2CppUnderspace.StationInteractions.QUEST) ? 2 : npc.canInteract ? 1 : 0;
                float d = Vector3.Distance(me, npc.transform.position);
                if (score > bestScore || (score == bestScore && d < bestDist)) { best = npc; bestScore = score; bestDist = d; }
            }
            if (best == null) return "";
            Log.Write($"talk person chosen: {best.NPCName} (score {bestScore}, {bestDist:0}m)");
            category = 0; // People
            Collect(pc, room);
            index = places.FindIndex(p => p.src == best);
            if (index < 0) return "";
            var place = places[index];
            target = place; guiding = true; arrivedSaid = false;
            lastSaid = Bucket(pc, place); nextGuide = Time.unscaledTime + 1f;
            return $"Person to talk to: {place.name}, {Where(pc, place, false)}{TurnHint(Angle(pc, place))} Chosen for you: walk with W, the mod guides you, press F when it says close enough";
        }

        static string ThingName(StationActivator a, bool isChair)
        {
            string n = MenuNav.StripTags(a.activatorName ?? "").Trim();
            if (n.Length > 0) return n;
            if (isChair) return "Empty chair";
            if (a.TryCast<StationReader>() != null) return "Computer";
            if (a.TryCast<StationLever>() != null) return "Lever";
            if (a.TryCast<StationButton>() != null) return "Button";
            if (a.TryCast<StationGambler>() != null) return "Gambling machine";
            if (a.TryCast<StationTransferActivator>() != null) return "Transport";
            return a.gameObject.name;
        }

        // ---------- directions ----------

        static Vector3 Me(PlayerController pc) => pc.playerController.transform.position;

        static Vector3 Facing(PlayerController pc)
        {
            var cam = pc.playerCamera != null ? pc.playerCamera.transform : (Camera.main != null ? Camera.main.transform : pc.playerController.transform);
            var f = cam.forward; f.y = 0;
            return f.sqrMagnitude < 0.001f ? pc.playerController.transform.forward : f.normalized;
        }

        // Angle of the place from where the player faces: 0 ahead, positive to the right.
        static float Angle(PlayerController pc, Place p)
        {
            var to = p.t.position - Me(pc); to.y = 0;
            return Vector3.SignedAngle(Facing(pc), to, Vector3.up);
        }

        static string Direction(float a)
        {
            float m = Mathf.Abs(a);
            string side = a > 0 ? "right" : "left";
            if (m < 20) return "straight ahead";
            if (m < 60) return "ahead to your " + side;
            if (m < 120) return "to your " + side;
            if (m < 160) return "behind you, " + side;
            return "behind you";
        }

        static float Meters(PlayerController pc, Place p) { var d = p.t.position - Me(pc); d.y = 0; return d.magnitude; }

        static string Where(PlayerController pc, Place p, bool withName)
        {
            if (p.t == null) return "That place is gone.";
            int m = Mathf.RoundToInt(Meters(pc, p));
            string s = (withName ? p.name + ", " : "") + (m <= 2 ? "right here, " : m + (m == 1 ? " meter, " : " meters, ")) + Direction(Angle(pc, p)) + ".";
            return s;
        }

        static string Bucket(PlayerController pc, Place p) => Direction(Angle(pc, p));

        // While walking keys are held: say the direction when it changes, and the meters every few steps.
        static void Guide(PlayerController pc)
        {
            if (target.t == null || !target.t.gameObject.activeInHierarchy) { Stop(false); return; }
            bool moving = MenuNav.Down(VK_W) || MenuNav.Down(VK_A) || MenuNav.Down(VK_S) || MenuNav.Down(VK_D);
            float dist = Meters(pc, target);
            if (dist < 2.5f)
            {
                if (!arrivedSaid)
                {
                    arrivedSaid = true;
                    float a = Angle(pc, target);
                    Speech.Say($"You are at {target.name}." + (Mathf.Abs(a) < 30 ? " Press F." : $" It is {Direction(a)}.{TurnHint(a)} Or press Home to face it. Then press F."));
                }
                return;
            }
            if (dist > 4f) arrivedSaid = false;
            if (!moving) { nextStuckCheck = 0; return; }

            // Held the walking keys for a while without getting anywhere: something is in the way.
            if (nextStuckCheck == 0) { nextStuckCheck = Time.unscaledTime + 1.5f; stuckFrom = Me(pc); }
            else if (Time.unscaledTime >= nextStuckCheck)
            {
                if (Vector3.Distance(stuckFrom, Me(pc)) < 0.4f && Time.unscaledTime - lastStuckSay > 3f)
                {
                    lastStuckSay = Time.unscaledTime;
                    if ((target.kind == "person" || target.kind == "thing") && dist < TalkReach) Speech.Say($"Blocked, probably a counter or a table. You are close enough to {target.name}. Press F.");
                    else Speech.Say("Blocked. Try walking to the side with A or D.");
                }
                nextStuckCheck = Time.unscaledTime + 1.5f; stuckFrom = Me(pc);
            }

            if (Time.unscaledTime < nextGuide) return;
            nextGuide = Time.unscaledTime + 0.4f;
            string bucket = Bucket(pc, target);
            bool changed = bucket != lastSaid;
            if (!changed && Time.unscaledTime - lastGuideSpoken < 3f) return;
            lastSaid = bucket;
            lastGuideSpoken = Time.unscaledTime;
            int m = Mathf.RoundToInt(dist);
            Speech.Say(changed ? $"{bucket}, {m}.{TurnHint(Angle(pc, target))}" : $"{m}");
        }

        static string TurnHint(float a) => Mathf.Abs(a) < 20f ? "" : a > 0 ? " Turn right with E." : " Turn left with Q.";

        // ---------- turning ----------

        // The game turns the view with the mouse only. The mod turns the same body the mouse
        // turns (the look script that handles left-right), so the game keeps the new direction.
        static Transform YawTransform(PlayerController pc)
        {
            foreach (var look in new[] { pc.mouseLookController, pc.mouseLookController2 })
                if (look != null && look.isActiveAndEnabled && look.axes != MouseLookUnderspace.RotationAxes.MouseY) return look.transform;
            return pc.playerController.transform;
        }

        static void Turn(PlayerController pc, float degrees)
        {
            var t = YawTransform(pc);
            float before = t.eulerAngles.y;
            t.Rotate(0f, degrees, 0f, Space.World);
            if (!turnLogged)
            {
                turnLogged = true;
                Log.Write($"turn: rotating {t.name} (look1 {Describe(pc.mouseLookController)}, look2 {Describe(pc.mouseLookController2)}), yaw {before:0.0} -> {t.eulerAngles.y:0.0}, camera {(pc.playerCamera != null ? pc.playerCamera.name : "none")}");
                checkYawAt = Time.frameCount + 5; checkYaw = t.eulerAngles.y; checkT = t;
            }
        }

        static int checkYawAt; static float checkYaw; static Transform checkT;
        public static void LateCheck()
        {
            if (checkT == null || Time.frameCount < checkYawAt) return;
            Log.Write($"turn check: {checkT.name} yaw now {checkT.eulerAngles.y:0.0} (was set to {checkYaw:0.0} five frames ago, arrows may still be held)");
            checkT = null;
        }

        static string Describe(MouseLookUnderspace l) => l == null ? "none" : $"{l.name} axes {l.axes} enabled {l.isActiveAndEnabled}";

        static void FaceTarget(PlayerController pc)
        {
            if (target == null || target.t == null) { Speech.Say("No place chosen."); return; }
            Turn(pc, Angle(pc, target));
            Speech.Say($"Facing {target.name}, {Mathf.RoundToInt(Meters(pc, target))} meters ahead.");
            lastSaid = Bucket(pc, target);
        }

        static string Compass(PlayerController pc)
        {
            var f = Facing(pc);
            float deg = (Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + 360f) % 360f;
            string[] names = { "north", "north east", "east", "south east", "south", "south west", "west", "north west" };
            return names[Mathf.RoundToInt(deg / 45f) % 8];
        }
    }
}
