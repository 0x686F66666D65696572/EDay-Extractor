# E-Day Extractor

An asset viewer and extractor for **Gears of War: E-Day** (PC / Xbox app), built on
[IndexV2](https://github.com/Wildenhaus/IndexV2) (via [PAK-Extractor](https://github.com/0x686F66666D65696572/PAK-Extractor))
and [CUE4Parse](https://github.com/FabianFG/CUE4Parse).

Point it at your game folder and it lists every texture, static mesh, skeletal mesh, geometry collection and config file in the
game's Unreal Engine 5 archives, in the same folder layout the game uses. Open anything to preview it, then export it.

## Using it

1. Download the latest build (see **Download** below), unzip it anywhere and run `EDayExtractor.exe`.
2. Click **Select Game Folder** and pick the game's root folder, e.g. `C:\XboxGames\Gears of War- E-Day`.
   Any folder inside the install (such as `...\Content\Paks`) works too. You can also drag the folder onto the
   window, or onto `EDayExtractor.exe`.
3. The asset tree appears on the left, grouped by type:

   | Category | What's in it | Viewer | Export |
   | --- | --- | --- | --- |
   | **Texture** | `Texture2D` assets (BC1-7, uncompressed, virtual textures) | 2D preview | DDS, PNG, TGA, HDR, BMP, TIFF, JPEG |
   | **Static Mesh** | `StaticMesh` assets, all LODs | 3D viewer with textures | FBX, OBJ, glTF, DAE, ... (+ textures) |
   | **Skeletal Mesh** | `SkeletalMesh` assets with skeleton and skin weights | 3D viewer with textures | FBX, glTF, DAE, ... (+ textures) |
   | **Geometry Collection** | Chaos destruction assets; several weapons' full models live only here (e.g. `COG_GnasherGeo_GP01_GeometryCollection`) | 3D viewer with textures | FBX, OBJ, glTF, DAE, ... (+ textures) |
   | **Text** | `.ini`, `.json`, `.xml`, `.txt`, `.uplugin`, ... | Text editor | Text |

   Double-click an asset to open it. In a viewer, **File > Export** saves it; the **Bulk Export** button in the
   Data Explorer exports many assets at once. Mesh exports can include the textures their materials use.

### Choosing which meshes to export

The mesh viewer's **Meshes** list has a checkbox per mesh, and exporting from the viewer only writes the ticked ones.

- **Select all** at the top of the list ticks or unticks every mesh the search box currently shows. It shows a
  partial mark when only some are ticked; clicking it then ticks them all.
- Combine it with the search: type `Nanite` and tick **Select all** to export only the full-detail meshes, or type
  `LOD2` to grab just one LOD.
- **Remove LODs** / **Remove Volumes** in the export dialog still apply on top of your selection.

The game folder you open is remembered in the launcher list, so next time it's a double-click.

### First launch

- The first time a game folder is opened, every package header is read to work out what each asset is.
  That takes about 10-60 seconds depending on your drive. The result is cached in `%LOCALAPPDATA%\EDayExtractor`,
  so later launches open in a few seconds. A game update invalidates the cache automatically.
- The Oodle decompression library is downloaded once into the same folder (from
  [OodleUE](https://github.com/WorkingRobot/OodleUE), as FModel does). An internet connection is needed for that first run.

## How it reads E-Day without mappings

E-Day is an Unreal Engine 5.7-era game. Its packages use *unversioned properties*, which normally can't be read
without a `.usmap` mappings file dumped from the running game, and there's no public one (Easy Anti-Cheat blocks dumping).

This tool doesn't need one. CUE4Parse is vendored in `external/CUE4Parse` with a small addition,
[`NoMappingsFallback`](external/CUE4Parse/CUE4Parse/UE4/Assets/NoMappingsFallback.cs): for each export it skips the
unreadable property block and finds where the native data (mesh buffers, texture mips) starts by trial-parsing
candidate offsets. Cheap byte-level checks rule out most offsets, and a parse is accepted only if it produces real
data (vertices, a skeleton, texture dimensions) and ends at (or just before) the end of the export.

What that means in practice:

- Meshes (including full-detail Nanite geometry), skeletons, skin weights, material slot names and textures come through fine.
- Anything stored only in properties is unavailable: material parameter values, for example.
  A mesh's textures are found from the textures its materials import, and the diffuse/normal maps are picked by name
  (`_d`, `_nmr`, ...).
- **If you have a `.usmap` for E-Day**, drop it into a `Mappings` folder next to `EDayExtractor.exe` (or in
  `%LOCALAPPDATA%\EDayExtractor\Mappings`) and it's used automatically for full property data.

### Nanite meshes

Most E-Day meshes (props, environments and characters) are **Nanite** meshes. Their full-detail geometry lives
only in the Nanite data; the regular LODs are a heavily reduced fallback (for example 1,369 vs 102,454 vertices
for a collectible helmet, or 33,695 vs 210,361 for Marcus). The viewer loads the Nanite geometry first, listed as
`<name>_Nanite_<section>`, and keeps the fallback LODs in the mesh list (unticked, flagged as LODs). Skeletal Nanite
meshes keep their skin weights. Use **Remove LODs** when exporting to get only the full-detail mesh.

### Weapons: look in Geometry Collection

Some weapons' skeletal mesh (e.g. `COG_Gnasher_GP01`) is only a placeholder quad in the game data. Their full
model is the matching `...Geo_GP01_GeometryCollection` in the **Geometry Collection** category, which loads at full
Nanite detail with one mesh per material (gun body, mods, scope, holo sight), so attachments can be unticked.
Its material list is a property, so without mappings it is recovered from the raw property data.

### Known limitations


- Audio (Wwise), animations, materials, maps and blueprints are not listed yet; they are readable through CUE4Parse
  and are good candidates for future categories.

## Download

GitHub Actions builds the app on every push (**Actions > Build > newest run > Artifacts > EDay-Extractor-win-x64**).
Pushing a tag that starts with `v` (e.g. `v1.0.0`) publishes a release zip on the **Releases** page.

Builds are self-contained: no .NET install is needed. Windows 10/11 x64 only.

## Building

Requirements: Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
git clone https://github.com/0x686F66666D65696572/EDay-Extractor
cd EDay-Extractor
dotnet build EDayExtractor.sln -c Release
```

To produce the same self-contained folder the CI publishes:

```
dotnet publish src/Index.App/Index.App.csproj -c Release -r win-x64 --self-contained true -o publish
```

### Layout

| Path | What |
| --- | --- |
| `src/Index.*`, `src/Modules/*` | The IndexV2 UI and core (launcher, data explorer, texture/mesh/text editors, export jobs), retargeted to .NET 10 |
| `src/Profiles/Index.Profiles.GearsEDay` | The E-Day game profile: mounting, asset classification, texture/mesh/text loaders, Assimp scene building |
| `external/CUE4Parse` | Vendored CUE4Parse ([upstream commit](external/CUE4Parse/UPSTREAM.txt)) with the no-mappings fallback |

## Credits and licenses

- **Index / IndexV2** by Haus and contributors: the UI, viewers and export pipeline this tool is built on.
- **CUE4Parse** by FabianFG and contributors (Apache-2.0, see `external/CUE4Parse/LICENSE` and `NOTICE`).
  Changes made here: `NoMappingsFallback.cs` (new), and small hooks in `UObject.cs`, `AbstractUePackage.cs`,
  `FArchive.cs` and `MeshDto.cs` (conversion: missing LOD info, Nanite on skeletal meshes), each marked in the code.
- This project's own code is MIT licensed (see `LICENSE`).

Gears of War: E-Day is © Microsoft / The Coalition. This is an unofficial fan tool; it ships no game data and only
reads files from an install you own.
