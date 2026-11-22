# Dungeon Keys

<img src="Cleric.png" alt="Dungeon Keys icon" width="64" align="right">

**Switch between your Happy Dungeons accounts with one click.**

Dungeon Keys is a small Windows program by **Totemo Fire**. It saves your accounts and swaps the active one before you launch the game, so you don't have to have multiple game installations.

## Download

Get the latest `Dungeon Keys v*.zip` from the [Releases](../../releases) page, unzip it anywhere, and double-click **Dungeon Keys.exe**. It only needs Windows 10 or 11, nothing else.

## How to use

1. Open **Dungeon Keys.exe**.
2. Click **Browse...** and choose the game's **Mount** folder.
3. Start the game and sign in to an account, then come back and click **Save current account**. Repeat for each account.
4. To switch, select an account and click **Switch to selected account**. Close the game first, then start it.
5. To start a brand-new account, click **New**. The game will ask you to create one on its next launch.

Other buttons: **Import...** adds an account by typing its details, **Edit...** changes a saved account, **Remove** deletes it from the list (the game and your server account are untouched).

## Is it safe?

- It only edits one file: `UserData\LocalState\WinDurango\winDurango.json` in the game folder. It changes `gamertag`, `gamerscore`, `xuidBase` and `secret`, and leaves the rest of the file alone.
- It never connects to the internet.
- Saved accounts stay on your PC in a `DungeonKeys` folder (in `AppData`, or next to the program if Windows doesn't allow that). They include login secrets, so treat that folder like a password.
- The program is a single readable source file: [`src/Program.cs`](src/Program.cs).
- On first launch, the first copy of your game's `winDurango.json` is backed up as `original-winDurango-backup.json` in that same folder.

### Windows or antivirus warnings

The exe isn't digitally signed, so Windows SmartScreen may say "Windows protected your PC" and some antivirus tools may flag it by mistake. Click **More info -> Run anyway**, or build it yourself (below).

### "Windows blocked access"

The program offers to restart with administrator permission. This is only asked when Windows blocks the game's files.

## Build from source

You don't need Visual Studio or the .NET SDK - Windows already includes the C# compiler.

```
build.bat
```

This produces `Dungeon Keys.exe`. To build a release zip, run `make-release.bat`; the result appears in `Release\`.

The code is C# (WinForms, .NET Framework 4) in a single file, with no external dependencies.

## How it works

`winDurango.json` contains the account the game logs in with. Dungeon Keys keeps a list of accounts and writes the chosen one into that file. The "New" button empties those four values, which makes the game ask the player to create a new account.

## License

[MIT](LICENSE) (c) Totemo Fire
