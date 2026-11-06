# Shadow Mobs Macro

A Windows helper for the Roblox game **Idle Mafia**. It watches the game window and clicks for you:

- **Stamina Donator**: donates stamina to your family perks in the order you choose. Optionally levels every
  ticked perk to a set level first (e.g. all to 7), then carries on to 25.
- **Auto Jobs**: does jobs for you, in one of two modes:
  - **Job Mastery**: gold mastery on every job, lowest level first, then switches to Most XP per hour.
  - **Most XP per hour**: always the job with the best XP per energy (using your real mastery discounts and
    the XP you actually earn, boosts included).
- **Auto-Train Pet**: every 20 minutes opens Safehouse > Animals > Animal Training and presses TRAIN. If the
  pet is still resting it reads the "Train again in" timer and comes back then. Optional **feeding** on each
  visit: FEED 10% (food) when hunger is 90% or less, and/or FEED 50% (premium food) at 50% or less. With both
  ticked, premium food goes first and 10% tops it up.
- **Auto Deposit**: presses DEPOSIT ALL right after every 5-second cash payout, so other players can't take
  your cash. Optional **auto-buy withdrawal**: takes out a set amount 10 seconds before each shop restock
  (every 5 minutes on the clock) for the auto-buy pass, then deposits what's left. Skipped if you were
  attacked in the last minute.
- **Status panel** over the game: what it's doing, the job, XP/hr, XP and stamina this session, the next pet
  training, and runtime.

Tick any mix of them and press **START**. Together they run by priority:
1. do a job when there's enough energy for the next one,
2. train the pet when it's due (it costs 10 energy),
3. donate when there's enough stamina,
4. deposit cash the rest of the time.

Stamina is read once, when you press START, and then counted from its regen rate; energy is counted the same
way. So notices covering the bars don't hold things up.

## Requirements

- Windows 10 or 11.
- Roblox with Idle Mafia, ideally **fullscreen (F11)**.
- An English language pack in Windows (Settings > Time & language > Language). The tool reads the game's
  text with Windows' built-in text recognition.
- Nothing else to install: it uses the .NET Framework that ships with Windows.

## Getting started

1. Keep `Shadow Mobs Macro.exe` next to the `data` folder (it reads `data\jobs.txt`).
2. Open the game at the size you'll play at.
3. Press **CALIBRATE**. It walks through each feature in turn: a short checklist (which page to open), then
   a screenshot where you drag a box around each thing it asks for (the pet takes three screenshots: the
   Safehouse page, the Animals window as it opens on OWNED, then its Animal Training tab). Every step shows an example picture and
   explains why it's needed. The mouse wheel zooms; hold the mouse at an edge while dragging to slide the view.
4. Tick the features you want in the left-hand menu and press **START**. Press **F8** (or STOP) to stop.

Tips:

- **Turn off notifications** (Windows Do not disturb, Roblox notifications, Discord/Steam overlays). Popups
  over the game can cover what the tool reads.
- **Calibrate in the theme you play with**, and again if you change theme or screen size. Each screen size
  keeps its own calibration, so a desktop and a laptop can both be set up.
- With "Pause while I'm using the mouse or keyboard" on (Settings), touching the PC pauses it for a few seconds.
- **Check screen** on each page reports what the tool can see, without clicking anything.

## Files it creates

Next to the exe: `settings.txt`, `log.txt`, `job-progress.txt` (mastery), `job-xp.txt` (measured XP),
`pet-next.txt` (next pet training), `check-screen.png`, and `profiles\<width>x<height>\` (your calibrations). These are yours and are ignored by git.

## Building

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

This compiles `Shadow Mobs Macro.exe` with the C# compiler built into Windows; nothing to install.
`tools\make-examples.ps1` regenerates the calibration example pictures from `tools\source`, and
`tools\make-icon.ps1` makes `icon.ico` from `assets\logo.png`. The code is in `src\`: `Game` (screen capture,
clicks, text recognition), `Core` (calibration and settings), `Features\<name>` (one folder per feature) and
`UI` (the window).

## What's new in v1.01

- **Auto-Train Pet** (new): trains your active animal every 20 minutes, with optional feeding (FEED 10% at
  90% hunger or less, FEED 50% at 50% or less, premium food first). It needs calibrating: three screenshots.
- **New priority order**: jobs, then the pet, then stamina, with deposits the rest of the time (stamina
  used to go first).
- **Stamina is read once, at START**, then counted. Donation notices covering the stamina bar, or stacking
  over the HEALTH label, no longer stall the donator after 20 to 30 minutes. Each donation is confirmed by
  the game's "You gave N stamina" notice.
- **Status panel**: shows when the pet trains next, and what the tool is doing whichever features are ticked.
- **Fixed**: if the bar's "(+1 0:31)" countdown couldn't be read at START, the stamina count stopped rising
  and the first donation froze the tool for a moment.
- The version is shown in the window's title bar.

## Disclaimer

This is an unofficial fan-made tool, not affiliated with Roblox or the game's developers. Automating gameplay
may be against Roblox's Terms of Use or the game's rules, which could get an account penalised. Use it at
your own risk.
