# Shadow Mobs Macro

A Windows helper for the Roblox game **Idle Mafia**. It watches the game window and clicks for you:

- **Stamina Donator**: donates stamina to your family perks in the order you choose. Optionally levels every
  ticked perk to a set level first (e.g. all to 7), then carries on to 25.
- **Auto Jobs**: does jobs for you, in one of two modes:
  - **Job Mastery**: gold mastery on every job, lowest level first, then switches to Most XP per hour.
  - **Most XP per hour**: always the job with the best XP per energy (using your real mastery discounts and
    the XP you actually earn, boosts included).
- **Auto Deposit**: presses DEPOSIT ALL right after every 5-second cash payout, so other players can't take
  your cash. Optional **auto-buy withdrawal**: takes out a set amount 10 seconds before each shop restock
  (every 5 minutes on the clock) for the auto-buy pass, then deposits what's left. Skipped if you were
  attacked in the last minute.
- **Status panel** over the game: what it's doing, the job, XP/hr, XP and stamina this session, and runtime.

Tick any mix of the three and press **START**. Together they run by priority:
1. donate when there's enough stamina,
2. do a job when there's enough energy for the next one,
3. deposit cash the rest of the time.

Stamina and energy are tracked from their regen rates, so notices covering the bars don't hold things up.

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
   a screenshot where you drag a box around each thing it asks for. Every step shows an example picture and
   explains why it's needed. The mouse wheel zooms; hold the mouse at an edge while dragging to slide the view.
4. Tick the features you want in the left-hand menu and press **START**. Press **F8** (or STOP) to stop.

Tips:

- **Calibrate every time the theme changes**, and again if you change theme or screen size. Each screen size
  keeps its own calibration, so a desktop and a laptop can both be set up.
- With "Pause while I'm using the mouse or keyboard" on (Settings), touching the PC pauses it for a few seconds.
- **Check screen** on each page reports what the tool can see, without clicking anything.

## Files it creates

Next to the exe: `settings.txt`, `log.txt`, `job-progress.txt` (mastery), `job-xp.txt` (measured XP),
`check-screen.png`, and `profiles\<width>x<height>\` (your calibrations). These are yours and are ignored by git.

## Building

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

This compiles `Shadow Mobs Macro.exe` with the C# compiler built into Windows; nothing to install.
`tools\make-examples.ps1` regenerates the calibration example pictures from `tools\source`, and
`tools\make-icon.ps1` makes `icon.ico` from `assets\logo.png`. `CLAUDE.md` describes how the code is laid out.

## Disclaimer

This is an unofficial fan-made tool, not affiliated with Roblox or the game's developers. Automating gameplay
may be against Roblox's Terms of Use or the game's rules, which could get an account penalised. Use it at
your own risk.
