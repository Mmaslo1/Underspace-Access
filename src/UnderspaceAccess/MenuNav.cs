using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderspaceAccess
{
    // Keyboard menu reader. Collects every visible, clickable UI element on screen,
    // sorts them in reading order and lets the player walk them with the arrows.
    //   Up/Down: previous/next item. Home/End: first/last. Page Up/Down: 10 items.
    //   Enter or Space: click. Left/Right: change slider, dropdown or on/off.
    //   F11: repeat item and its description. F10: read the other text on screen.
    //   Shift+F11: write a UI dump to the log (diagnostics). (The game itself uses F1 to F9.)
    internal static class MenuNav
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        internal const int VK_RETURN = 0x0D, VK_SPACE = 0x20, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24,
                  VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_SHIFT = 0x10,
                  VK_F10 = 0x79, VK_F11 = 0x7A, VK_ESCAPE = 0x1B;
        static readonly int[] watched = { VK_ESCAPE, VK_RETURN, VK_SPACE, VK_PRIOR, VK_NEXT, VK_END, VK_HOME, VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN, VK_F10, VK_F11 };
        static readonly bool[] wasDown = new bool[256];
        static readonly float[] repeatAt = new float[256];

        static List<Selectable> items = new();
        static Selectable current;
        static GameObject hovered;
        static float nextScan;
        static string lastSignature = "";
        static bool active;
        static int reannounceFrames;
        static string lastState;
        static bool editing;

        internal static bool Active => active;
        internal static bool Focused() => GameHasFocus();
        static bool inGameSaid;
        static float inGameAt = -1f;
        public static void SceneLoaded(string name) { if (name.StartsWith("Title")) inGameSaid = false; }

        public static void Update()
        {
            try { Tick(); }
            catch (Exception e) { Log.Write("MenuNav error: " + e); }
        }

        static void Tick()
        {
            // Flying in space keeps the cursor hidden but only confined (seen 10/25), which is not a menu.
            // 10/25 6:46: in space the lock also switches to None with the cursor still hidden, so in
            // space only a visible cursor means a menu.
            bool menuMode = Cursor.visible || (Cursor.lockState == CursorLockMode.None && !Flight.InSpace);
            if (menuMode != active)
            {
                active = menuMode;
                Log.Write("menu mode " + (active ? "on" : "off") + $" (cursor visible {Cursor.visible}, lock {Cursor.lockState})");
                if (!active) { current = null; lastSignature = ""; }
                inGameAt = !active && !inGameSaid ? Time.unscaledTime + 1.5f : -1f;
            }
            // Menus closed for good (not just between two screens): the game itself is running.
            if (!active && inGameAt > 0 && Time.unscaledTime >= inGameAt)
            {
                inGameAt = -1f;
                // In space Flight gives its own help; this walking help cut it off (11/25).
                if (Flight.InSpace) { }
                else if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("Title"))
                {
                    inGameSaid = true;
                    Speech.Say("You are in the game. F10: where you are. Page Up or Page Down: places to walk to, then Up and Down arrows. Walk with W A S D, turn with Q and E. Shift F10: all keys, also written to the file Underspace Controls in Downloads. Escape: pause menu. " + InGame.RoomServiceKeys());
                }
            }

            bool focused = GameHasFocus();
            if (focused && Down(VK_SHIFT) && Pressed(VK_F11)) Dump();
            if (!active) return;

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.15f;
                Rescan();
            }

            // Unity's own keyboard handling sends Enter and the arrows to whatever was clicked
            // last, which would act a second time on top of the mod. Keep that selection empty,
            // except for a text field the player is typing in.
            var esys = EventSystem.current;
            if (esys != null && esys.currentSelectedGameObject != null && esys.currentSelectedGameObject.GetComponent<TMP_InputField>() == null)
                esys.SetSelectedGameObject(null);

            // A text field being typed in owns the keyboard.
            var field = current != null ? current.TryCast<TMP_InputField>() : null;
            if (field != null && field.isFocused) { editing = true; return; }
            if (editing) { editing = false; if (current != null) Speech.Say(Describe(current)); }

            if (reannounceFrames > 0 && --reannounceFrames == 0 && current != null)
            {
                string s = StateOf(current);
                if (s != lastState) Speech.Say(s);
                lastState = s;
            }

            SaveCheck();
            if (escapeCheckAt > 0 && Time.unscaledTime >= escapeCheckAt) { Rescan(); EscapeFallback(); }
            HoverCheck();
            TimedSpeech();
            if (capturing != null) { CaptureTick(); return; } // the next key press belongs to the game
            if (bindClickPending)
            {
                if (Down(VK_RETURN) || Down(VK_SPACE)) return;
                bindClickPending = false;
                if (current != null) Activate();
                return;
            }

            if (!focused) return;
            if (ManualReader.Tick(items)) return;
            if (Trade.KeyCheck(current)) return;
            if (Pressed(VK_ESCAPE)) Escape();
            else if (Pressed(VK_DOWN, true)) Move(1);
            else if (Pressed(VK_UP, true)) Move(-1);
            else if (Pressed(VK_NEXT, true)) Move(10);
            else if (Pressed(VK_PRIOR, true)) Move(-10);
            else if (Pressed(VK_HOME)) MoveTo(0);
            else if (Pressed(VK_END)) MoveTo(items.Count - 1);
            else if (Pressed(VK_RETURN) || Pressed(VK_SPACE)) Activate();
            else if (Pressed(VK_RIGHT, true)) Adjust(1);
            else if (Pressed(VK_LEFT, true)) Adjust(-1);
            else if (Pressed(VK_F11)) { if (current != null) { Speech.Say(Describe(current)); var d = Trade.Details(current) ?? DescriptionOf(current); if (d != null) Speech.Say(d, false); } else Speech.Say("Nothing to select."); }
            else if (Pressed(VK_F10)) Speech.Say(ScreenText());
        }

        // ---------- collecting items ----------

        static void Rescan()
        {
            var found = new List<Selectable>();
            var es = EventSystem.current;
            var dropdownList = OpenDropdownList();

            foreach (var s in Selectable.allSelectablesArray)
            {
                if (s == null || !s.isActiveAndEnabled || !s.IsInteractable()) continue;
                if (s.TryCast<Scrollbar>() != null) continue;
                if (Skipped(s)) continue;
                if (dropdownList != null && !s.transform.IsChildOf(dropdownList)) continue;
                if (!Visible(s, es)) continue;
                found.Add(s);
            }
            // A number option shows a slider and an edit box for the same value: keep the slider only.
            found.RemoveAll(s =>
            {
                if (s.TryCast<TMP_InputField>() == null) return false;
                var ob = s.GetComponentInParent<OptionsButton>();
                return ob != null && ob.linkedSlider != null && ob.linkedSlider.isActiveAndEnabled;
            });
            items = CharacterCreationOrder(ReadingOrder(found));

            string sig = string.Join(",", items.Select(i => i.GetInstanceID()));
            bool currentValid = current != null && items.Contains(current);

            if (dropdownList != null) holdUntil = Time.unscaledTime + 1f;
            // A dropdown list just closed: back to its dropdown, with the value now chosen.
            if (openedFrom != null && dropdownList == null)
            {
                if (items.Contains(openedFrom))
                {
                    var dd = openedFrom;
                    openedFrom = null;
                    lastSignature = sig;
                    Focus(dd);
                    Speech.Say(Describe(dd));
                    return;
                }
                if (Time.unscaledTime < holdUntil) return; // list still fading out
                Log.Write("dropdown not found after its list closed");
                openedFrom = null;
            }

            if (sig != lastSignature || (!currentValid && items.Count > 0))
            {
                if (sig != lastSignature) Log.Write($"screen changed: {items.Count} items, current {(currentValid ? "kept" : "lost")}, list {(dropdownList != null ? "open" : "closed")}, from-dropdown {(openedFrom != null)}");
                lastSignature = sig;
                if (!currentValid && dropdownList != null)
                {
                    // List just opened: start on the value already chosen.
                    var chosen = items.FirstOrDefault(i => i.TryCast<Toggle>() != null && i.TryCast<Toggle>().isOn) ?? items.FirstOrDefault();
                    if (chosen != null) { Focus(chosen); Speech.Say(Describe(chosen) + $", {items.IndexOf(chosen) + 1} of {items.Count}"); }
                    return;
                }
                if (!currentValid)
                {
                    // Same option, new control (an on/off switch swaps its button when clicked).
                    var sameRow = SameRow(current);
                    if (sameRow != null)
                    {
                        Focus(sameRow);
                        reannounceFrames = 0;
                        Speech.Say(StateOf(sameRow));
                        return;
                    }
                    current = null;
                    if (items.Count > 0)
                    {
                        // Back on an earlier screen: return to the item last used there.
                        var back = history.LastOrDefault(id => items.Any(i => i.GetInstanceID() == id));
                        var target = back != 0 ? items.First(i => i.GetInstanceID() == back) : items[0];
                        Focus(target);
                        Speech.Say(Describe(current) + (items.Count > 1 ? $", {items.IndexOf(target) + 1} of {items.Count}" : ""));
                        // A new screen: also read its heading and explanation texts.
                        var info = ScreenInfo();
                        if (!string.IsNullOrEmpty(info)) { Speech.Say(info, false); spokenInfo = info; InGame.Remember(info); }
                    }
                }
            }
        }

        static readonly List<int> history = new();
        static OptionsButton currentRow;
        static TMP_Dropdown openedFrom;
        static float holdUntil;

        // Texts of the current screen that are not menu items, list rows or option names:
        // a panel heading, a disclaimer, an explanation.
        static string ScreenInfo()
        {
            if (items.Count == 0) return null;
            Transform common = items[0].transform.parent;
            while (common != null && !items.All(s => s.transform.IsChildOf(common))) common = common.parent;
            if (common == null) return null;
            var labels = new HashSet<string>(items.Select(LabelOf));
            var parts = new List<string>();
            foreach (var t in common.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!t.isActiveAndEnabled || t.color.a < 0.05f) continue;
                if (t.GetComponentInParent<Selectable>() != null || t.GetComponentInParent<OptionsButton>() != null) continue;
                if (MaskOf(t.transform) != null) continue;
                // The options explanation box still holds the last hovered option's text.
                if (OptionsMenu.mainMenu != null && OptionsMenu.mainMenu.functionExplanationText != null
                    && t.Pointer == OptionsMenu.mainMenu.functionExplanationText.Pointer) continue;
                var s = StripTags(t.text);
                if (labels.Contains(s)) continue;
                s = Useful(s);
                // Long texts (tutorial boxes, dialogue history) are said by InGame or F10.
                if (s == null || s.Length > 300 || parts.Contains(s) || InGame.SaidRecently(s)) continue;
                parts.Add(s);
            }
            var text = string.Join(". ", parts);
            return text.Length > 600 ? text.Substring(0, 600) : text;
        }

        // Key binding buttons: after a click the game waits for the new key.
        static bool bindClickPending;
        static Selectable capturing;
        static string captureBefore, captureSignature;
        static float captureUntil;

        static bool IsKeyBinding(Selectable s) => s.gameObject.name.StartsWith("BaseKeyboardKey");

        static void CaptureTick()
        {
            if (capturing == null) return;
            // The game may follow up with its own question (Press, Hold or Double tap):
            // a new screen ends the wait and is read like any other.
            if (lastSignature != captureSignature) { capturing = null; return; }
            string now = TextsUnder(capturing.transform, null);
            if (now == "(?)") return;
            if (now != captureBefore && !string.IsNullOrWhiteSpace(now) && !now.StartsWith("Press", StringComparison.OrdinalIgnoreCase) && now != "...")
            {
                capturing = null;
                Speech.Say("Set to " + now);
                return;
            }
            if (Time.unscaledTime > captureUntil)
            {
                capturing = null;
                Speech.Say("No key pressed. " + Describe(current));
            }
        }

        static Selectable SameRow(Selectable old)
        {
            if (currentRow == null) return null;
            try
            {
                foreach (var s in items)
                {
                    var ob = s.GetComponentInParent<OptionsButton>();
                    if (ob != null && ob.Pointer == currentRow.Pointer) return s;
                }
            }
            catch { }
            return null;
        }

        // Items are grouped by panel (for example the tab column, then the option list),
        // panels ordered by their first item, items top to bottom and left to right inside.
        static List<Selectable> ReadingOrder(List<Selectable> found)
        {
            if (found.Count < 2) return found;
            var pos = found.ToDictionary(s => s.GetInstanceID(), s => ScreenPos(s));
            Comparison<Selectable> byPos = (a, b) =>
            {
                var pa = pos[a.GetInstanceID()]; var pb = pos[b.GetInstanceID()];
                if (Mathf.Abs(pa.y - pb.y) > 12f) return pb.y.CompareTo(pa.y);
                return pa.x.CompareTo(pb.x);
            };
            Transform common = found[0].transform.parent;
            while (common != null && !found.All(s => s.transform.IsChildOf(common))) common = common.parent;
            if (common == null) { found.Sort(byPos); return found; }

            var groups = new Dictionary<int, List<Selectable>>();
            foreach (var s in found)
            {
                var t = s.transform;
                while (t.parent != null && t.parent != common) t = t.parent;
                int key = t.GetInstanceID();
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<Selectable>();
                g.Add(s);
            }
            var ordered = groups.Values.ToList();
            foreach (var g in ordered)
            {
                // Inside a panel, the scrolling list comes first, then its fixed buttons
                // (Restore Defaults and the like), so the list is not cut in two.
                g.Sort((a, b) =>
                {
                    bool ma = MaskOf(a.transform) != null, mb = MaskOf(b.transform) != null;
                    return ma != mb ? (ma ? -1 : 1) : byPos(a, b);
                });
            }
            // Tab rows go first, so a screen always starts with its tabs.
            ordered.Sort((a, b) =>
            {
                bool ta = IsTabGroup(a[0]), tb = IsTabGroup(b[0]);
                return ta != tb ? (ta ? -1 : 1) : byPos(a[0], b[0]);
            });
            return ordered.SelectMany(g => g).ToList();
        }

        static bool IsTabGroup(Selectable s)
        {
            var p = s.transform.parent;
            return p != null && p.name.IndexOf("Tab", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Escape: the game handles it on some screens. When nothing changed shortly after,
        // the mod presses the screen's own Return/Back/Close button instead.
        static float escapeCheckAt = -1f;
        static string escapeSignature;

        // 11/27: the game's developer command box ("Input Text") opened after a load and Escape
        // could not close it ("no back button"). Escape now closes it with the game's own call.
        internal static bool ConsoleOpen()
        {
            try { var c = DeveloperConsole.main; return c != null && c.consoleMenu != null && c.consoleMenu.activeInHierarchy; } catch { return false; }
        }

        static void Escape()
        {
            if (ConsoleOpen())
            {
                try { DeveloperConsole.main.closeConsole(); } catch (Exception e) { Log.Write("closeConsole failed: " + e.Message); }
                try { if (DeveloperConsole.main.consoleMenu.activeInHierarchy) DeveloperConsole.main.consoleMenu.SetActive(false); } catch { }
                try { UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null); } catch { }
                Log.Write("escape: closed the developer console");
                Speech.Say("Command box closed.");
                return;
            }
            var dd = items.Count > 0 ? OpenDropdownOwner() : null;
            if (dd != null) { dd.Hide(); openedFrom = dd; holdUntil = Time.unscaledTime + 1f; return; }
            escapeSignature = lastSignature;
            escapeCheckAt = Time.unscaledTime + 0.3f;
            Log.Write("escape pressed");
        }

        static void EscapeFallback()
        {
            escapeCheckAt = -1f;
            if (lastSignature != escapeSignature) return; // the game reacted
            // Only a screen's own fixed buttons count, never a list row or a key binding
            // (the Controls list has "Cancel Autopilot").
            var back = items.LastOrDefault(s => !IsKeyBinding(s) && MaskOf(s.transform) == null && IsBackLabel(LabelOf(s)));
            if (back == null) { Log.Write("escape: no back button found"); return; }
            Log.Write("escape fallback clicks " + PathOf(back.transform));
            current = back;
            Speech.Say(LabelOf(back));
            Activate();
        }

        static readonly Regex backWords = new(@"^(return|back|go back|close|cancel|done|exit menu|resume|resume game|continue)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static bool IsBackLabel(string label) => !string.IsNullOrEmpty(label) && backWords.IsMatch(label.Trim());

        static TMP_Dropdown OpenDropdownOwner()
        {
            foreach (var dd in UnityEngine.Object.FindObjectsOfType<TMP_Dropdown>())
                if (dd != null && dd.IsExpanded) return dd;
            return null;
        }

        static Transform OpenDropdownList()
        {
            foreach (var dd in UnityEngine.Object.FindObjectsOfType<TMP_Dropdown>())
            {
                if (dd == null || !dd.IsExpanded) continue;
                var canvas = dd.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.rootCanvas.transform : dd.transform.root;
                var list = FindChild(root, "Dropdown List");
                if (list != null) return list;
            }
            return null;
        }

        static Transform FindChild(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name == name && c.gameObject.activeInHierarchy) return c;
                var r = FindChild(c, name);
                if (r != null) return r;
            }
            return null;
        }

        static bool Visible(Selectable s, EventSystem es)
        {
            var rt = s.transform.TryCast<RectTransform>();
            if (rt == null) return false;
            foreach (var g in s.GetComponentsInParent<CanvasGroup>())
            {
                if (g.alpha < 0.05f) return false;
                if (g.ignoreParentGroups) break;
            }
            var canvas = s.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled) return false;

            // Items inside a masked scroll area are tested at the centre of the mask,
            // so the ones scrolled out of view still count.
            Transform hitRoot = s.transform;
            Vector2 point = ScreenPos(s);
            var mask = MaskOf(s.transform);
            if (mask != null)
            {
                hitRoot = mask;
                point = ScreenPos(mask.TryCast<RectTransform>(), canvas);
            }
            if (point.x < 0 || point.y < 0 || point.x > Screen.width || point.y > Screen.height) return false;
            if (es == null) return true;
            if (s.GetComponent<TutorialButton>() != null) return true; // Manual entries have no graphic of their own to hit

            var ped = new PointerEventData(es) { position = point };
            var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(ped, results);
            if (results.Count == 0) return mask != null; // nothing blocks, but the item has no graphic hit either
            var top = results[0].gameObject;
            return top != null && (top.transform.IsChildOf(hitRoot) || (mask != null && top.transform.IsChildOf(s.transform)));
        }

        static Transform MaskOf(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<RectMask2D>() != null || p.GetComponent<Mask>() != null) return p;
                if (p.GetComponent<Canvas>() != null) return null;
            }
            return null;
        }

        static Vector2 ScreenPos(Selectable s) => ScreenPos(s.transform.TryCast<RectTransform>(), s.GetComponentInParent<Canvas>());

        static Vector2 ScreenPos(RectTransform rt, Canvas canvas)
        {
            if (rt == null) return new Vector2(-1, -1);
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * 0.5f;
            Camera cam = null;
            if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.rootCanvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(cam, center);
        }

        // ---------- moving ----------

        static void Move(int delta)
        {
            if (items.Count == 0) { Speech.Say("No menu items."); return; }
            int i = current != null ? items.IndexOf(current) : -1;
            int n = i < 0 ? (delta > 0 ? 0 : items.Count - 1) : i + delta;
            if (n < 0 || n >= items.Count)
            {
                n = Mathf.Clamp(n, 0, items.Count - 1);
                if (n == i) { Speech.Say(n == 0 ? "Top. " + Describe(current) : "Bottom. " + Describe(current)); return; }
            }
            MoveTo(n);
        }

        static void MoveTo(int n)
        {
            if (items.Count == 0) { Speech.Say("No menu items."); return; }
            Focus(items[Mathf.Clamp(n, 0, items.Count - 1)]);
            Speech.Say(Describe(current));
        }

        static void Focus(Selectable s)
        {
            current = s;
            currentRow = s.GetComponentInParent<OptionsButton>();
            int id = s.GetInstanceID();
            history.Remove(id);
            history.Add(id);
            if (history.Count > 40) history.RemoveAt(0);
            lastState = StateOf(s);
            ScrollIntoView(s);
            var es = EventSystem.current;
            if (es == null) return;
            if (hovered != null && hovered != s.gameObject)
                ExecuteEvents.Execute(hovered, new PointerEventData(es), ExecuteEvents.pointerExitHandler);
            hovered = s.gameObject;
            hoverBefore = SnapshotTexts();
            hoverCheckFrames = 4;
            afterClick = false;
            var ped = new PointerEventData(es) { position = ScreenPos(s) };
            ExecuteEvents.Execute(s.gameObject, ped, ExecuteEvents.pointerEnterHandler);
        }

        // Hovering an item often makes the game show an explanation or tooltip somewhere
        // on screen. Whatever text appears or changes then is read after the item.
        static string spokenInfo;
        static Dictionary<int, string> hoverBefore;
        static int hoverCheckFrames;
        static bool afterClick;

        static Dictionary<int, string> SnapshotTexts()
        {
            var d = new Dictionary<int, string>();
            foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
            {
                if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f) continue;
                if (t.GetComponentInParent<Selectable>() != null) continue;
                bool hidden = false;
                foreach (var g in t.GetComponentsInParent<CanvasGroup>()) if (g.alpha < 0.05f) { hidden = true; break; }
                if (hidden) continue;
                var txt = StripTags(t.text);
                if (!Junk(txt)) d[t.GetInstanceID()] = txt;
            }
            return d;
        }

        static void HoverCheck()
        {
            if (hoverCheckFrames <= 0 || --hoverCheckFrames > 0 || hoverBefore == null || current == null) return;
            var after = SnapshotTexts();
            var label = LabelOf(current);
            var news = after.Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && kv.Value != label
                                          && (!hoverBefore.TryGetValue(kv.Key, out var old) || old != kv.Value)
                                          && (spokenInfo == null || !spokenInfo.Contains(kv.Value)))
                            .Select(kv => kv.Value).Distinct().ToList();
            hoverBefore = null;
            spokenInfo = null;
            // Game options carry their own description: read it after the item.
            // The game's pointer hints ("[X] Buy One. [A] Buy All") name its mouse keys; the mod says its own.
            if (Trade.Open) news.RemoveAll(n => n.Contains("] Buy") || n.Contains("] Sell") || n.Contains("] Unequip") || n.Contains("] Equip"));
            var desc = afterClick ? null : DescriptionOf(current, false);
            if (desc != null && !news.Contains(desc)) news.Add(desc);
            if (afterClick)
            {
                afterClick = false;
                // Counters like the play time change every second: not worth reading.
                news.RemoveAll(n => n.StartsWith("Playtime") || n == StateOf(current));
                if (news.Count > 0) Log.Write("after click: " + string.Join(" | ", news));
            }
            news = news.Select(Useful).Where(n => n != null && !InGame.SaidRecently(n)).ToList();
            if (news.Count > 0 && news.Count <= 3) { var line = string.Join(". ", news); InGame.Remember(line); Speech.Say(line, false); }
        }

        static void ScrollIntoView(Selectable s)
        {
            var mask = MaskOf(s.transform)?.TryCast<RectTransform>();
            if (mask == null) return;
            var sr = s.GetComponentInParent<ScrollRect>();
            var maskCorners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            var itemCorners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            mask.GetWorldCorners(maskCorners);
            s.transform.TryCast<RectTransform>().GetWorldCorners(itemCorners);
            float above = itemCorners[1].y - maskCorners[1].y; // item top over mask top
            float below = maskCorners[0].y - itemCorners[0].y; // item bottom under mask bottom
            float shift = above > 0 ? -above : below > 0 ? below : 0;
            if (shift == 0) return;
            RectTransform content = sr != null ? sr.content : null;
            if (content == null)
            {
                for (var t = s.transform; t != null && t.parent != null; t = t.parent)
                    if (t.parent == mask.transform) { content = t.TryCast<RectTransform>(); break; }
            }
            if (content == null) return;
            content.position += new Vector3(0, shift, 0);
            // Keep the game's own scrollbar in step with the new position, if it has one.
            if (sr != null && sr.verticalScrollbar != null) sr.verticalNormalizedPosition = sr.verticalNormalizedPosition;
        }

        // ---------- acting ----------

        static void Activate()
        {
            if (current == null) { Speech.Say("Nothing selected."); return; }
            var es = EventSystem.current;
            var input = current.TryCast<TMP_InputField>();
            if (input != null)
            {
                input.Select();
                input.ActivateInputField();
                Speech.Say("Editing. Type, then press Enter.");
                return;
            }
            var dd = current.TryCast<TMP_Dropdown>();
            if (dd != null) { dd.Show(); openedFrom = dd; holdUntil = Time.unscaledTime + 1f; return; }
            var slider = current.TryCast<Slider>();
            if (slider != null) { Speech.Say(Describe(current)); return; }
            if (CustomRow(current) != null) { StepCustomRow(1); return; }
            var tb = current.GetComponent<TutorialButton>();
            if (tb != null)
            {
                Log.Write("manual entry " + PathOf(current.transform) + " : " + Describe(current));
                tb.showTutorial();
                manualTextAt = Time.unscaledTime + 0.4f;
                return;
            }
            // A key binding listens for the next key at once: click only after Enter is let go,
            // or the game would take Enter itself as the new key.
            if (IsKeyBinding(current) && (Down(VK_RETURN) || Down(VK_SPACE))) { bindClickPending = true; return; }
            var beforeClick = SnapshotTexts();
            if (es != null)
            {
                var ped = new PointerEventData(es) { position = ScreenPos(current), button = PointerEventData.InputButton.Left, clickCount = 1 };
                ExecuteEvents.Execute(current.gameObject, ped, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(current.gameObject, ped, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(current.gameObject, ped, ExecuteEvents.pointerClickHandler);
            }
            Log.Write("click " + PathOf(current.transform) + " : " + Describe(current));
            if (PathOf(current.transform).EndsWith("SaveFileButtons/Save")) SaveClicked();
            reannounceFrames = 3;
            // A click can make the game show a message (for example why Create did not work):
            // read texts that appear or change in the next moment.
            if (!IsKeyBinding(current)) { hoverBefore = beforeClick; hoverCheckFrames = 20; afterClick = true; }
            if (IsKeyBinding(current))
            {
                capturing = current;
                captureSignature = lastSignature;
                reannounceFrames = 0;
                captureBefore = TextsUnder(current.transform, null);
                captureUntil = Time.unscaledTime + 8f;
                Speech.Say("Press the new key.");
            }
            else if (openedFrom != null) holdUntil = Time.unscaledTime + 1f; // picked a value in an open list
        }

        static void Adjust(int dir)
        {
            if (current == null) return;
            if (InDropdownList(current)) { Move(dir); return; }
            if (CustomRow(current) != null) { StepCustomRow(dir); return; }
            var slider = current.TryCast<Slider>();
            if (slider != null)
            {
                float step = slider.wholeNumbers ? 1f : (slider.maxValue - slider.minValue) / 20f;
                if (Down(VK_SHIFT)) step *= slider.wholeNumbers ? 10f : 0.2f;
                slider.value = Mathf.Clamp(slider.value + dir * step, slider.minValue, slider.maxValue);
                Speech.Say(ValueOf(slider, current));
                return;
            }
            var dd = current.TryCast<TMP_Dropdown>();
            if (dd != null && dd.options.Count > 0)
            {
                dd.value = Mathf.Clamp(dd.value + dir, 0, dd.options.Count - 1);
                dd.RefreshShownValue();
                Speech.Say(StripTags(dd.options[dd.value].text));
                descriptionAt = Time.unscaledTime + 0.3f; // the race explanation changes with the race
                return;
            }
            var toggle = current.TryCast<Toggle>();
            if (toggle != null)
            {
                bool want = dir > 0;
                if (toggle.isOn != want) toggle.isOn = want;
                Speech.Say(toggle.isOn ? "on" : "off");
                return;
            }
            var onOff = OnOff(current);
            if (onOff != null)
            {
                // Right turns on, Left turns off; already there just says so.
                if (onOff.Value != dir > 0) Activate();
                else Speech.Say(onOff.Value ? "on" : "off");
                return;
            }
            Speech.Say(Describe(current));
        }

        // ---------- describing ----------

        public static string Describe(Selectable s)
        {
            var trade = Trade.Describe(s);
            if (trade != null) return trade;
            string label = LabelOf(s);
            string state = StateOf(s);
            string kind = KindOf(s);
            var parts = new List<string> { label };
            if (!string.IsNullOrEmpty(kind)) parts.Add(kind);
            if (!string.IsNullOrEmpty(state)) parts.Add(state);
            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        static string KindOf(Selectable s)
        {
            if (CustomRow(s) != null) return customHintGiven ? "" : HintOnce();
            if (s.TryCast<Slider>() != null) return "slider";
            if (s.TryCast<TMP_Dropdown>() != null) return "dropdown";
            if (s.TryCast<TMP_InputField>() != null) return "edit";
            if (s.TryCast<Toggle>() != null) return InDropdownList(s) ? "" : "checkbox";
            if (OnOff(s) != null) return "switch";
            return "";
        }

        static string StateOf(Selectable s)
        {
            var row = CustomRow(s);
            if (row != null)
            {
                string num = row.bodyPartNumber != null ? StripTags(row.bodyPartNumber.text) : (row.currentBodyPart + 1).ToString();
                int count = row.bodyPartsAvailable != null ? row.bodyPartsAvailable.Count : 0;
                string pos = count > 1 && int.TryParse(num, out _) ? $"{num} of {count}" : num;
                string colour = ColorNames.Of(row);
                return colour != null ? colour + ", " + pos : pos;
            }
            if (IsRandomName(s)) return LoginUI.mainUI != null && LoginUI.mainUI.characterName != null ? "now " + LoginUI.mainUI.characterName.text : "";
            var slider = s.TryCast<Slider>();
            if (slider != null) return ValueOf(slider, s);
            var dd = s.TryCast<TMP_Dropdown>();
            if (dd != null) return dd.options.Count > dd.value && dd.value >= 0 ? StripTags(dd.options[dd.value].text) : "";
            var input = s.TryCast<TMP_InputField>();
            if (input != null) return string.IsNullOrEmpty(input.text) ? "blank" : input.text;
            var toggle = s.TryCast<Toggle>();
            if (toggle != null && InDropdownList(s)) return toggle.isOn ? "selected" : "";
            if (toggle != null) return toggle.isOn ? "checked" : "not checked";
            if (IsKeyBinding(s)) return TextsUnder(s.transform, null);
            var oo = OnOff(s);
            if (oo != null) return oo.Value ? "on" : "off";
            return RowContext(s);
        }

        static string ValueOf(Slider slider, Selectable s)
        {
            var ob = s.GetComponentInParent<OptionsButton>();
            if (ob != null && ob.linkedInputField != null && !string.IsNullOrWhiteSpace(ob.linkedInputField.text))
                return ob.linkedInputField.text;
            float v = slider.value;
            if (slider.wholeNumbers) return ((int)v).ToString();
            if (slider.minValue == 0f && slider.maxValue == 1f) return Mathf.RoundToInt(v * 100f) + " percent";
            return v.ToString("0.##");
        }

        // The game's own on/off options show one of two objects instead of a Toggle.
        static bool? OnOff(Selectable s)
        {
            var ob = s.GetComponentInParent<OptionsButton>();
            if (ob == null || ob.linkedSlider != null || ob.linkedDropdown != null) return null;
            if (ob.linkedToggleOn == null && ob.linkedToggleOff == null) return null;
            if (ob.linkedToggleOn != null && ob.linkedToggleOn.activeInHierarchy) return true;
            if (ob.linkedToggleOff != null && ob.linkedToggleOff.activeInHierarchy) return false;
            return null;
        }

        static bool InDropdownList(Selectable s)
        {
            for (var p = s.transform.parent; p != null; p = p.parent)
                if (p.name == "Dropdown List") return true;
            return false;
        }

        static string LabelOf(Selectable s)
        {
            var custom = CustomRow(s);
            if (custom != null && custom.bodyPartName != null) return StripTags(custom.bodyPartName.text);
            if (IsRandomName(s)) return "Random name";
            var tb = s.GetComponent<TutorialButton>();
            if (tb != null && tb.tutorialName != null) return StripTags(tb.tutorialName.text) + (tb.isCategory ? ", topic" : "");
            var login = LoginUI.mainUI;
            if (login != null && login.characterName != null && s.Pointer == login.characterName.Pointer) return "Name";
            if (InDropdownList(s)) return TextsUnder(s.transform, null);
            if (IsKeyBinding(s) && s.transform.parent != null)
            {
                // The action name sits on the row itself, e.g. "Accelerate", with two key buttons.
                var row = s.transform.parent.GetComponent<TMP_Text>();
                string action = row != null ? StripTags(row.text) : CleanName(s.transform.parent.name.Replace("ControllerButton", ""));
                bool alt = s.gameObject.name.EndsWith("Alt");
                return action + (alt ? ", alternate key" : ", key");
            }
            var ob = s.GetComponentInParent<OptionsButton>();
            if (ob != null)
            {
                string n = ob.optionsButtonName != null ? StripTags(ob.optionsButtonName.text) : "";
                if (string.IsNullOrWhiteSpace(n) && ob.linkedNode != null) n = ob.linkedNode.optionName;
                if (!string.IsNullOrWhiteSpace(n))
                {
                    // Colour sliders sit three to an option.
                    if (ob.colorSliderR != null && s.gameObject == ob.colorSliderR.gameObject) n += " red";
                    else if (ob.colorSliderG != null && s.gameObject == ob.colorSliderG.gameObject) n += " green";
                    else if (ob.colorSliderB != null && s.gameObject == ob.colorSliderB.gameObject) n += " blue";
                    return n;
                }
            }
            var dd = s.TryCast<TMP_Dropdown>();
            var input = s.TryCast<TMP_InputField>();
            if (dd == null && input == null && s.TryCast<Slider>() == null)
            {
                string own = TextsUnder(s.transform, null);
                if (!string.IsNullOrWhiteSpace(own)) return Expand(own);
            }
            if (input != null && input.placeholder != null)
            {
                var ph = input.placeholder.TryCast<TMP_Text>();
                if (ph != null && !string.IsNullOrWhiteSpace(ph.text)) return StripTags(ph.text);
            }
            // Fall back to a short text beside the control, then to its object name
            // ("RaceDropdown" becomes "Race").
            if (s.transform.parent != null)
            {
                string near = TextsUnder(s.transform.parent, s.transform);
                if (!string.IsNullOrWhiteSpace(near) && near.Length <= 40) return near;
            }
            var name = Regex.Replace(CleanName(s.gameObject.name), @"\b(Button|Dropdown|Drop Down|Slider|Toggle|Input Field)\b", "", RegexOptions.IgnoreCase);
            name = spaces.Replace(name, " ").Trim();
            return string.IsNullOrEmpty(name) ? CleanName(s.gameObject.name) : name;
        }

        // A button inside a list row (for example Previous and Next beside "Eye Color 1")
        // gets the row's own texts as its state, so a click is heard as the new value.
        static string RowContext(Selectable s)
        {
            var mask = MaskOf(s.transform);
            if (mask == null) return "";
            Transform row = s.transform;
            while (row.parent != null && row.parent.parent != mask) row = row.parent;
            if (row == s.transform || row.parent == null) return "";
            var parts = new List<string>();
            foreach (var t in row.GetComponentsInChildren<TMP_Text>(false))
            {
                if (t.GetComponentInParent<Selectable>() != null && t.GetComponentInParent<Selectable>().transform.IsChildOf(row)) continue;
                AddText(parts, t.text, t.color.a);
            }
            return string.Join(" ", parts);
        }

        static string Expand(string label) => label switch
        {
            "Prev" => "Previous",
            "Next But" => "Next",
            _ => label
        };

        static string TextsUnder(Transform root, Transform skip)
        {
            var parts = new List<string>();
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (skip != null && t.transform.IsChildOf(skip)) continue;
                if (skip != null && t.GetComponentInParent<Selectable>() != null && t.GetComponentInParent<Selectable>().transform != root) continue;
                AddText(parts, t.text, t.color.a);
            }
            foreach (var t in root.GetComponentsInChildren<Text>(false))
            {
                if (skip != null && t.transform.IsChildOf(skip)) continue;
                AddText(parts, t.text, t.color.a);
            }
            return string.Join(", ", parts);
        }

        static void AddText(List<string> parts, string raw, float alpha)
        {
            if (alpha < 0.05f) return;
            var s = StripTags(raw);
            if (string.IsNullOrWhiteSpace(s) || parts.Contains(s)) return;
            parts.Add(s);
        }

        static string DescriptionOf(Selectable s, bool screenToo = true)
        {
            var login = LoginUI.mainUI;
            if (login != null && login.characterRace != null && s.Pointer == login.characterRace.Pointer
                && login.raceDescription != null && !string.IsNullOrWhiteSpace(login.raceDescription.text))
                return StripTags(login.raceDescription.text);
            if (IsRandomName(s)) return "Picks a new random name that fits your race. It replaces the name you typed.";
            var ob = s.GetComponentInParent<OptionsButton>();
            if (ob != null && ob.linkedNode != null && !string.IsNullOrWhiteSpace(ob.linkedNode.optionDescription))
                return StripTags(ob.linkedNode.optionDescription);
            if (screenToo && OptionsMenu.mainMenu != null && OptionsMenu.mainMenu.functionExplanationText != null
                && OptionsMenu.mainMenu.functionExplanationText.isActiveAndEnabled)
            {
                var t = StripTags(OptionsMenu.mainMenu.functionExplanationText.text);
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
            return null;
        }

        // Every visible text on screen that is not part of a menu item, top to bottom.
        static string ScreenText()
        {
            var list = new List<(Vector2 p, string s)>();
            foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
            {
                if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f) continue;
                if (t.GetComponentInParent<Selectable>() != null) continue;
                bool hidden = false;
                foreach (var g in t.GetComponentsInParent<CanvasGroup>()) if (g.alpha < 0.05f) { hidden = true; break; }
                if (hidden) continue;
                var c = t.GetComponentInParent<Canvas>();
                if (c == null || !c.isActiveAndEnabled) continue;
                var s = Useful(StripTags(t.text));
                if (s == null) continue;
                var p = ScreenPos(t.transform.TryCast<RectTransform>(), c);
                if (p.x < 0 || p.y < 0 || p.x > Screen.width || p.y > Screen.height) continue;
                list.Add((p, s));
            }
            list.Sort((a, b) => Mathf.Abs(a.p.y - b.p.y) > 12f ? b.p.y.CompareTo(a.p.y) : a.p.x.CompareTo(b.p.x));
            var seen = new HashSet<string>();
            var text = string.Join(". ", list.Select(x => x.s).Where(seen.Add));
            return string.IsNullOrWhiteSpace(text) ? "No other text on screen." : text;
        }

        // HUD numbers, the version and the play time clock say nothing useful.
        static readonly Regex junk = new(@"^([-+$\d.,:%\s]+|Beta \d.*|Playtime:.*)$", RegexOptions.Compiled);
        internal static bool Junk(string s) => junk.IsMatch(s);

        // 11/3: screens were read with the game's mouse hints ("Select Target: [LMB]", "Dock: [F]"),
        // "Game Saved" and the version. Returns the text worth saying, or null.
        static readonly Regex keyHint = new(@"^(\[[^\]]+\]|[\w ]{1,24}: ?\[[^\]]+\])$", RegexOptions.Compiled);
        internal static string Useful(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || Junk(s) || keyHint.IsMatch(s)) return null;
            if (s == "Game Saved" || s == "Game Saved." || s == "Dialogue History" || s == "FREE FLIGHT") return null;
            s = InGame.DropMouse(s.Replace(" *", " ").TrimStart('*'));
            return s.Length < 3 ? null : s;
        }
        static readonly Regex tags = new("<[^>]*>", RegexOptions.Compiled);
        static readonly Regex spaces = new(@"\s+", RegexOptions.Compiled);
        public static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = tags.Replace(s, "");
            s = s.Replace(' ', ' ').Replace("​", "");
            return spaces.Replace(s, " ").Trim();
        }

        static string CleanName(string n)
        {
            n = Regex.Replace(n ?? "", @"\(Clone\)|\(\d+\)", "");
            n = Regex.Replace(n, @"(?<=[a-z])(?=[A-Z])", " ").Replace('_', ' ');
            return spaces.Replace(n, " ").Trim();
        }

        // ---------- character creation and manual ----------

        // Each colour row of character creation has Previous and Next buttons. The mod shows
        // one item per row, changed with Left and Right, and hides the Previous twin and the
        // camera rotate buttons (they only turn the picture).
        static bool Skipped(Selectable s)
        {
            string n = s.gameObject.name;
            if (n.StartsWith("RotateButton")) return true;
            return n == "PreviousButton" && CustomRow(s) != null;
        }

        static ShipCustomizationButton CustomRow(Selectable s)
        {
            if (s == null) return null;
            string n = s.gameObject.name;
            if (n != "NextButton" && n != "PreviousButton") return null;
            return s.GetComponentInParent<ShipCustomizationButton>();
        }

        static bool IsRandomName(Selectable s) => s != null && s.gameObject.name == "RaceRandomButton";

        static bool customHintGiven;
        static string HintOnce() { customHintGiven = true; return "Left and Right arrows change it"; }

        // The same click Enter gives, for a row the mod acts on itself (X and A in the trade window).
        internal static void ClickRow(Selectable s)
        {
            var es = EventSystem.current;
            if (s == null || es == null) throw new InvalidOperationException("no row or event system");
            var ped = new PointerEventData(es) { position = ScreenPos(s), button = PointerEventData.InputButton.Left, clickCount = 1 };
            ExecuteEvents.Execute(s.gameObject, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(s.gameObject, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(s.gameObject, ped, ExecuteEvents.pointerClickHandler);
        }

        static void StepCustomRow(int dir)
        {
            var row = CustomRow(current);
            if (row == null || row.leftRightButtons == null) return;
            var target = row.leftRightButtons.transform.Find(dir > 0 ? "NextButton" : "PreviousButton");
            var es = EventSystem.current;
            if (target == null || es == null) { Log.Write("custom row: no " + (dir > 0 ? "Next" : "Previous") + " button"); return; }
            var ped = new PointerEventData(es) { position = ScreenPos(current), button = PointerEventData.InputButton.Left, clickCount = 1 };
            ExecuteEvents.Execute(target.gameObject, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target.gameObject, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target.gameObject, ped, ExecuteEvents.pointerClickHandler);
            Log.Write("custom row " + LabelOf(current) + (dir > 0 ? " next" : " previous"));
            lastState = null;
            reannounceFrames = 3;
        }

        // Character creation in a useful order: name, random name, race, ship, colours, then the rest (Create last).
        static List<Selectable> CharacterCreationOrder(List<Selectable> list)
        {
            var login = LoginUI.mainUI;
            if (login == null || login.characterName == null || !list.Any(i => i.Pointer == login.characterName.Pointer)) return list;
            int Rank(Selectable s)
            {
                if (s.Pointer == login.characterName.Pointer) return 0;
                if (IsRandomName(s)) return 1;
                if (login.characterRace != null && s.Pointer == login.characterRace.Pointer) return 2;
                if (login.characterLoadout != null && s.Pointer == login.characterLoadout.Pointer) return 3;
                if (CustomRow(s) != null) return 4;
                if (s.gameObject.name == "CreateButton") return 6;
                return 5;
            }
            return list.Select((s, i) => (s, i)).OrderBy(x => Rank(x.s)).ThenBy(x => x.i).Select(x => x.s).ToList();
        }


        static float manualTextAt = -1f, descriptionAt = -1f;
        static string lastManualText;
        static void TimedSpeech()
        {
            if (descriptionAt > 0 && Time.unscaledTime >= descriptionAt)
            {
                descriptionAt = -1f;
                if (current != null) { var d = DescriptionOf(current, false); if (d != null) Speech.Say(d, false); }
            }
            if (manualTextAt > 0 && Time.unscaledTime >= manualTextAt)
            {
                manualTextAt = -1f;
                var ts = TutorialSystem.mainSystem;
                if (ts == null) return;
                var parts = new List<string>();
                foreach (var t in new[] { ts.tutorialTextFull, ts.tutorialDescription })
                    if (t != null && t.isActiveAndEnabled) AddText(parts, t.text, t.color.a);
                var text = string.Join(". ", parts);
                Log.Write("manual text: " + (text.Length > 200 ? text.Substring(0, 200) + "..." : text));
                if (text.Length > 0 && text != lastManualText) { lastManualText = text; Speech.Say(text, false); }
                else if (text.Length == 0) Speech.Say("Opened. Press F10 to read the screen.", false);
            }
        }

        // ---------- save confirmation ----------

        // After New Save / Overwrite Save, look in the game's save folder for the file it just wrote.
        static string pendingSave;
        static float saveCheckAt;
        static DateTime saveClickedAt;
        static void SaveClicked()
        {
            pendingSave = null;
            saveClickedAt = DateTime.Now;
            foreach (var s in items)
            {
                var f = s != null ? s.TryCast<TMP_InputField>() : null;
                if (f == null) continue;
                string t = !string.IsNullOrWhiteSpace(f.text) ? f.text : f.textComponent != null ? StripTags(f.textComponent.text) : "";
                t = t.Replace("​", "").Trim();
                if (t.Length > 0) { pendingSave = t; break; }
            }
            saveCheckAt = Time.unscaledTime + 1.5f;
            Log.Write("save clicked, name " + (pendingSave ?? "(none typed)"));
            Speech.Say("Saving.");
        }

        static void SaveCheck()
        {
            if (saveCheckAt <= 0 || Time.unscaledTime < saveCheckAt) return;
            saveCheckAt = 0;
            string newest = null;
            try
            {
                var dir = System.IO.Path.Combine(Application.persistentDataPath, "SaveGames");
                DateTime best = saveClickedAt.AddSeconds(-2);
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.unf", System.IO.SearchOption.AllDirectories))
                {
                    var w = System.IO.File.GetLastWriteTime(f);
                    if (w >= best) { best = w; newest = System.IO.Path.GetFileNameWithoutExtension(f); }
                }
            }
            catch (Exception e) { Log.Write("save check failed: " + e.Message); }
            Log.Write($"save check: typed {pendingSave ?? "(none)"}, newest file {newest ?? "none"}");
            Speech.Say(newest != null ? $"Saved as {newest}." : "No new save file was written. The save may not have worked.");
            pendingSave = null;
        }

        // ---------- keys ----------

        internal static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        internal static bool Pressed(int vk, bool repeat = false)
        {
            bool down = Down(vk);
            bool was = wasDown[vk];
            wasDown[vk] = down;
            if (down && !was) { repeatAt[vk] = Time.unscaledTime + 0.45f; Speech.Unprotect(); return true; }
            if (repeat && down && Time.unscaledTime >= repeatAt[vk]) { repeatAt[vk] = Time.unscaledTime + 0.08f; return true; }
            return false;
        }

        static bool GameHasFocus()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            bool f = pid == (uint)Environment.ProcessId;
            if (!f) foreach (var vk in watched) wasDown[vk] = Down(vk); // no stale presses when focus returns
            return f;
        }

        // ---------- diagnostics ----------

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== UI DUMP scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, menu mode {active}, cursor {Cursor.visible}/{Cursor.lockState}");
            sb.AppendLine($"items ({items.Count}):");
            foreach (var s in items) sb.AppendLine($"  [{s.GetIl2CppType().Name}] {PathOf(s.transform)} => {Describe(s)}");
            sb.AppendLine("all selectables:");
            foreach (var s in Selectable.allSelectablesArray)
                if (s != null) sb.AppendLine($"  [{s.GetIl2CppType().Name}] active={s.isActiveAndEnabled} inter={s.IsInteractable()} {PathOf(s.transform)} pos={ScreenPos(s)}");
            sb.AppendLine("texts:");
            foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
                if (t != null && t.isActiveAndEnabled) sb.AppendLine($"  {PathOf(t.transform)} = {StripTags(t.text)}");
            Log.Write(sb.ToString());
            Speech.Say("Dump written.");
        }
    }
}
