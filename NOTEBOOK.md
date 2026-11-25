# Notebook: Underspace

## RESUME (2026-11-25 10:50) - Claude played Mission 1 to the end; KEEP this progress (user said so)

- Save: game autosave6 (10:38:26, right at Mission Complete) in SaveGames\Kardoz; copy in Downloads\Games_Backup\Underspace_saves_2026-11-25_after_mission1. User's old save (laneline step) in ..._before_claude_missions. Load newest Kardoz save (autosave6) to start Mission 2.
- PROVEN 11/25: 0.10.2 laneline fly-in works (3 lanelines). Update check works. Mission 1 COMPLETE ($65,000, level 4): laneline -> fighters -> laneline Spolen -> laneline Rikenrend (protect shipyard) -> find convoy (autopilot ~40 km) -> ambush 5 Nebulas + gunboat DSS Acceptance near a collapsed jumphole. Mission 2 "Upgrades": land on Rikenrend Shipyard; optional storm chase with Veildrake Lansrig; STORMS tutorial.
- PROVEN steering bug: after some popups / during autopilot the game sets Cursor.lockState None; it then steers by the pointer position (virtual joystick) and holds the ship against MoveRotation (~1 deg/s). SetCursorPos right of centre turned the ship. Locked/Confined: MoveRotation works. Relocking each frame failed (game resets None every frame). FIX: Flight.PointerStick steers by placing the real pointer when lock is None (UNTESTED in play).
- Other fixes: behind-target left/right flip; aim 90 deg/s while homing; Period during autopilot GOTO/DOCK presses game F1 once; Period on non-ship target says so; standing-still warning while firing; "Shields down! Tap G", "Hold G" every 15 s under 60 hull; damage: log of nearby ships; death screen hint; tutorial keyboard hints (CONTACT WINDOW, COMBAT, ABYSSDRIVE); walking intro no longer cuts space intro; dying target re-picked ("destroyed" x15) fixed, cargo hint once. Newest build compiled, NOT deployed while game ran.
- Deaths: autopilot flew into the ambush (100->0 in ~1 s, cause unproven); parked at speed 0 under 5 Nebulas. Survived with throttle up + G.
- Open: "You have leveled up" tutorial skipped as already said (SaidRecently false positive). Comms all labelled Yunelese.

## RESUME (2026-11-03 later) - v0.10.2 deployed (laneline ring + no double speech), UNTESTED

- 0.10.1 PROVEN 2:05-2:19 AM log: Semicolon chaptermaster worked, mission "First Day On The Job" started, launched, F2 to waypoint 6.4 km OK. Then "Dock with the laneline. Hint: Fly your ship through the bottom ring." Target Planet Vauldric >> Spolen Trainyard (dockDistance 1100); F at 900-1027 m did nothing ~20x, L toggled manual docking, F2 Goto stopped instantly ("already close").
- SDK SolarLaneLinePortal: player enters via OnTriggerEnter when canEnterLane() (campaign: CampaignManager.currentAllowedDockingSolar + dockingSolarDirection) and isActive; enterPortal(ship, 0) same logic. SolarLaneLine.nextPortal/previousPortal.
- 0.10.2 Flight: F2/F on a SolarLaneLine target = StartLane: picks portal with canEnterLane (nearest if both), mod aims (AimPoint) + throttle, waits while game GOTO, enterPortal under 250 m (again each 50 m closer), success when autopilot leaves FREEFLIGHT. 120 s timeout. HYPOTHESIS: enterPortal works when isActive false? log 'lane start', 'lane: enterPortal', 'lane stopped'.
- 0.10.2 speech: InGame.SaidRecently/Remember (6 s, normalized containment) skips duplicates (subtitle + station subtitle, message + objective, screen info + tutorial, after-click). InGame.DropMouse removes sentences with LMB/RMB/MMB/mouse from tip/tutorial/prompt/advance. MenuNav.Useful filters key hints "X: [K]", "[L]", Game Saved, Dialogue History, FREE FLIGHT, version; used by F10 ScreenText, ScreenInfo (also drops >300 chars), HoverCheck.
- Test sheet Downloads\UnderspaceAccess\CAMPAIGN_FIX_0.10.2.txt.
## RESUME (2026-11-03) - v0.10.1 deployed (campaign start fix), UNTESTED

- 11/3 1:43-1:56 AM log (first campaign run): intro + Karbelle talk OK, objective "Go to the chapterhouse, Hint F7" OK, F7 reached chapterhouse. Tutorial "Talk to the chaptermaster with F" was said once; mod never named the chaptermaster. People list: Hantaonailati 21 m, Veildrake Lansrig 43 m. User walked to Hantaonailati, Blocked at 13-15 m, F at 14.8 m did nothing (fallback limit was 12 m), then wandered to launch bay (launch warning) and bar, quit. Chaptermaster identity UNPROVEN (Hantaonailati = HYPOTHESIS, she speaks of losing starchasers).
- Semicolon earlier said "Go to the Underspace information" (currentWantedRoomType during cinematic, wrong).
- 0.10.1: InGame.LastTask (last objective / "Talk to|Go to|Press" tutorial). Semicolon in station: "Talk to" task -> Navigator.ChooseTalkPerson (quest marker StationNPC.hasQuest active > JOB/QUEST interaction > canInteract, nearest) selects + guides; else repeats task. People list adds ", has something for you" for quest marker; logs "person X: interaction, quest marker, quest id, desc". F reach for person/thing 12 -> 18 m (TalkReach). Tutorial "Talk to" adds hint "press Semicolon".
- Test sheet: Downloads\UnderspaceAccess\CAMPAIGN_FIX_0.10.1.txt. NEXT: read "person ", "talk person chosen", "F fallback", "campaign:" lines.

