using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace UnderspaceAccess
{
    // Long-range scan (game key: hold B, release to send the pulse). 11/27: the game only draws
    // coloured rings (PlayerScanTarget) on what the pulse found: gold = point of interest (quests,
    // Mission 2 artifact), wreck colour, jumphole colour. The mod speaks them, nearest first, and
    // selects the nearest point of interest so F2 flies there.
    // HYPOTHESIS: rings are PlayerScanTarget objects alive ~3 s after release; their
    // overrideMaterial is one of PlayerScanningSystem's wreck/jump/pointOfInterest/alreadyScanned materials.
    internal static class Scanner
    {
        const int VK_B = 0x42;
        static float diagAt, heldSince = -1f, checkAt = -1f, pollEnd, lastNewAt = -1f;
        static readonly HashSet<int> seen = new();
        static readonly HashSet<string> seenNames = new();
        static readonly List<(float d, string line, BaseSolar s, int rank)> found = new();

        public static void Tick()
        {
            if (!Flight.InSpace || MenuNav.Active || !MenuNav.Focused()) { heldSince = -1f; return; }
            bool down = MenuNav.Down(VK_B);
            if (down && heldSince < 0) heldSince = Time.unscaledTime;
            if (!down && heldSince >= 0)
            {
                float held = Time.unscaledTime - heldSince;
                heldSince = -1f;
                if (held > 0.4f) { checkAt = Time.unscaledTime + 0.5f; pollEnd = Time.unscaledTime + 9f; seen.Clear(); seenNames.Clear(); found.Clear(); Log.Write($"scan: B released after {held:0.0}s"); }
            }
            if (down && heldSince >= 0 && Time.unscaledTime > diagAt)
            {
                diagAt = Time.unscaledTime + 1f;
                var sy = PlayerScanningSystem.mainSystem; var sh = Ship.PlayerShip;
                float sp = -1f; try { sp = sh.physics.rbody.velocity.magnitude; } catch { }
                Log.Write($"scan hold {Time.unscaledTime - heldSince:0.0}s: isScanning {(sy != null && sy.isScanning)}, energy {(sh != null ? sh.powerPlantCurrent : -1):0}, speed {sp:0}, cruise {(sh != null && sh.isInCruise)}, autopilot {(sh != null ? sh.autoPilot.ToString() : "")}, focused {MenuNav.Focused()}");
            }
            if (down && heldSince >= 0 && Time.unscaledTime - heldSince > 0.4f && Time.unscaledTime - heldSince < 0.45f)
                Speech.Say("Charging the long range scan. Your ship stops while it charges. Let go of B after about 3 seconds.", false);
            if (checkAt > 0 && Time.unscaledTime > checkAt)
            {
                Collect();
                checkAt = Time.unscaledTime + 0.4f;
                if (Time.unscaledTime > pollEnd || (lastNewAt > 0 && Time.unscaledTime > lastNewAt + 1.5f)) { checkAt = -1f; lastNewAt = -1f; Report(); }
            }
        }

        static int total;
        static void Collect()
        {
            var sys = PlayerScanningSystem.mainSystem;
            var ship = Ship.PlayerShip;
            if (ship == null) return;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<PlayerScanTarget>())
            {
                if (t == null || !t.gameObject.activeInHierarchy || t.trackedTransform == null) continue;
                if (!t.isScanTarget) continue; // 11/27: a plain station ring (no scan colour) came with the pulse
                BaseSolar owner = null; try { owner = t.trackedTransform.GetComponentInParent<BaseSolar>(); } catch { }
                // 11/27: one wreck gave three rings (three parts): one entry per name.
                string key = owner != null ? Flight.SolarName(owner) : t.trackedTransform.name;
                if (!seenNames.Add(key)) continue;
                lastNewAt = Time.unscaledTime;
                total++;
                var tt = t.trackedTransform;
                BaseSolar solar = null;
                try { solar = tt.GetComponentInParent<BaseSolar>(); } catch { }
                string mat = t.overrideMaterial != null ? t.overrideMaterial.name : "none";
                string kind = "signal"; int rank = 2;
                if (sys != null && t.overrideMaterial != null)
                {
                    if (sys.pointOfInterestMaterial != null && t.overrideMaterial.name == sys.pointOfInterestMaterial.name) { kind = "gold point of interest"; rank = 0; }
                    else if (sys.alreadyScannedMaterial != null && t.overrideMaterial.name == sys.alreadyScannedMaterial.name) { kind = "already scanned"; rank = 3; }
                    else if (sys.wreckMaterial != null && t.overrideMaterial.name == sys.wreckMaterial.name) { kind = "wreck"; rank = 1; }
                    else if (sys.jumpMaterial != null && t.overrideMaterial.name == sys.jumpMaterial.name) { kind = "jumphole"; rank = 1; }
                }
                float d = Vector3.Distance(ship.transform.position, tt.position);
                Flight.Angles(tt.position, out float h, out float v);
                string name = solar != null ? Flight.SolarName(solar) : MenuNav.StripTags(tt.name);
                string dir = DirectionOf(h, v);
                Log.Write($"scan target: {name}, kind {kind}, material {mat}, color {t.overrideColor}, isScanTarget {t.isScanTarget}, solar {(solar != null)}, {d:0}m, {dir}");
                found.Add((d, $"{kind}: {name}, {Flight.Distance(d)}, {dir}", solar, rank));
            }
        }

        static void Report()
        {
            var sys = PlayerScanningSystem.mainSystem;
            float range = 0f; try { range = sys != null ? sys.getLongRangeScanRange() : 0f; } catch { }
            int all = -1, active = -1, loaded = -1;
            try { all = Resources.FindObjectsOfTypeAll<PlayerScanTarget>().Length; active = UnityEngine.Object.FindObjectsOfType<PlayerScanTarget>().Length; loaded = sys != null && sys.loadedData != null ? sys.loadedData.Count : -1; } catch { }
            Log.Write($"scan report: {total} rings, range {range:0}, PlayerScanTarget all {all} active {active}, loadedData {loaded}, isScanning {(sys != null && sys.isScanning)}"); total = 0;
            if (found.Count == 0) { Speech.Say("Scan found nothing within " + Flight.Distance(range > 0 ? range : 50000f) + ". Sometimes the first scan misses: hold B for 3 seconds again."); return; }
            found.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.d.CompareTo(b.d));
            var parts = new List<string>();
            for (int i = 0; i < found.Count && i < 4; i++) parts.Add(found[i].line);
            string line = $"Scan found {found.Count}. " + string.Join(". ", parts) + ".";
            var best = found.Find(f => f.rank == 0 && f.s != null);
            if (best.s != null) { Flight.SelectFromMission(best.s); line += " The nearest point of interest is selected: F2 flies there."; }
            else if (found[0].rank == 0) line += " Steer toward it with the arrow keys; it has no target to select.";
            else
            {
                var first = found.Find(f => f.s != null);
                if (first.s != null) { Flight.SelectFromMission(first.s); line += $" {Flight.SolarName(first.s)} is selected: F2 flies there."; }
            }
            Speech.Say(line);
        }

        static string DirectionOf(float h, float v)
        {
            float m = Mathf.Abs(h);
            if (m < 8 && Mathf.Abs(v) < 8) return "dead ahead";
            string side = h > 0 ? "right" : "left";
            string d = m < 20 ? "ahead" : m < 60 ? "ahead " + side : m < 120 ? "to your " + side : m < 160 ? "behind, " + side : "behind you";
            if (v > 25) d += ", above"; else if (v < -25) d += ", below";
            return d;
        }
    }
}
