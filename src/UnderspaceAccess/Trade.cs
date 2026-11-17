using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderspaceAccess
{
    // The station trade window (cargo trader, equipment dealer). The rows are StationTradeButton:
    // name, price, how many, category. The game's own keys act on the row the pointer is over
    // (the menu reader moves the pointer there): X buy or sell one, A buy or sell all.
    //   On opening and after every purchase or sale: credits and cargo hold.
    //   F11 on a row: its details and the stations that pay the most for it (the game's best prices).
    internal static class Trade
    {
        static bool wasOpen;
        static string lastCredits = "";

        public static bool Open
        {
            get
            {
                var ui = StationUI.mainUI;
                return ui != null && ui.tradingUI != null && ui.tradingUI.activeInHierarchy;
            }
        }

        public static void Tick()
        {
            try
            {
                bool open = Open;
                AnnounceCheck();
                if (open != wasOpen)
                {
                    wasOpen = open;
                    if (open)
                    {
                        lastCredits = Credits();
                        levelSeen = LevelNow();
                        string line = $"{TraderName()}Credits {lastCredits}. Cargo hold {Hold()}. {Progress()} Up and Down arrows: goods. X: buy or sell one. A: buy or sell all. F11 on goods: where it sells for the most.";
                        Log.Write("trade open: " + line);
                        Speech.Say(line, false);
                    }
                    return;
                }
                if (!open) return;
                SubPanelWatch();
                StageCheck();
                PendingCheck();
                string c = Credits();
                if (c != lastCredits)
                {
                    // Works for every way of trading (X, A, the game's own E and F, the Buy button).
                    // Said a moment later: the hold number and the list update after the credits.
                    if (announceAt < 0) announceFrom = lastCredits;
                    lastCredits = c;
                    announceAt = Time.unscaledTime + 0.6f;
                }
            }
            catch (Exception e) { Log.Write("Trade error: " + e.Message); }
        }

        static string TraderName()
        {
            var ui = StationUI.mainUI;
            string n = ui != null && ui.traderName != null ? MenuNav.StripTags(ui.traderName.text).Trim() : "";
            return n.Length > 0 ? n + ". " : "";
        }

        static string Credits()
        {
            var ui = StationUI.mainUI;
            string t = ui != null && ui.playerCredits != null ? MenuNav.StripTags(ui.playerCredits.text).Trim() : "";
            return t.Length > 0 ? t : "unknown";
        }

        // The hold shows as "used/size", for example "0/15" (seen 10/25 at Pondshell & Palace).
        static readonly Regex holdRx = new(@"^\s*(\d+)\s*/\s*(\d+)\s*$");
        static string Hold()
        {
            var ui = StationUI.mainUI;
            if (ui == null || ui.tradingUI == null) return "unknown";
            foreach (var t in ui.tradingUI.GetComponentsInChildren<TMP_Text>())
            {
                if (t == null || !t.isActiveAndEnabled) continue;
                var m = holdRx.Match(MenuNav.StripTags(t.text));
                if (m.Success) return $"{m.Groups[1].Value} of {m.Groups[2].Value} used";
            }
            return "unknown";
        }

        static StationTradeButton RowOf(Selectable s)
        {
            if (s == null) return null;
            var b = s.GetComponent<StationTradeButton>();
            if (b == null) b = s.GetComponentInParent<StationTradeButton>();
            return b;
        }

        static string T(TMP_Text t) => t != null && t.isActiveAndEnabled ? MenuNav.StripTags(t.text).Trim() : "";

        // "Consumer Goods, 19 credits each, 120 for sale, Commodity." Null when not a trade row.
        public static string Describe(Selectable s)
        {
            var b = RowOf(s);
            if (b == null) return null;
            var parts = new List<string>();
            string name = T(b.itemName);
            if (name.Length == 0) return null;
            parts.Add(name);
            string price = T(b.itemValue);
            if (price.Length > 0) parts.Add(price + " each");
            string count = T(b.itemCount);
            if (count.Length > 0 && !Regex.IsMatch(count, @"\d")) count = ""; // equipment shows "Active" here
            if (!b.isNPC) { LoadCosts(); if (costs.TryGetValue(name, out var paid) && paid.units > 0) parts.Add($"you paid {Money(paid.cost / paid.units)} each"); }
            if (count.Length > 0) parts.Add(b.isNPC ? count + " for sale" : "you have " + count);
            else if (b.cargoItemCount > 0) parts.Add(b.isNPC ? b.cargoItemCount + " for sale" : "you have " + b.cargoItemCount);
            if (b.isEquipped) parts.Add("equipped on your ship");
            string cat = T(b.itemCategory);
            if (cat.Length == 0) cat = T(b.itemType);
            if (cat.Length > 0) parts.Add(cat);
            parts.Add(b.isNPC ? "X buys one, A buys all" : "X sells one, A sells all");
            return string.Join(", ", parts);
        }

        // F11 on a row: the game's best buying and selling prices for that goods (StationUI.getBestPrices
        // fills bestBuyingList / bestSellingList; HYPOTHESIS that it works on the hovered row).
        public static string Details(Selectable s)
        {
            var b = RowOf(s);
            var ui = StationUI.mainUI;
            if (b == null || ui == null) return null;
            var lines = new List<string>();
            try
            {
                ui.checkTradeButton(b);
                ui.getBestPrices();
                string sell = Prices(ui.bestSellingList), buy = Prices(ui.bestBuyingList);
                Log.Write($"best prices for {T(b.itemName)}: sell [{sell}] buy [{buy}]");
                string guide = T(ui.priceGuideText);
                Log.Write("price guide text: " + guide);
                if (sell.Length > 0) lines.Add("To earn money, sell it at: " + sell);
                if (buy.Length > 0) lines.Add("Cheapest to buy at: " + buy);
                if (sell.Length == 0 && buy.Length == 0 && guide.Length > 0) lines.Add(guide);
                try { ui.closeBestPrices(); } catch { }
            }
            catch (Exception e) { Log.Write("best prices failed: " + e.Message); }
            if (lines.Count == 0) lines.Add("No price list from other stations yet. The game only knows prices of stations you have docked at. Visit other stations and look at their cargo trader, then press F11 here again. Ijuni trademasters on capital planets and big stations also tell base prices.");
            return string.Join(". ", lines) + ".";
        }

        // ---------- X and A: buy or sell ----------
        // The game's own X and A only work on the row under the real mouse pointer, so they did
        // nothing for keyboard users (10/27 test). The mod opens the same quantity panel the
        // game opens on a click (openSubMenu), sets 1 or the maximum, and presses its Buy or Sell button.
        const int VK_X = 0x58, VK_A = 0x41;
        static float pendingAt = -1f;
        static string pendingCredits;

        // Called by the menu reader every frame while a menu is open. True when it used the key.
        public static bool KeyCheck(Selectable current)
        {
            if (!Open) return false;
            bool one = MenuNav.Pressed(VK_X), all = MenuNav.Pressed(VK_A);
            Remember(current);
            if (!one && !all) return false;
            var b = RowOf(current);
            if (b == null) { Speech.Say("First move to goods with the Up and Down arrows."); return true; }
            if (stageAt >= 0 || pendingAt >= 0) return true;
            DoTrade(current, b, all);
            return true;
        }

        // 11/1 test: calling openSubMenu directly left the panel half filled (no name, no price) and
        // updateTradingAmount threw. A real click on the row fills it (seen in the log), so X and A click first.
        static void DoTrade(Selectable row, StationTradeButton b, bool all)
        {
            var ui = StationUI.mainUI;
            stageRow = b; stageAll = all; stageName = T(b.itemName);
            stageAt = Time.unscaledTime + (ui.isSubPanelOpen ? 0f : 0.15f);
            try { if (!ui.isSubPanelOpen) MenuNav.ClickRow(row); }
            catch (Exception e) { stageAt = -1f; Log.Write("trade click failed: " + e); Speech.Say("That did not work."); }
        }

        static void StageCheck()
        {
            if (stageAt < 0 || Time.unscaledTime < stageAt) return;
            stageAt = -1f;
            var ui = StationUI.mainUI;
            var b = stageRow;
            try
            {
                if (!ui.isSubPanelOpen) { Log.Write("trade: panel did not open"); Speech.Say("That did not work."); return; }
                if (stageAll) { try { ui.setMaxTradingAmount(); } catch (Exception e) { Log.Write("trade setMax failed: " + e.Message); } }
                else if (ui.cargoSlider != null) ui.cargoSlider.value = Mathf.Max(1f, ui.cargoSlider.minValue);
                try { ui.updateTradingAmount(); } catch (Exception e) { Log.Write("trade updateAmount failed: " + e.Message); }
                string amount = T(ui.cargoAmount), total = T(ui.cargoCostTotal), problem = ProblemText();
                Log.Write($"trade {(stageAll ? "all" : "one")} {stageName} npc {b.isNPC}: amount '{amount}' total '{total}' slider {(ui.cargoSlider != null ? ui.cargoSlider.value + "/" + ui.cargoSlider.maxValue : "none")} problem '{problem}' buy {Active(ui.buyButton)} sell {Active(ui.sellButton)}");
                if (problem.Length > 0) { Speech.Say(problem); try { ui.forceCloseSubMenu(); } catch { } return; }
                var go = b.isNPC ? ui.buyButton : ui.sellButton;
                if (!Active(go)) go = Active(ui.buyButton) ? ui.buyButton : Active(ui.sellButton) ? ui.sellButton : null;
                var btn = go != null ? go.GetComponent<Button>() : null;
                pendingCredits = Credits();
                pendingAt = Time.unscaledTime + 0.8f;
                if (btn != null) { Log.Write("trade: clicking " + go.name); btn.onClick.Invoke(); }
                else { Log.Write("trade: no buy/sell button, itemTransaction"); ui.itemTransaction(); }
            }
            catch (Exception e) { pendingAt = -1f; Log.Write("trade failed: " + e); Speech.Say("That did not work."); }
        }

        // ---------- What a trade earned ----------
        static float announceAt = -1f, stageAt = -1f;
        static string announceFrom = "", stageName = "", rowName = "";
        static StationTradeButton stageRow;
        static bool stageAll;
        static long rowPrice;
        static int levelSeen = -1;
        static Dictionary<string, (long units, long cost)> costs;

        static void Remember(Selectable s)
        {
            var b = RowOf(s);
            if (b == null) return;
            string n = T(b.itemName);
            if (n.Length == 0) return;
            rowName = n;
            rowPrice = Number(T(b.itemValue));
        }

        static long Number(string text)
        {
            var m = Regex.Match(text ?? "", @"\d[\d,]*");
            return m.Success && long.TryParse(m.Value.Replace(",", ""), out long v) ? v : 0;
        }

        static string Money(long n) => "$" + n.ToString("N0");

        static void AnnounceCheck()
        {
            if (announceAt < 0 || Time.unscaledTime < announceAt) return;
            announceAt = -1f;
            long change = Number(lastCredits) - Number(announceFrom);
            if (change == 0) return;
            long units = rowPrice > 0 ? Math.Abs(change) / rowPrice : 0;
            string what = units > 0 ? $"{units} {rowName}" : rowName;
            string line;
            LoadCosts();
            costs.TryGetValue(rowName, out var known);
            if (change < 0)
            {
                line = $"Bought {what} for {Money(-change)}.";
                if (units > 0) { costs[rowName] = (known.units + units, known.cost + -change); SaveCosts(); }
            }
            else
            {
                line = $"Sold {what} for {Money(change)}.";
                if (units > 0 && known.units >= units)
                {
                    long paid = known.cost * units / known.units;
                    long gain = change - paid;
                    line += $" You paid {Money(paid)}. " + (gain > 0 ? $"Profit {Money(gain)}." : gain < 0 ? $"Loss {Money(-gain)}." : "No profit, no loss.");
                    costs[rowName] = (known.units - units, known.cost - paid);
                    SaveCosts();
                }
            }
            line += $" Credits {lastCredits}.";
            string hold = Hold();
            if (hold != "unknown") line += $" Cargo hold {hold}.";
            int level = LevelNow();
            if (levelSeen > 0 && level > levelSeen) line += $" Level up! You are now level {level}.";
            if (level > 0) levelSeen = level;
            line += " " + Progress();
            Log.Write("trade: " + line);
            Speech.Say(line);
            Speech.Protect(line); // the list refresh must not cut this off; the next key press still does
        }

        static int LevelNow()
        {
            try { var pc = PlayerController.Player; return pc != null ? pc.playerLevel : -1; } catch { return -1; }
        }

        // Your level comes from your net worth, which is what the game counts instead of points.
        // HYPOTHESIS: totalCurrentWorth is the net worth the next level needs (values are logged to check).
        static string Progress()
        {
            try
            {
                var pc = PlayerController.Player;
                if (pc == null) return "";
                long worth = (long)pc.getWorth(), next = pc.totalCurrentWorth;
                Log.Write($"progress: level {pc.playerLevel} worth {worth} totalCurrentWorth {next} requiredWorthChange {pc.requiredWorthChange} requiredCredits {pc.requiredCredits} unusedPoints {pc.unusedPoints} credits {pc.playerCredits}");
                string line = $"Level {pc.playerLevel}, net worth {Money(worth)}.";
                if (next > worth) line += $" Next level at {Money(next)}, {Money(next - worth)} to go.";
                if (pc.unusedPoints > 0) line += $" {pc.unusedPoints} skill points to spend.";
                return line;
            }
            catch (Exception e) { Log.Write("progress failed: " + e.Message); return ""; }
        }

        static string CostFile => System.IO.Path.Combine(Main.Dir, "trade_costs.txt");

        static void LoadCosts()
        {
            if (costs != null) return;
            costs = new();
            try
            {
                if (!System.IO.File.Exists(CostFile)) return;
                foreach (var l in System.IO.File.ReadAllLines(CostFile))
                {
                    var p = l.Split('|');
                    if (p.Length == 3 && long.TryParse(p[1], out long u) && long.TryParse(p[2], out long c)) costs[p[0]] = (u, c);
                }
            }
            catch (Exception e) { Log.Write("trade costs load failed: " + e.Message); }
        }

        static void SaveCosts()
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in costs) if (kv.Value.units > 0) lines.Add($"{kv.Key}|{kv.Value.units}|{kv.Value.cost}");
                System.IO.File.WriteAllLines(CostFile, lines);
            }
            catch (Exception e) { Log.Write("trade costs save failed: " + e.Message); }
        }

        // Half a second later: did the credits change? Then close the quantity panel so the list is back.
        static void PendingCheck()
        {
            if (pendingAt < 0 || Time.unscaledTime < pendingAt) return;
            pendingAt = -1f;
            var ui = StationUI.mainUI;
            string now = Credits();
            string problem = ProblemText();
            Log.Write($"trade result: credits {pendingCredits} -> {now}, problem '{problem}', panel open {ui.isSubPanelOpen}");
            if (now == pendingCredits) Speech.Say(problem.Length > 0 ? problem : "Nothing changed. Maybe the cargo hold is full or you do not have enough credits.");
            try { if (ui.isSubPanelOpen) ui.forceCloseSubMenu(); } catch { }
            subOpen = false;
        }

        static bool Active(GameObject g) => g != null && g.activeInHierarchy;

        static string ProblemText()
        {
            var ui = StationUI.mainUI;
            return ui != null && Active(ui.problemButton) ? T(ui.problemText) : "";
        }

        // Enter on a row opens the game's quantity panel: read it, and the quantity when it changes.
        static bool subOpen;
        static string lastAmount = "";
        static void SubPanelWatch()
        {
            var ui = StationUI.mainUI;
            bool open = ui.isSubPanelOpen && pendingAt < 0 && stageAt < 0;
            if (open != subOpen)
            {
                subOpen = open;
                lastAmount = T(ui.cargoAmount);
                if (open)
                {
                    string problem = ProblemText();
                    string line = $"{T(ui.cargoName)}. Price {T(ui.cargoCost)}. {T(ui.cargoAmount)}. {T(ui.cargoCostTotal)}." +
                                  (problem.Length > 0 ? " " + problem + "." : "") +
                                  " Left and Right arrows on the slider change the amount, then the Buy or Sell button. Or Escape, and X or A on the goods.";
                    Log.Write("trade panel: " + line);
                    Speech.Say(line, false);
                }
                return;
            }
            if (!open) return;
            string a = T(ui.cargoAmount);
            if (a != lastAmount) { lastAmount = a; Speech.Say($"{a}. {T(ui.cargoCostTotal)}."); }
        }

        static string Prices(Il2CppSystem.Collections.Generic.List<priceNode> list)
        {
            if (list == null || list.Count == 0) return "";
            var parts = new List<string>();
            for (int i = 0; i < list.Count && i < 3; i++)
            {
                var p = list[i];
                parts.Add($"{MenuNav.StripTags(p.stationName)} {Mathf.RoundToInt(p.stationPrice)} credits");
            }
            return string.Join(", ", parts);
        }
    }
}