## Previous RESUME (2026-11-02) - v0.10.0 - v0.10.0 deployed (Campaign Mission 1, keyboard fire), UNTESTED

- User asked to move to the Campaign and make at least mission 1 accessible. Freeplay saves untouched; saves copied to Downloads\Games_Backup\Underspace_saves_2026-11-02; old DLL = UnderspaceAccess_0.9.4_backup.dll in the project folder. A Kardoz save folder already exists from 10/22 (a new campaign may overwrite it; it is in the backup).
- 0.9.4 after-dinner test never happened: last log is 11/1 16:29. Part 8 still untested.
- Mission text PROVEN from game data only (length-prefixed strings in level1 + resources.assets, not from play): Looking For Work (go to chapterhouse on Planet Vauldric, /headtouhcchapterhouse) -> First Day On The Job (launch, meet squad, "simple transport escort", combat; leader Yunelese) -> land on Rikenrend Shipyard. Order is an inference. Mission scripts live in prefabs; the SDK has stubs only.
- New Mission.cs: Semicolon WhatNow (mission, quest tracker, selects ContactHUD mission/quest/waypoint; station: currentWantedRoomType + room keys). Hold Period: nose on target (Flight home) + real RMB via mouse_event + TargetingUI.curveTarget = target each frame (SDK ComponentGun.activateComponent leads with curveTarget). Range from FireControl.MaxGunRange. Stay-near warning at 85%. "campaign:" log line on any CampaignManager change. InGame.Clean drops colour/icon wording and turns <<HINT>> into Hint.
- HYPOTHESIS: synthetic RMB reaches the game; curveTarget survives the frame; MaxGunRange is set; arrows/Home rotation is not fought by the mouse steering; comm reply buttons and pause tutorial popups can be operated.
- NEXT: read log "campaign:", "what now:", "fire start", "fire check", "fire end", "curveTarget". fire end must show target hull/shields dropping. If isUsingEquipment is false, RMB did not reach the game.

## Previous (2026-11-01 PM) - v0.9.4 deployed (trade X/A + trade result speech), UNTESTED

