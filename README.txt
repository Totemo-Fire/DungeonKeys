DUNGEON KEYS
by Totemo Fire
==============================

Dungeon Keys lets you switch between your Happy Dungeons
accounts with one click.

HOW TO USE
1. Double-click "Dungeon Keys.exe".
2. Click "Browse..." and choose the game's "Mount" folder.
3. Start the game and sign in to an account as usual, then come back and click
   "Save current account". Repeat for each account you have.
4. To switch, pick an account in the list and click "Switch to selected account".
   Then start the game. (Close the game before switching.)
5. To start a brand-new account, click "New". The game will ask you to create
   one the next time you start it.

IS IT SAFE?
- The program only changes one file: UserData\LocalState\WinDurango\winDurango.json
  inside the game's folder (the account name, score and login details).
- It never connects to the internet.
- Your saved accounts stay on your own PC, in a "DungeonKeys" folder
  (inside your AppData folder, or next to the program if Windows does not
  allow that). Treat that folder like a password. Do not share it.
- The full source code is included (src\Program.cs). You can build the
  program yourself by double-clicking build.bat.

WINDOWS OR ANTIVIRUS WARNINGS
Because this is a small program that is not digitally signed, Windows may say
"Windows protected your PC", and some antivirus programs may flag it by mistake.
- Windows: click "More info", then "Run anyway".
- If you are unsure, build it yourself from the source (see above).

IF IT SAYS WINDOWS BLOCKED ACCESS
This can happen if the game's files belong to a different Windows user. The
program will offer to restart with administrator permission, click Yes and
confirm. This is only needed when Windows blocks access to the game's files.
