using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Il2Cpp;
using UnityEngine;
using UnityEngine.UI;

namespace UnderspaceAccess
{
    // The Manual screen (title menu "Manual", pause menu "Tutorials") can stay empty, but its
    // contents are game data: TutorialObject topics, each with pages (TutorialSubObject title
    // and explanation). While that screen is open the mod reads the data itself.
    //   Up/Down, Home/End: topics or pages. Enter or Right: open a topic. Left or Escape: back
    //   to the topics; Escape on the topics leaves the Manual. F11 repeats.
    internal static class ManualReader
    {
        static List<TutorialObject> topics;
        static int topic, page = -1; // page -1 = browsing topics
        static bool wasOpen;

        public static bool IsOpen(List<Selectable> items) =>
            items.Any(s => s != null && s.transform.parent != null && s.transform.parent.name == "TutorialBackButton");

        // The manual writes keys as commands ("/opentarget"); the game's keyboard turns them
        // into the keys the player has set. "*" starts each point.
        static readonly Regex command = new(@"/[a-z][a-z0-9]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex brackets = new(@"\[([^\]]+)\]", RegexOptions.Compiled);
        internal static string Clean(string text)
        {
            text = MenuNav.StripTags(text);
            var kb = OptionsMenu.mainMenu != null ? OptionsMenu.mainMenu.mainKeyboard : null;
            if (kb != null)
                text = command.Replace(text, m =>
                {
                    try
                    {
                        var key = kb.parseKeyboardString(m.Value);
                        return string.IsNullOrWhiteSpace(key) || key == m.Value ? m.Value : "the " + brackets.Replace(MenuNav.StripTags(key), "$1") + " key";
                    }
                    catch { return m.Value; }
                });
            text = brackets.Replace(text, "$1");
            var points = text.Split('*').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => p.EndsWith(".") || p.EndsWith("!") || p.EndsWith("?") ? p : p + ".");
            return string.Join(" ", points);
        }

        // Called every frame while menus are active. Returns true when it used the keys.
        public static bool Tick(List<Selectable> items)
        {
            bool open = IsOpen(items);
            if (!open) { wasOpen = false; return false; }
            if (!wasOpen)
            {
                wasOpen = true;
                Load();
                topic = 0; page = -1;
                if (topics.Count == 0) { Speech.Say("Manual. No topics found."); return false; }
                Speech.Say($"Manual, {topics.Count} topics. Up and Down choose, Enter opens, Escape goes back.");
                Speech.Say(TopicLine(), false);
            }
            if (topics.Count == 0) return false;

            if (MenuNav.Pressed(MenuNav.VK_DOWN, true)) Move(1);
            else if (MenuNav.Pressed(MenuNav.VK_UP, true)) Move(-1);
            else if (MenuNav.Pressed(MenuNav.VK_NEXT, true)) Move(10);
            else if (MenuNav.Pressed(MenuNav.VK_PRIOR, true)) Move(-10);
            else if (MenuNav.Pressed(MenuNav.VK_HOME)) Move(-1000);
            else if (MenuNav.Pressed(MenuNav.VK_END)) Move(1000);
            else if (MenuNav.Pressed(MenuNav.VK_RETURN) || MenuNav.Pressed(MenuNav.VK_SPACE) || MenuNav.Pressed(MenuNav.VK_RIGHT))
            {
                if (page < 0)
                {
                    if (Pages().Count == 0) { Speech.Say("This topic has no pages."); return true; }
                    page = 0;
                }
                Speech.Say(PageLine());
            }
            else if (MenuNav.Pressed(MenuNav.VK_LEFT)) { if (page >= 0) { page = -1; Speech.Say(TopicLine()); } }
            else if (MenuNav.Pressed(MenuNav.VK_F11)) Speech.Say(page < 0 ? TopicLine() : PageLine());
            else if (page >= 0 && MenuNav.Pressed(MenuNav.VK_ESCAPE)) { page = -1; Speech.Say(TopicLine()); }
            else return false; // Escape on the topics, F10: the normal menu reader handles them
            return true;
        }

        static void Load()
        {
            topics = new List<TutorialObject>();
            var ts = TutorialSystem.mainSystem;
            if (ts != null && ts.availableTutorialObjects != null)
                foreach (var t in ts.availableTutorialObjects) if (t != null) topics.Add(t);
            string from = "TutorialSystem";
            if (topics.Count == 0)
            {
                from = "game data";
                foreach (var t in Resources.FindObjectsOfTypeAll<TutorialObject>())
                    if (t != null && t.allTutorials != null && t.allTutorials.Count > 0) topics.Add(t);
                topics = topics.GroupBy(t => t.tutorialName).Select(g => g.First()).OrderBy(t => t.tutorialName).ToList();
            }
            Log.Write($"manual: {topics.Count} topics from {from}");
        }

        static List<TutorialSubObject> Pages()
        {
            var list = new List<TutorialSubObject>();
            var all = topics[topic].allTutorials;
            if (all != null) foreach (var p in all) if (p != null) list.Add(p);
            return list;
        }

        static void Move(int delta)
        {
            if (page < 0)
            {
                int n = Mathf.Clamp(topic + delta, 0, topics.Count - 1);
                if (n == topic && Math.Abs(delta) == 1) { Speech.Say((delta > 0 ? "Bottom. " : "Top. ") + TopicLine()); return; }
                topic = n;
                Speech.Say(TopicLine());
            }
            else
            {
                int count = Pages().Count;
                int n = Mathf.Clamp(page + delta, 0, count - 1);
                if (n == page && Math.Abs(delta) == 1) { Speech.Say(delta > 0 ? "Last page." : "First page."); return; }
                page = n;
                Speech.Say(PageLine());
            }
        }

        static string TopicLine()
        {
            int pages = Pages().Count;
            return $"{MenuNav.StripTags(topics[topic].tutorialName)}, {topic + 1} of {topics.Count}, {pages} page{(pages == 1 ? "" : "s")}";
        }

        static string PageLine()
        {
            var pages = Pages();
            var p = pages[page];
            string title = MenuNav.StripTags(p.tutorialTitle);
            if (title.Length > 1 && title == title.ToUpperInvariant()) title = char.ToUpper(title[0]) + title.Substring(1).ToLowerInvariant(); // "COMBAT" is spelled out by JAWS
            return $"{title}, page {page + 1} of {pages.Count}. {Clean(p.tutorialExplanation)}";
        }
    }
}