- User test 16:19-16:29 (0.9.3), log read. PROVEN: bought 15 Consumer Goods $19 at The Axle (15,000 -> 14,715), sold 15 at $30 at another station (-> 15,165), profit $165. F11 not used.
- X/A in 0.9.3 threw NullReference every press, before the "trade one/all" log line: openSubMenu + setMax ran (Quantity changed), so updateTradingAmount is the likely thrower (HYPOTHESIS). Panel was blank (no name/price) after direct openSubMenu; full after a real click on the row (PROVEN 16:29:06).
- The trades happened with no mod click logged, shortly after hints "[F] Buy All. [E] Buy One": probably the game's own F/E (HYPOTHESIS; user said it did not work as told).
- Result line was cut by the list refresh (say [i] "Commodities, 2 of 6" 0.14 s later) and hold was stale (15 of 15, real 0/15).
- 0.9.4: X/A = real row click (MenuNav.ClickRow), 0.15 s later set amount and press Buy/Sell (StageCheck). Any credits change = spoken 0.6 s later, protected: bought/sold N name for $, paid + profit/loss (mod's own ledger UserData\UnderspaceAccess\trade_costs.txt, not tied to save slot), credits, hold, level, net worth. Trade open also says level + worth. Row says "you paid $x each".
- HYPOTHESIS: totalCurrentWorth = net worth for next level. Check log "progress:" lines against a level-up.
- User tests after dinner 11/1. Then build mining: fields list, nearby asteroids list, Home aim, collect messages.
- NEXT: read log "trade one/all", "trade updateAmount failed", "trade result", "trade: Sold", "progress:". Then combat + mining plan below.

## Previous (2026-10-30 night) - log of 10/27 19:54 read, no code changed

- PROVEN (log): gate fly-through jumped Omega-Alpha -> Marrenwhit (IN07, "Independent Space"). F2 pressed mid-approach stops it, then F restarts it.
- Death at Marrenwhit: 19:56:33.9 warning "Enemy: Eclipse 2.6 km" (LightFighter, only hostile within 6 km); ship nearly stopped from :42 while user browsed lists; :47.08 shields 59 -> :47.70 hull 0. Attacker not logged, so Eclipse is the likely but UNPROVEN killer.
- Next (user: Sat/Sun): combat + asteroid mining; speak SolarSystemRecord.systemLevel + systemCategory on map/gate entries (HYPOTHESIS: level = danger; dump all values first); log damage source; escape advice in enemy warning.

## Previous: (2026-10-27 afternoon) - v0.9.3 deployed (trade X/A, gate fly-through, dock guard)

- User test 13:10-13:35 (0.9.2), log read:
  - Trade: X/A did nothing (game's hotkeys need the real pointer over the row; hover text alternated
    "[X] Buy One. [A] Buy All" / "[E] Buy One. [F] Buy All"). Enter on a row = openSubMenu panel
    ("Price $19 | Consumer Goods | Quantity: 1 | Total: -$19"), not read. F11 never used.
  - Gates: game GOTO to a gate ends FREEFLIGHT at 1.2-2.5 km; gate dockDistance 1500 (laneline 1100),
    F at 1847/2483/2581 m did nothing; ship drifts past. HUD text showed "No Entry" near Alpha-Omega
    Jumpgate (meaning unknown). 10/25 jump (INJUMP at 542 m) not reproduced.
  - Docking The Axle WORKED: DOCK -> ENTERDOCK 13:29:59 -> FULLDOCK 13:30:12, but F3 at 13:30:12.8
    (mod fallback toggleAutoFollower) put autopilot back to DOCK, then ship circled 2+ min. Toggling
    autofollow off also triggers game message "Manual docking enabled". 2nd try: ENTERDOCK 13:34:22,
    Enter (getBestPath re-route) at 13:34:39, stuck ENTERDOCK > 1 min at ~4 km.
- 0.9.3: Trade.KeyCheck (called from MenuNav before Escape): X/A on a StationTradeButton ->
  openSubMenu(b), setMaxTradingAmount or cargoSlider.value=1, updateTradingAmount, click
  buyButton/sellButton (fallback itemTransaction); 0.5 s later credits compare -> "Bought N name",
  else problemText; forceCloseSubMenu. Sub panel read (cargoName/Cost/Amount/CostTotal). Hover
  "] Buy/Sell/Equip" hints dropped. F11 also logs priceGuideText. HYPOTHESIS: all of these calls.
- Flight: Busy() (ENTERDOCK/FULLDOCK/INJUMP/INQUEUE/LEAVEDOCK) swallows Enter/F/F2/F3/PgUp/PgDn with
  "please wait". Jump helper (F2 or F on Jumpgate/Jumphole/JumpPoint): stage 1 game GOTO; when
  FREEFLIGHT: within 0.9*dockDistance -> ship.enterDock(true, gate) (stage 2, 3 s), else stage 3: mod
  autoAim (FixedTick rotation) + ship.input.throttle=1, re-ask gate every 400 m closer; ends on
  INJUMP; arrows/F1 stop; 150 s timeout. Logs "jump start" (jumpDistance, extents, meetsDockingRequirements).
- NEXT: read log "jump start", "jump stage", "jump: asking gate", "trade one/all", "trade result",
  "trade panel", "best prices", "price guide text". If throttle write is ignored, try holding W via input.

### Earlier: RESUME (2026-10-25 ~8:20 AM) - v0.9.2 deployed, UNTESTED (trade window, docking help, all systems)

- User test 07:19-07:29 (0.9.1): PROVEN: "ways out" list (Omega-Alpha Jumpgate -> Omega-Alpha, id UN02 matches
  SolarSystemRecord.linkedSolarSystemID); F2 to gate -> GOTO, then autopilot INJUMP at 542 m -> scene UN02
  "Entering Omega-Alpha System". 6 gates there (Tierra jumphole EDG03, The Baton IN01, Thoroughfare IN06,
  Marrenwhit IN07, Hardturn IN04). Docked The Axle (scene Axle): F granted x3 with no move while manual docking
  enabled; after "Manual docking disabled" F -> ENTERDOCK -> FULLDOCK. GOTO near station flips GOTO->FREEFLIGHT
  in 10 ms. Trade window: rows "Consumer Goods, $19, Cargo Item" + hover "[X] Buy One. [A] Buy All";
  header texts "0/15", "Cargo Trader", "Credits". Rooms at The Axle: The Duchess (bar), United Weaponry,
  Pondshell & Palace (trader). Saved over firstgame at The Axle 07:29.
- 0.9.2: Trade.cs (StationUI.tradingUI active: credits playerCredits + hold "n/m" text on open and on credits
  change; StationTradeButton rows described: itemName, itemValue, itemCount, isNPC, itemCategory; F11 =
  checkTradeButton + getBestPrices -> bestSellingList/bestBuyingList priceNode, HYPOTHESIS). Flight: "Dock
  Request Granted" + 3 s FREEFLIGHT -> "manual docking is on, press L"; GOTO ending < 1.5 s near dockable ->
  "already close, press F"; category 8 "Map, all star systems A to Z" (knownSystems minus Unknown), Enter =
  getPathToSystem, "No path available" -> spoken explanation; logs SystemMap.knownConnections.
- User away 8:25 AM, tests later. Then MINING (SolarSystem.allMiningFields/allAsteroidFields, manual: small moving asteroids, B collects).
- NEXT: read log ("trade", "best prices", "map all systems ... known connections", "dock granted").

### Earlier: RESUME (2026-10-25 ~8 AM) - v0.9.1 deployed, UNTESTED (jumpgate travel)

- User test 07:09-07:15 (0.9.0): PROVEN: getBestPath(Communication Relay) -> waypoint; F3 -> "Autofollow
  Engaged: Standard", GOTO, high engines, disengaged on arrival. PROVEN DOCKING: F2 on UHC-1 at 6 km ->
  autopilot DOCK -> ENTERDOCK ("Dock Request Granted") -> FULLDOCK -> station scene. L pressed -> "Manual
  docking disabled" (earlier failures were manual docking enabled). StatsHUD.manualDockingIndicator was
  active while docking worked -> not a manual-docking sign.
- Map places: only 1 of 5 BaseSolars had mapIcon/isInitiallyMapped -> filter removed.
- Systems: getValidconnectingSystemIDs(here,false) = 0, SystemMap.knownSystems = 113 (all systems, many
  "Unknown"); getPathToSystem -> game message "No path available" (no discovered connections).
- 0.9.1: category 7 = "Map, ways to other star systems": FindObjectsOfType<JumpPoint>, destination
  jumpSystemID looked up in knownSystems by linkedSolarSystemID/systemAbbreviation (HYPOTHESIS which ID
  matches; log "map systems: ... -> name (id ...)"). Enter = getBestPath(gate) + target. Manual docking =
  only the game's messages. Hints: F2 flies there and docks.
- NEXT: read log for "map systems" ids and whether F2/DOCK on a jumpgate jumps (autopilot INJUMP, new scene).

### Earlier: RESUME (2026-10-25 ~7:45 AM) - v0.9.0 deployed, UNTESTED (map + docking)

- User test 06:57-07:02 (0.8.2): PROVEN: space no longer a menu; contact list read (Solars: UHC-1, Maudlin Yards;
  jumpgate "Omega-Alpha Jumpgate" seen at 9.4 km); Enter -> TargetingUI targeted True; game F2 -> GOTO, stops
  ~3.7 km out ("Autopilot stopped"); F2 again toggles GOTO off (mod fallback wrongly restarted it). dock key
  parse /dockwithtarget = F. Ship.enterDock(true,target) -> comms "Dock Request Granted" but autopilot stayed
  FREEFLIGHT (x4, incl. 1.4 km). HYPOTHESIS: manual docking on (user pressed L twice on 10/25 05:59).
  Category read right after switchTo* returned the previous list. steer: first turn logged, rigidbody True.
- 0.9.0: categories 6 "Map, places in this system" (FindObjectsOfType<BaseSolar>, kinds station/planet/gate/
  hole/laneline/ring/wreck/sun, mapIcon != null || isInitiallyMapped, one per name) and 7 "Map, other star
  systems" (WaypointManager.getValidconnectingSystemIDs(current) + SystemMap.knownSystems; current =
  FindObjectOfType<SolarSystem>().linkedRecord). Enter on place = WaypointManager.getBestPath(solar) + target;
  on system = getPathToSystem(r, here) then route to currentWantedGate. F3 = game auto-follow, fallback
  toggleAutoFollower if autoFollow unchanged; autoFollow + ContactHUD.currentWaypoint changes spoken.
  F = dock: log + after 2 s "too far" / "manual docking is on, press L" (StatsHUD.manualDockingIndicator or
  last "Manual docking enabled/disabled" message). F2 fallback only if FREEFLIGHT at press; no dock fallback.
  Category read delayed 0.35 s.
