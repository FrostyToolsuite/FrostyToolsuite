# Installation

## Build from source

### Install dependencies
This project requires [.NET 8](https://learn.microsoft.com/en-us/dotnet/core/install/)

### Build instructions
Follow the [build instructions](https://github.com/FrostyToolsuite/FrostyToolsuite?tab=readme-ov-file#from-source).

The compiled executable will be in the `FrostyCLI/bin/Debug/net8.0/` directory.

## Nightly builds
Grab the latest CLI build for Windows or Linux from the [Github Actions](https://github.com/FrostyToolsuite/FrostyToolsuite/actions), compiled from the latest commit.

On Linux, set FrostyCli to be executable before using it:
```bash
chmod +x FrostyCli
```

# Usage
> [!WARNING]
> There is currently [a bug](https://github.com/McSimp/linoodle/issues/5) in FrostyCLI **on Linux** (Windows is unaffected) where it crashes when working with games that use Oodle 2.8.x. To resolve this issue, please download the oo2core_6_win64 DLL from an older game that uses Oodle and place it in the games directory.

## Overview
> [!NOTE]
> Mods made with Frosty 1.0.x must be converted using the UpdateMod option in interactive mode or with the update-mod argument before use with FrostyCLI.
```
Description:
  CLI app to load and mod games made with the Frostbite Engine.

Usage:
  FrostyCli [command] [options]

Options:
  --version       Show version information
  -?, -h, --help  Show help and usage information

Commands:
  load <game-path>                                  Load a games data from the cache or create it.
  mod <game-path> <mods-dir>                        Generates a ModData folder, which can be used to mod the game.
  update-mod <game-path> <mod-path>                 Updates a fbmod to the newest version.
  create-mod <game-path> <project-path> <--output>  Creates a mod from a project.
```

## Interactive mode
Using the interactive CLI mode:
```bash
$ ./FrostyCli
```
Example clip using the interactive mode to generate mod data:
[![GitHub Video](https://img.youtube.com/vi/Gy4sFT9FtVU/hqdefault.jpg)](https://youtu.be/Gy4sFT9FtVU)



After generating a mod data folder, pass the datapath argument to the games launch options to apply the mods as such:

```-dataPath "<mod data path>"```
the dataPath argument takes either absolute paths or paths relative to the games directory, so for example if your mod data folder is in the games folder, it'd look like:
```-dataPath "Moddata"```

Or as an alternative to the datapath launch command, you can use the `GAME_DATA_DIR` environment variable instead as such:

```GAME_DATA_DIR=<mod data path>```


> [!NOTE]
> Games of Frostbite version above 2014.4.11 require the following steps (check version in the games json file in the Profiles folder) 

### On Linux
Please copy the bcrypt.dll file from the ThirdParty folder and paste it into the games files, and add the wine DLL override before the datapath command as such:
```WINEDLLOVERRIDES="bcrypt=n,b" %command% -datapath '<mod data path>'```

### On Windows
For Windows users, please copy the CryptBase.dll file from the ThirdParty folder and paste it into the games files.
