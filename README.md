# Simitone Packs

Add-on packs for [Simitone](https://github.com/Sleepsong/Simitone), with their source.

A pack is a folder you copy into Simitone's `Packs` directory and switch on in **Options > Packs**.
Some packs contain a compiled `.dll`. This repository is where that code comes from: you can read
what a pack does before you run it, and build it yourself instead of trusting a download.

These packs need a build of Simitone with add-on pack support, which at the moment is the
`addon-packs` branch.

## The packs

| Pack | Kind | What it does |
|---|---|---|
| [Build Cheats](BuildCheats) | Code | Adds the `offgrid` and `rotate45` console cheats (full 3D only): place objects off the grid and turn them 45 degrees at a time. |
| [Half Walls](HalfWalls) | Code, patches the engine | Adds The Sims 2's half wall to build mode: a low wall you can wallpaper, which Sims cannot cross but which does not divide a room. Experimental. |
| [Terrain Paint](TerrainPaint) | Code, patches the engine | Paint the ground with the game's terrain types (grass, dark and autumn grass, sand, rock, snow, cloud), as in The Sims 2. In build mode under Terrain. Painted ground is saved with each lot, in the pack's own file. Experimental. |
| [Roof Tool](RoofTool) | Code, patches the engine | Draw your own roofs, as in The Sims 2: hip, gable and shed roofs with a pitch each, in place of the automatic roof. In build mode under Roofs. Drawn roofs are saved with each lot, in the pack's own file. Experimental. |
| [Try for Baby](TryForBaby) | Code, patches the engine | Try for Baby, as in The Sims 2: WooHoo in the Vibromatic Heart Bed with a 50% chance of a baby, 1 in 10 of them twins. Needs Livin' Large and the game in English. Experimental. |
| [Foundations](Foundations) | Code, patches the engine | Foundations and decks, as in The Sims 2: raise a house's ground floor a few steps, with everything on it. In build mode under Terrain. The largest port here: an engine change of about 2,300 lines in 24 files, with nothing in the engine changed. |
| [Cut Content](CutContent) | Code, patches the engine | Restores features Maxis left unfinished in The Sims and its expansions: switched off interactions (hug a tree, children kiss toads, lock doors and others), the unused ghost sting and Superstar's Information Kiosk. A first stage, not yet run against a game. Needs the game in English. |
| [Test Cube](TestCube) | Content | One object, a plain grey cube with no behaviour, under Decorative > Other. For checking that content packs load. |

## Installing a pack

1. Get the pack's folder: download it from the latest run on this repository's **Actions** page, or
   build it (below). Test Cube needs no building; its folder is `TestCube/pack/testcube`.
2. Copy the folder into Simitone's `Packs` directory. On Linux that is
   `~/.local/share/Simitone/Packs/`; it is next to Simitone's `config.ini`.
3. In a lot, open **Options > Packs**, switch the pack on, and restart Simitone. A pack with code asks
   you to confirm first.

A code pack only works with the version of Simitone it was built against. If it does not match, the
Packs screen says so under the pack's name instead of loading it.

## Building

You need the .NET 9 SDK and Simitone's source, including its FreeSO submodule. Put it in a folder
called `Simitone` inside this repository or next to it:

```bash
git clone --recursive -b addon-packs https://github.com/Sleepsong/Simitone
dotnet build BuildCheats/BuildCheats.csproj -c Release
dotnet build HalfWalls/HalfWalls.csproj -c Release
dotnet build TerrainPaint/TerrainPaint.csproj -c Release
dotnet build RoofTool/RoofTool.csproj -c Release
dotnet build TryForBaby/TryForBaby.csproj -c Release
dotnet build Foundations/Foundations.csproj -c Release
dotnet build CutContent/CutContent.csproj -c Release
```

Each build leaves the finished pack in the project's `pack/` folder, for example
`HalfWalls/pack/halfwalls/`, holding `pack.json` and the `.dll`. To use a Simitone checkout somewhere
else, add `-p:SimitonePath=/path/to/Simitone`.

Test Cube's object file is committed, since it is data rather than code. It was written by
`TestCube/generator`, which can write it again:

```bash
dotnet run --project TestCube/generator -- TestCube/pack/testcube/content/Downloads
```

## Writing your own

`PACKS.md` in the Simitone repository describes the pack format and what a pack's code can do. The
packs here are meant to be copied from: Test Cube for content, Build Cheats for code that uses
Simitone's own hooks, Half Walls for code that patches the engine with Harmony, Terrain Paint for
a larger feature with its own build tool, drawing and saved data, and Roof Tool for one that takes
over part of what the engine does.

## License

Mozilla Public License 2.0, the same as Simitone and FreeSO. See [LICENSE.md](LICENSE.md).
