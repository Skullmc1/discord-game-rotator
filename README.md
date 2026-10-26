# Discord Game Rotator

**Can be used to complete Discord Quests and grind the new badges, but use at your own risk.**

A small Windows app that makes Discord think you are playing games, and switches to a different game every few seconds (5 by default).

It does not download or run any real games. It launches a tiny placeholder program renamed to match a game's executable, which is what Discord's game detection looks for.

## Features

- Uses Discord's own list of detectable games (about 10,000 Windows games), downloaded on first launch
- Search the list and build a rotation of games that loops in order
- Add 100 random games at once, optionally limited to games matching the search box
- Click and drag, Shift-click or Ctrl-click to select many games at once
- Reorder, remove or clear the rotation
- Adjustable swap interval, from 1 second to 1 hour
- Remembers your rotation between runs
- Placeholder processes close themselves if the app exits or crashes

## Requirements

- Windows with .NET Framework 4.x (included with Windows 10 and 11)
- No other installs needed. The C# compiler that ships with .NET Framework is used to build it.

## Building

Run `build.bat` in the project folder:

```
build.bat
```

This produces `GameRotator.exe`.

## Usage

1. Run `GameRotator.exe`. The first launch downloads Discord's game list and saves it as `detectable.json`. Use **Refresh list** to download it again later.
2. Find games with the search box and add them to the rotation with **Add >** or a double-click, or press **Add 100 random**.
3. Set the swap interval in seconds and press **Start**.
4. Press **Stop**, or close the app, to end the current game.

Discord checks running programs on its own schedule, so a very short interval may not give every game time to show up. If games are being skipped, try 15 to 30 seconds.

## How it works

Discord publishes the games it can detect at `https://discord.com/api/v9/applications/detectable`. Each entry lists one or more executable names, such as `cs2.exe` or `fortnite/fortniteclient-win64-shipping.exe`. Discord scans running processes and matches the end of each process path against those names.

For each game in the rotation, the app copies itself into `games\<application id>\<executable path>` and starts that copy with a `--stub` argument. In stub mode it only shows a small window titled with the game's name. When the interval passes, the app closes the current stub and starts the next one.

## Files created at runtime

These are created next to the exe and are ignored by git:

| File | Purpose |
| --- | --- |
| `detectable.json` | Cached copy of Discord's detectable games list |
| `rotation.txt` | Application IDs of the games in your rotation |
| `games\` | Renamed copies of the exe, one per game |