- NEXT: read log: "map places", "map systems", "route to", "F3", "autoFollow", "waypoint:", "F pressed in space",
  "after F". Then trader screen, mining, combat.

### Earlier: RESUME (2026-10-25 ~7:15 AM) - v0.8.2 deployed, UNTESTED

- User test 06:44-06:48 (0.8.1): Z in bar opened Job Board (PROVEN, missions read: name, faction, D: 12-17,
  pay, type; "[F] Accept Mission" tooltip); C = News (PROVEN); Z in launch bay launched (PROVEN).
  BUGS: room keys spoken one room late (buttons switch after the room change); in space cursor went
  invisible + lock None at 06:46:52 -> MenuNav active again ("No menu items", F10 read HUD junk).
  Flight briefly "in space True" during save load (before station scene).
- 0.8.2: menu mode in space only when Cursor.visible; room line delayed 1.2 s then RoomServiceKeys();
  Flight in/out of space needs 1 s stable.
- Jobs at UHC-1 all D 12-17 vs player level 5 (manual: difficulty = enemy level). User chose to STAY IN FREEPLAY (7:20 AM). Plan: flight test, then trading / wreck looting / non-combat jobs (level comes from net worth).

### Earlier: RESUME (2026-10-25 ~7 AM) - v0.8.1 deployed, UNTESTED; flight (0.8.0) still untested

- User test 06:29-06:38 (0.8.0): loaded firstgame (docked UHC-1, launch bay). Walked to bar Job Board, stuck at
  11-14 m ("Blocked" 22x, tables). F2 in launch bay did nothing (already there). Never launched.
- PROVEN from SDK FirstPersonMovement.Update (Dependencies, has bodies): keyStation_Action1 (Z) / Action2 (C)
  fire the room's shortcut buttons when active: launchButton1/2 -> StationUI.launchToSpace, jobButton ->
  MissionMasterSystem.openMissions, equipmentDealerButton, traderButton, shipdealerButton, underspaceButton;
  C: trainTravelButton, newsButton, repairButton (RepairUI), storageButton, shipCustomizationButton,
  underspaceRegionButton. Not yet proven live.
- 0.8.1: InGame.RoomServiceKeys() reads those buttons' activeInHierarchy -> "Z: Job Board. C: News Board."
  on room change, in F10 and in the "You are in the game" line. Station F fallback now < 12 m for people and
  things; "Blocked ... close enough, press F" for both.
- NEXT: user tests Z in bar/launch bay + flight test (guide Part 8); read log ("room service keys", flight lines).

### Earlier: RESUME (2026-10-25 ~8 AM) - v0.8.0 deployed, UNTESTED; user tests next, then wants Silent Hill f

