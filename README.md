<p align="center">  
  <a href="https://frostytoolsuite.com/">
    <picture>
        <img src="./Resources/FrostyBannerChucky296.svg">
      </picture>
  </a>
</p>

<p align="center">
  <a title="Discord Server" href="https://discord.gg/nrq7G5Q9">
    <img alt="Discord Server" src="https://img.shields.io/discord/333086156478480384?color=green&label=DISCORD&logo=discord&logoColor=white">
  </a>
  <a title="Total Downloads" href="https://github.com/CadeEvs/FrostyToolsuite/releases/latest">
    <img alt="Total Downloads" src="https://img.shields.io/github/downloads/CadeEvs/FrostyToolsuite/latest/total?color=white&label=DOWNLOADS&logo=github">
  </a>
</p>

# About
The FrostyToolsuite is a modding tool for games running on DICE's Frostbite game engine.

This is a rewrite of the FrostyToolsuite in .NET 7, which is in early development and has no functional UI yet.

The old repository can be found at https://github.com/CadeEvs/FrostyToolsuite.

The goal of this rewrite is to clean up the code base and make it crossplatform with the use of [Avalonia UI](https://github.com/AvaloniaUI/Avalonia) and the [MVVM Community Toolkit](https://aka.ms/mvvmtoolkit/docs) instead of WPF.

## Structure
The Toolsuite is split up into multiple projects.

### FrostyEditor
A GUI application which is used to create mods.

### FrostyModManager
A GUI application which is used to select what mods to apply to the game.

### FrostySdk
A library which is used to access data from the game.

### FrostyModSupport
A library which is used to create modified data which the game can read.

### FrostyTypeSdkGenerator
A source generator which is used to improve the type sdk which gets dumped from the games memory.

# Getting Started

## Release
Download the latest release from the [release page](https://github.com/FrostyToolsuite/FrostyToolsuite/releases/latest).

## From source
Make sure u have [git](https://git-scm.com/downloads) and the [dotnet7 sdk](https://dotnet.microsoft.com/en-us/download/dotnet/7.0) installed and in your path.

Then just clone and build the Editor using these commands.
```
git clone https://github.com/FrostyToolsuite/FrostyToolsuite.git
cd FrostyToolsuite
dotnet build
```
After that the executable for the editor can be found in `FrostyToolsuite/FrostyEditor/bin/Debug/net7.0`, for the ModManager in `FrostyToolsuite/FrostyModManager/bin/Debug/net7.0`.

# Documentation
Todo

# Plugins
Todo

# Contributing
If you want to contribute to Frosty you can just fork this branch and make a pull request with your changes.
Before you do that please check the [CodingStandards.cs](https://github.com/FrostyToolsuite/FrostyToolsuite/blob/master/CodingStandards.cs) to check if your code follows those.
In the [Projects tab](https://github.com/orgs/FrostyToolsuite/projects/1) you can see what needs to be done, ideas of what can be done and stuff that is currently getting worked on or is already done.
If you decide to work on something it would be great if you could say that in the [#developer](https://discord.gg/BXJSBzgc) channel on the discord server.