- User test 05:36-06:00 (0.7.3): talk lines read in full (Riles, Geero) PROVEN. Got quest "The Ordnance Tomb"
  (Geero, items Titan's Map/Key, objective kill 3 Ordnance Titans; late-game per Geero's own lines). Stuck
  "Blocked" 9-10 m from Wreckage Vendor (counter). Launched: F at launchpad (prompt "F Travel", hover
  "Launchpad") -> scene UN01 "Alpha-Omega System, Union Space". In space cursor = invisible + Confined, so
  MenuNav treated flight as a menu ("No menu items"); user clicked HUD buttons (Formation [F4] -> FREE FLIGHT).
- Missions tab text "All The Little Things In Life" = string $allthelittlethingsinlife (HYPOTHESIS placeholder).
- 0.8.0: Flight.cs. Menu mode = Cursor.visible || lock None (flight no longer a menu). Contact list =
  ContactHUD.mainContactHUD.contentArea children (ContactButton.linkedSolar), sorted by distance, waypoints
  first (currentQuestWaypoint/MissionWaypoint/Waypoint). Category via switchToImportant/Ships/Solars/Wrecks/
  Cargo/All. Enter = ContactButton.activateButton(), fallback TargetingUI.openTarget (log "select ...:
  targeted"). F2 = game Goto; if autopilot still FREEFLIGHT after 0.8 s: Ship.enterDock(true,target) when
  dockable and within dockDistance, else Ship.enterGoto(target) (log "F2 fallback"). Steering: FixedUpdate
  rigidbody MoveRotation at 60 deg/s (Shift 20), Home = proportional homing, "On target" < 3 deg; blocked
  while autopilot != FREEFLIGHT. Watches: autoPilot enum changes, isInCruise, throttle on W/S release,
  hull/shield 25% buckets, hostile Ships < 6 km (getReactionToPlayer), TargetingUI.targetedSolar changes,
  target isDying. F10 in space = Flight.Status(). Station F fallback for people now < 10 m.
- Game manual text extracted from assets (level1/level14/level20: "<size=150%>" pages): docking = target +
  /dockwithtarget; lanelines = fly through the ring; contact window lower left; Freeplay warns that the
  campaign has the tutorials.
- Docs: Downloads\UnderspaceAccess\UNDERSPACE_GUIDE.txt = full how-to-play + all keys + test steps (Part 8).
- HYPOTHESES to check in the log: activateButton targets; game F2 works with our selection; GOTO to a station
  docks; MoveRotation sticks vs. the game's mouse PID ("steer:" line); Escape leaves a laneline.
- NEXT: read log ("contacts", "select", "F2", "autopilot:", "steer:", "enemies:"), fix; then job board,
  quest log reader, combat polish, inventory/equipment, crafting, map; Campaign later.

### Earlier: RESUME (2026-10-24 ~10:15 AM) - v0.7.3 deployed, UNTESTED; user away, back later today

- 0.7.2 test 09:52-09:55 (PROVEN): F fallback doInteraction opened Stationmaster + Signalman talks; F on
  Shipwright worked natively (lookingNPC = Shipwright, prompt "[F] Trade") -> shop. Directions + Q/E turn
  work (user: "directions work fine"); turn rotates PlayerController (MouseLookUnderspace MouseXAndY), sticks.
  Save check said "Saved as firstgame" (proven). PROBLEM: NPC lines cut off: "Starchaser. How can I assist
  you?" by "Looking at ..." and "What? Oh, no, nevermind." by "End" button 60 ms later. BUG: late after-F
  check (Navigator not ticked while menu open) reopened the shop after Close.
- 0.7.3: Speech.Protect(line) after talk/comms/subtitle lines (~0.065 s/char, max 8 s): interrupting
  speech is queued meanwhile; any player key press (MenuNav.Pressed) lifts it. "Looking at X." queued and
  not while a menu is open; hover text no longer spoken. After-F check skipped when late (menu opened).
- NEXT: user tests talk (hear full lines, then choices); then ship status -> space combat.

### Earlier: RESUME (2026-10-24 ~9:50 AM) v0.7.2

- 0.7.1 test 09:31-09:33: user walked to Stationmaster Ogatabanliter ("You are at ... Press F", straight ahead),
  pressed F (HYPOTHESIS, not logged) and NOTHING opened: no prompt/comms/quest text. Save worked
  (firstgame.unf 9:32:39) but SaveClicked found no typed name (field text empty after picking a list row).
  No turn log line: user did not need to turn (target was straight ahead).
- 0.7.2: F in station logged ("F pressed: lookingNPC ..., prompt, hover, target dist/angle", "after F: opened");
  if nothing opened after 0.7 s and the chosen place is < 4 m: StationNPC.doInteraction(false) /
  StationActivator.activateObject(0) fallback ("Talking to X"). "Looking at X. F to talk." when
  StationUI.lookingNPC changes; StationUI.hoverIconText watched. Save check = newest *.unf in
  persistentDataPath/SaveGames written since the click ("Saved as <file>"). Doors > 300 m dropped.
- NEXT: read log: does lookingNPC ever get set? does doInteraction open the talk? Then ship status -> space.

### Earlier: RESUME (2026-10-24 ~9:40 AM) v0.7.1

- 0.7.0 test 09:22-09:26: places lists WORKED (Doors 2, Things 5: Job Board/News Board/Tablet/"On Starship Anchors",
  Chairs 161, People 3); walking directions spoken (11 -> 4 m, "You are at Wreckage Vendor") but target stayed
  "behind you": user never turned (no "turn:" log line; user didn't know arrows turn). No interaction prompt seen.
  "Now in the room" (roomName empty, roomType not mapped) + room flicker right after load. Save not retried.
- 0.7.1: Q/E (and arrows) hold-to-turn, "Facing <name>. Let go." when passing the target, turn hints
  ("Turn right with E") in guidance/arrival/choose; chairs only in Chairs category; room name falls back to
  pc.currentRoom; room speech muted 8 s after scene load.
- HYPOTHESIS: yaw rotation sticks (check "turn:"/"turn check:" log); E/Q may trigger game actions in station.
- JAWS Underspace.jkm already passes arrows/Q/E/PageUp etc. (user asked for one; it exists and is active).

### Earlier: RESUME (2026-10-24 morning) v0.7.0

- User test 10/24 09:03-09:09 (0.6.1): F2-F7 room warps WORKED (F10 showed bar / launch bay / Underspace info)
  but were silent; save "firstgame" WORKED (appeared in list, then overwritten) but no confirmation spoken;
  F10 listed people with meters but no direction. User wants category list + manual WASD walking with
  spoken directions (autowalk NOT wanted).
- 0.7.0: Navigator.cs = places list in station (People = StationNPC, Doors = RoomWarp + roomToWarpTo,
  Things = StationActivator (activatorName or type name), Chairs = empty StationChair, Everything), filtered
  to interior.currentRoom children. PageUp/Down category, Up/Down place, End where, Home face, Delete off,
  hold Left/Right arrow turn (rotates the MouseLookUnderspace transform whose axes != MouseY; HYPOTHESIS that
  the yaw sticks - log "turn:" and "turn check:" lines). While W/A/S/D held: direction bucket spoken on change,
  meters every 3 s, "Blocked" if no movement 1.5 s, "You are at X" under 2.5 m.
  HYPOTHESIS: arrow keys may also move the player through Unity's legacy axes - check log/user.
  InGame.RoomChange: "Now in the <room>. N people here." MenuNav.SaveClicked/SaveCheck: "Saved as <name>".
- NEXT: read log after user test; then ship status -> space combat.

### Earlier: RESUME (2026-10-22 9:00 PM)

- 0.6.0 test 20:38: in station UHC-1 (Alpha-Omega System) after Create. Spoken: "Entering UHC-1..",
  HUD tips "Interact: F"/"Toggle Mouse: RMB" repeated every menu close (useless); F10 said only "System: UHC-1".
  User could not tell how to move; WASD/arrows gave no feedback (HYPOTHESIS: walking works but is silent).
  Right mouse toggles the cursor (menu mode) -> HUD buttons "[F2]" = UIStationRoomButton "Warp to launch bay".
- v0.6.1 deployed, UNTESTED: F10 in station = "Docked at <StationInterior.stationName>, in the <room>",
  people in room (StationNPC.NPCName + meters, nearest 6), "Room keys" (UIStationRoomButton
  linkedTutorialButton.linkedText + linkedElement.toolTip, else stationRoomTypes name), system + category,
  quest/objective. Shift+F10 = controls from MasterKeyboard.allKeys (commandName + parseKeyboardString("/"+parseReplaceName))
  written to Downloads\UnderspaceAccess\Underspace_Controls.txt + walk-ish keys spoken. Tips once per session.
  HUD junk (numbers, "Beta x", "Playtime") filtered from menu screen info. Enum namespace is Il2CppUnderspace.
- NEXT 10/23: read log; station navigation (scanner of NPCs/objects + autowalk, room keys), ship status,
  then space combat; crafting later.

### Earlier (2026-10-22 8:45 PM)

- v0.6.0 deployed, UNTESTED: InGame.cs polls game texts every 0.1 s and speaks changes (logged "game text [key]"):
  MessageHUD.mainDisplay + allMessages[].itemName, SystemNameReadout.systemName/bigsystemName (+category),
  CommsUI.wantedDialogueString (+currentCharacterName) while commsActive, QuestUI.wantedDialogueString
  (+npcName) while questMenu active, QuestUI.questName/questTrackerObjective, AcceptQuestHUD.questName,
  CinematicUI.stationSubtitles, StationUI.stationSubtitles/primary+secondaryInteractionUI (interrupt),
  StatsHUD.alertText/tutorialText/advanceText/respawnTitle, TutorialSystem.allActiveTooltips[].linkedTooltip
  and pausepopupDescription. In game (menu mode off): F10 where am I (system, quest, objective, prompt),
  F11 repeat last message. All field names from SDK stubs; HYPOTHESIS which ones are used in Freeplay.
- User plan: tomorrow 10/23 = SPACE COMBAT (also RE4R + Silent Hill f). Station walking/navigation is
  probably needed before flying (game starts in station UHC1, "[F3] Warp to launch bay").
- AGREED order (user 10/22 8:50 PM): station navigation + ship status -> space combat; CRAFTING also wanted.

### Earlier (2026-10-22 8:40 PM)

- 0.5.1 test 20:27 PASSED: Manual (11 topics from TutorialSystem.availableTutorialObjects), colour names
  (materials Ijuni_SkinN, Standard shader, texture average; characterPartName EMPTY, id in internalPartID),
  "Error: No Name Chosen" read after Create, in-game notice. Game allows Create without name on 2nd press.
- v0.5.2 deployed, UNTESTED: rows without paint/name use internalPartID ("ijuni_HasFronds" -> "Has Fronds");
  manual "/command" tokens -> keys via OptionsMenu.mainMenu.mainKeyboard.parseKeyboardString (MasterKeyboard),
  "*" bullets -> sentences, ALL-CAPS page titles lowered; ManualReader.IsOpen null guard (NRE on quit to title).
- NEXT (user to discuss): in-game speech (location, messages, tutorial popups, HUD), then flight/combat.

### Earlier (2026-10-22 8:35 PM)

- v0.5.1 deployed, UNTESTED. Test 20:19 (0.5.0) results: order/race description/Left-Right rows WORKED;
  colour names silent (no "colour" log line: part/material null); Manual screen = only Return
  (no TutorialButton objects on title); first Create click did nothing for 30 s (name blank?
  failure text not read); in game: silence.
- 0.5.1 fixes: ManualReader.cs reads TutorialObject data directly (TutorialSystem.availableTutorialObjects
  or Resources.FindObjectsOfTypeAll) whenever TutorialBackButton is on screen; colour rows try
  currentCharacterPart / linkedBodyPart / id lookup / renderer material and log "colour row ..." diagnostics;
  texts appearing after any click are read (logged "after click:"); "You are in the game..." once
  menus stay closed 1.5 s outside Title scenes.
- Freeplay rows seen for Vauldwin: Inner Mask, Outer Mask, Mask Filter, Robe Color, Button Color, Hair/Hat.

### Earlier (2026-10-22 8:15 PM)

- v0.5.0 deployed, UNTESTED: colour NAMES via ColorNames.cs (CharacterPart.swappedMaterial tint x blitted texture average -> HSV words, logged "colour ..."); character creation one item per colour row (Left/Right click the row's
  Previous/Next), order Name/Random name/Race/Loadout/colours/rest/Create, Rotate buttons hidden,
  race description read (LoginUI.raceDescription), RaceRandomButton = "Random name" (getRandomRaceName
  replaced user's typed name in the 20:02 test); loading watchdog thread (main thread frozen >3 s
  while game in front -> "Loading..."); Manual: TutorialButton objects get an added Button,
  Enter calls showTutorial() then reads TutorialSystem.tutorialTextFull/tutorialDescription
  (HYPOTHESIS: entries had no Selectable; log line "manual: gave N of M entries a button").
- User test 20:00-20:04 (0.4.1): name typed OK; Previous/Next rows confusing; after Create the game
  went to UHC1 with menu mode off = silence (user unsure it loaded). Manual unread.
- JAWS Underspace.jkm already existed (user didn't know); added Shift+F1/F2/F10/F11.
- Next after test: in-game speech (location, messages, tutorial popups, subtitles), pause menu.

### Earlier (2026-10-22 6:20 PM)

- Next session: Friday night 10/24 or Saturday 10/25. Player-facing to-do also copied to
  Downloads\UnderspaceAccess\STATUS_AND_TODO.txt (player looks in Downloads; keep in sync).
- Player likes space combat: priority after menus = flight/combat access (ship status, scanner,
  enemy warnings, aim help, autopilot). Nothing researched yet: HYPOTHESIS only.

### Earlier (2026-10-22 9:30 AM)

- Mod v0.4.1 installed and working on the title screen menus (all tested live by Claude).
- Character-creation labels fix (Previous/Next rows, "Race" dropdown) is installed but UNTESTED.
- Claude accidentally started a Campaign game while testing (got as far as the intro cutscene,
  scene PlanetVauldric, then closed the game). A campaign save/autosave may now exist: tell the user.
- To do next, in order:
  1. Test character creation labels (Freeplay) and the in-game pause menu.
  2. Subtitles / cutscene text reading (HUD/EffectHUD/GameSubtitles), dialogue windows,
     quest popups, tutorial popups (TutorialPopUpUIText): currently NOT read automatically.
  3. Station menus (trade, repair, ship dealer, missions) once the user reaches a station.
  4. "Loading" announcement during loads (campaign load blocks the game for ~2 minutes).
  5. Optional: a "screen changed" title for tab switches.

## Game

- Name: Underspace
- Store and install folder: Steam, app 1111930, C:\program files (x86)\steam\steamapps\common\Underspace
- Main executable: Underspace.exe (only one)
- Build proven on: Steam build 25659081, game version 1.56 (MelonLoader log), shown in game as "Beta 1.57.1"
- 32 or 64 bit: 64-bit
- Engine family: unity-il2cpp
- Engine and version: Unity 2022.3.62f2 (UnityPlayer.dll file version + MelonLoader log), metadata v31
- Anti-cheat or DRM: none found (no anti-cheat files); multiplayer uses FishNet

## Stages

- Recon (game-recon): done
- Injection (inject): done
- Speech (speech): done (Prism 0.18.2, backend JAWS)
- Screen text (screen-text): in progress (title menus done, in-game menus partly)
- Scanner (scanner): station places list done; space contact list in 0.8.0 (untested)
- Autowalk (autowalk): to do (not requested)

## Resources

- Official Underspace Modding SDK ships in the game folder (UnderspaceModdingSDK_010.zip):
  a Unity project with ~1,100 game scripts as stubs (fields/method names, no bodies).
  Extracted to sdk_scripts\ (ModdableScripts\UI\OptionsMenu.cs, Options\OptionsButton.cs...).
- Nexus: 4 mods for "underspace" (none accessibility), checked 2026-10-22 via GraphQL.
- GitHub search, Molitvan blind list, web search: no accessibility mod found (2026-10-22).

## Decisions

- Route: MelonLoader 0.7.3 (latest stable, 2026-05-14). Chosen 2026-10-22: works on Unity 2022.3 / metadata v31.
- Menu reader is our own keyboard navigator over all visible Selectables (game is mouse-driven;
  Unity's own Enter/arrow handling is disabled by clearing EventSystem selection every frame).
- Mod keys avoid the game's bindings (game uses F1-F9, Home, Tab, Backspace, letters):
  arrows, Enter/Space, Home/End, PageUp/Down, Escape (fallback = screen's Return button),
  F11 = repeat + description, F10 = read screen, Shift+F11 = UI dump.

## Proven facts

- Probe line seen every launch: "PROBE UnderspaceAccess <ver> build <time> loaded; 64-bit; ... MelonLoader 0.7.3".
- Saves: %USERPROFILE%/AppData/LocalLow/Pastaspace Interactive/Underspace/SaveGames/<character>/<name>.unf (+ .uwf), seen 2026-10-24.
- Mod log: <game>\UserData\UnderspaceAccess\UnderspaceAccess.log (rewritten each launch).
- Game types are in Il2CppUnderspaceGame.dll, namespace Il2Cpp; TMPro is Il2CppTMPro.
- Options rows are OptionsButton (linkedSlider/linkedDropdown/linkedInputField/linkedToggleOn/Off,
  linkedNode.optionName/optionDescription). On/off switches swap OnButton/OffButton when clicked.
- Key binding buttons are named BaseKeyboardKey / BaseKeyboardKeyAlt under ControllerButton<Action>;
  after the new key, the game may show "Choose Input Type" (Press/Hold/Doubletap).
- The game ignores Escape in title menus; the mod clicks Return/Back.
- Dropdown rows are rebuilt after a pick; focus recovery uses the same OptionsButton row.

## Game folder changes

- Added: version.dll, MelonLoader\ (folder), Mods\UnderspaceAccess.dll, UserData\ (Loader.cfg,
  UnderspaceAccess\prism.dll + third-party licence). Nothing replaced. Delete these to undo.
- JAWS: added %APPDATA%\Freedom Scientific\JAWS\2026\Settings\enu\Underspace.jkm and Underspace.jcf
  (copies of the UnityPlayer key map; copies also in this folder).

## Pitfalls

- Clicking a control makes Unity select it; Enter then also fired the game's own Submit (double action). Fixed 0.3.3.
- Escape fallback once clicked "Cancel Autopilot" key binding (label starts with Cancel) and the
  test press of Home got bound to it. Fixed 0.4.0 (list rows/key bindings excluded); controls
  restored with the game's Restore Defaults on the Controls tab.
- Settings change only persists after leaving Options with Return.
- Test settings restored: Player Damage 1.00, Hide UI Effects off, Scrollwheel = Equipment Menu.
- Campaign start blocks the game's main thread ~2 minutes (no mod output meanwhile).

## Test log

- 2026-10-27 13:10 user tested 0.9.2: X/A no-op, gates never jumped (GOTO stops 2 km), dock worked then F3 undocked. -> 0.9.3.

- 2026-10-25 07:19 user tested 0.9.1: jumped to Omega-Alpha, docked The Axle (after L), trade window unclear. -> 0.9.2.

- 2026-10-25 07:09 user tested 0.9.0: F3 route + F2 docking WORK; system routes "No path available"; map places 1 of 5. -> 0.9.1.

- 2026-10-25 06:57 user tested 0.8.2: contacts/select/F2 GOTO work; docking granted but no move; only 2 stations nearby. -> 0.9.0 map + F dock.

- 2026-10-25 06:44 user tested 0.8.1: Z/C work; space still seen as menu (lock None); keys one room late. -> 0.8.2.

- 2026-10-25 06:29 user tested 0.8.0 in station only: stuck 11 m from Job Board; F2 no-op in launch bay. -> 0.8.1 Z/C keys.

- 2026-10-25 05:36 user tested 0.7.3: talk OK, quest from Geero, launched; space unusable (flight seen as menu). See RESUME 10/25.

- 2026-10-24 09:52 user tested 0.7.2: F/talk works (fallback), lines cut off by menu speech; shop reopened bug.
- 2026-10-24 09:31 user tested 0.7.1: walked to Stationmaster, F did nothing visible; save OK without spoken name.
- 2026-10-24 09:22 user tested 0.7.0: lists + distances OK, could not turn (keys unknown); see RESUME 9:40.
- 2026-10-24 09:03 user tested 0.6.1: warps + save worked silently, F10 lacked directions; see RESUME 10/24.
- 2026-10-22 08:48 first launch: proxies generated, probe + JAWS backend OK, title reached.
- 2026-10-22 08:56 user tested 0.2.0: title + options read; tabs mixed in, slider/edit double; Escape did nothing.
- 2026-10-22 09:00-09:20 Claude live-tested 0.2-0.4: title, Options (all 4 tabs), dropdown lists,
  switches, sliders, key rebinding, New Game, Load Game, Multiplayer, Mods, Manual (empty), Escape: all OK.
- 2026-10-22 09:25 Claude accidentally started Campaign; game closed in the intro cutscene.
- 2026-10-22 20:38 user tested 0.6.0 in station: see RESUME 9:00 PM.
- 2026-10-22 20:27 user tested 0.5.1: manual, colours, Create error, in-game notice OK; Fronds row silent; manual /commands unclear.
- 2026-10-22 20:19 user tested 0.5.0: see RESUME 8:35.
- 2026-10-22 20:00 user tested 0.4.1 Freeplay char creation: readable but confusing (see RESUME); Create -> silent game.


