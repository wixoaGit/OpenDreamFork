﻿using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DMCompiler.Json;
using OpenDreamRuntime.Objects;
using OpenDreamRuntime.Objects.Types;
using OpenDreamRuntime.Procs;
using OpenDreamRuntime.Rendering;
using OpenDreamShared.Dream;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;
using Level = OpenDreamRuntime.Map.IDreamMapManager.Level;
using Cell = OpenDreamRuntime.Map.IDreamMapManager.Cell;

namespace OpenDreamRuntime.Map;

public sealed partial class DreamMapManager : IDreamMapManager {
    [Dependency] private readonly DreamManager _dreamManager = default!;
    [Dependency] private readonly AtomManager _atomManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly DreamObjectTree _objectTree = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

    // Set in Initialize
    private ServerAppearanceSystem _appearanceSystem = default!;
    private SharedMapSystem _mapSystem = default!;

    public Vector2i Size { get; private set; }
    public int Levels => _levels.Count;

    public DreamObjectArea DefaultArea => GetOrCreateArea(_defaultArea);

    private readonly List<Level> _levels = new();
    private readonly Dictionary<MapObjectJson, DreamObjectArea> _areas = new();

    // Set in Initialize
    private MapObjectJson _defaultArea = default!;
    private MapObjectJson _defaultTurf = default!;

    private List<DreamMapJson>? _jsonMaps = new();

    public void Initialize() {
        _appearanceSystem = _entitySystemManager.GetEntitySystem<ServerAppearanceSystem>();
        _mapSystem = _entitySystemManager.GetEntitySystem<MapSystem>();

        DreamObjectDefinition worldDefinition = _objectTree.World.ObjectDefinition;

        // Default area
        var defaultArea = worldDefinition.Variables["area"];
        if (!defaultArea.TryGetValueAsType(out var defaultAreaValue) &&
            defaultArea.TryGetValueAsFloatCoerceNull(out var areaInt) && areaInt == 0) //TODO: Properly handle disabling default area
            defaultAreaValue = _objectTree.Area;
        if(defaultAreaValue?.ObjectDefinition.IsSubtypeOf(_objectTree.Area) is not true)
            throw new Exception("bad area");

        //Default turf
        var defaultTurf = worldDefinition.Variables["turf"];
        if (!defaultTurf.TryGetValueAsType(out var defaultTurfValue) &&
            defaultTurf.TryGetValueAsFloatCoerceNull(out var turfInt) && turfInt == 0) //TODO: Properly handle disabling default turf
            defaultTurfValue = _objectTree.Turf;
        if(defaultTurfValue?.ObjectDefinition.IsSubtypeOf(_objectTree.Turf) is not true)
            throw new Exception("bad turf");

        _defaultArea = new MapObjectJson(defaultAreaValue.Id);
        _defaultTurf = new MapObjectJson(defaultTurfValue.Id);
    }

    public void UpdateTiles() {
        foreach (Level level in _levels) {
            if (level.QueuedTileUpdates.Count == 0)
                continue;

            List<(Vector2i, Tile)> tiles = new(level.QueuedTileUpdates.Count);
            foreach (var tileUpdate in level.QueuedTileUpdates) {
                tiles.Add( (tileUpdate.Key, tileUpdate.Value) );
            }

            _mapSystem.SetTiles(level.Grid, tiles);
            level.QueuedTileUpdates.Clear();
        }
    }

    public void LoadMaps(List<DreamMapJson>? maps) {
        var world = _dreamManager.WorldInstance;
        var maxX = (int)world.ObjectDefinition.Variables["maxx"].UnsafeGetValueAsFloat();
        var maxY = (int)world.ObjectDefinition.Variables["maxy"].UnsafeGetValueAsFloat();
        var maxZ = (int)world.ObjectDefinition.Variables["maxz"].UnsafeGetValueAsFloat();

        if (maps != null) {
            foreach (var map in maps) {
                maxX = Math.Max(maxX, map.MaxX);
                maxY = Math.Max(maxY, map.MaxY);
                maxZ = Math.Max(maxZ, map.MaxZ);
            }
        }

        Size = new Vector2i(maxX, maxY);
        SetZLevels(maxZ);

        if (maps != null) {
            _jsonMaps = maps;

            // Load turfs and areas of compiled-in maps, recursively calling <init>, but suppressing all New
            foreach (var map in maps) {
                foreach (MapBlockJson block in map.Blocks) {
                    LoadMapAreasAndTurfs(block, map.CellDefinitions);
                }
            }
        }
    }

    public void InitializeAtoms() {
        // Call New() on all /area in this particular order, each with waitfor=FALSE
        var seenAreas = new HashSet<DreamObject>();
        for (var z = 1; z <= Levels; ++z) {
            for (var y = 1; y <= Size.Y; ++y) {
                for (var x = 1; x <= Size.X; ++x) {
                    var area = _levels[z - 1].Cells[x - 1, y - 1].Area;
                    if (seenAreas.Add(area)) {
                        area.SpawnProc("New");
                    }
                }
            }
        }

        // Also call New() on all /area not in the grid.
        // This may call New() a SECOND TIME. This is intentional.
        foreach (var thing in _atomManager.EnumerateAtoms(_objectTree.Area)) {
            if (seenAreas.Add(thing)) {
                thing.SpawnProc("New");
            }
        }

        // Call New() on all /turf in the grid, each with waitfor=FALSE
        for (var z = 1; z <= Levels; ++z) {
            for (var y = Size.Y; y >= 1; --y) {
                for (var x = Size.X; x >= 1; --x) {
                    _levels[z - 1].Cells[x - 1, y - 1].Turf.SpawnProc("New");
                }
            }
        }

        if (_jsonMaps != null) {
            // new() up /objs and /mobs from compiled-in maps
            foreach (var map in _jsonMaps) {
                foreach (MapBlockJson block in map.Blocks) {
                    LoadMapObjectsAndMobs(block, map.CellDefinitions);
                }
            }

            // No longer needed
            _jsonMaps = null;
        }
    }

    private void SetTurf(Vector2i pos, int z, DreamObjectDefinition type, DreamProcArguments creationArguments) {
        if (IsInvalidCoordinate(pos, z))
            throw new ArgumentException("Invalid coordinates");

        var cell = _levels[z - 1].Cells[pos.X - 1, pos.Y - 1];

        cell.Turf.SetTurfType(type);

        MutableAppearance turfAppearance = _atomManager.GetAppearanceFromDefinition(cell.Turf.ObjectDefinition);
        SetTurfAppearance(cell.Turf, turfAppearance);

        cell.Turf.InitSpawn(creationArguments);
    }

    public void SetTurf(DreamObjectTurf turf, DreamObjectDefinition type, DreamProcArguments creationArguments) {
        SetTurf((turf.X, turf.Y), turf.Z, type, creationArguments);
    }

    /// <summary>
    /// Caches the turf/area appearance pair instead of recreating and re-registering it for every turf in the game.
    /// This is cleared out when an area appearance changes
    /// </summary>
    private readonly Dictionary<ValueTuple<MutableAppearance, uint>, MutableAppearance> _turfAreaLookup = new();

    public void SetTurfAppearance(DreamObjectTurf turf, MutableAppearance appearance) {
        appearance.EnabledMouseEvents = _atomManager.GetEnabledMouseEvents(turf);

        if(turf.Cell.Area.Appearance != _appearanceSystem.DefaultAppearance)
            if(!appearance.Overlays.Contains(turf.Cell.Area.Appearance)) {
                if(!_turfAreaLookup.TryGetValue((appearance, turf.Cell.Area.Appearance.MustGetId()), out var newAppearance)) {
                    newAppearance = MutableAppearance.GetCopy(appearance);
                    newAppearance.Overlays.Add(turf.Cell.Area.Appearance);
                    _turfAreaLookup.Add((appearance, turf.Cell.Area.Appearance.MustGetId()), newAppearance);
                }

                appearance = newAppearance;
            }

        var immutableAppearance = _appearanceSystem.AddAppearance(appearance);

        var level = _levels[turf.Z - 1];
        uint turfId = immutableAppearance.MustGetId();
        level.QueuedTileUpdates[(turf.X, turf.Y)] = new Tile((int)turfId);
        turf.Appearance = immutableAppearance;
    }

    public void SetAreaAppearance(DreamObjectArea area, MutableAppearance appearance) {
        //if an area changes appearance, invalidate the lookup
        _turfAreaLookup.Clear();
        var oldAppearance = area.Appearance;
        appearance.AppearanceFlags |= AppearanceFlags.ResetColor | AppearanceFlags.ResetAlpha | AppearanceFlags.ResetTransform;
        area.Appearance  = _appearanceSystem.AddAppearance(appearance);

        //get all unique turf appearances
        //create the new version of each of those appearances
        //for each turf, update the appropriate ID

        Dictionary<ImmutableAppearance, ImmutableAppearance> oldToNewAppearance = new();
        foreach (var turf in area.Turfs) {
            if(oldToNewAppearance.TryGetValue(turf.Appearance, out var newAppearance))
                turf.Appearance = newAppearance;
            else {
                MutableAppearance turfAppearance = _atomManager.MustGetAppearance(turf).ToMutable();

                turfAppearance.Overlays.Remove(oldAppearance);
                turfAppearance.Overlays.Add(area.Appearance);
                newAppearance = _appearanceSystem.AddAppearance(turfAppearance);
                oldToNewAppearance.Add(turf.Appearance, newAppearance);
                turf.Appearance = newAppearance;
            }

            var level = _levels[turf.Z - 1];
            uint turfId = newAppearance.MustGetId();
            level.QueuedTileUpdates[(turf.X, turf.Y)] = new Tile((int)turfId);
        }
    }

    public bool TryGetCellAt(Vector2i pos, int z, [NotNullWhen(true)] out Cell? cell) {
        if (IsInvalidCoordinate(pos, z) || !_levels.TryGetValue(z - 1, out var level)) {
            cell = null;
            return false;
        }

        cell = level.Cells[pos.X - 1, pos.Y - 1];
        return true;
    }

    public bool TryGetTurfAt(Vector2i pos, int z, [NotNullWhen(true)] out DreamObjectTurf? turf) {
        if (TryGetCellAt(pos, z, out var cell)) {
            turf = cell.Turf;
            return true;
        }

        turf = null;
        return false;
    }

    //Returns an area loaded by a DMM
    //Does not include areas created by DM code
    private DreamObjectArea GetOrCreateArea(MapObjectJson prototype) {
        if (!_areas.TryGetValue(prototype, out DreamObjectArea? area)) {
            var definition = CreateMapObjectDefinition(prototype);
            area = new DreamObjectArea(definition);
            area.InitSpawn(new());
            _areas.Add(prototype, area);
        }

        return area;
    }

    public void SetWorldSize(Vector2i size) {
        Vector2i oldSize = Size;

        var newX = Math.Max(oldSize.X, size.X);
        var newY = Math.Max(oldSize.Y, size.Y);

        Size = (newX, newY);

        if(Size.X > oldSize.X || Size.Y > oldSize.Y) {
            foreach (Level existingLevel in _levels) {
                var oldCells = existingLevel.Cells;

                existingLevel.Cells = new Cell[Size.X, Size.Y];
                for (var x = 1; x <= Size.X; x++) {
                    for (var y = 1; y <= Size.Y; y++) {
                        if (x <= oldSize.X && y <= oldSize.Y) {
                            existingLevel.Cells[x - 1, y - 1] = oldCells[x - 1, y - 1];
                            continue;
                        }

                        var defaultTurfDef = _objectTree.GetTreeEntry(_defaultTurf.Type).ObjectDefinition;
                        var defaultTurf = new DreamObjectTurf(defaultTurfDef, x, y, existingLevel.Z);
                        var cell = new Cell(DefaultArea, defaultTurf);
                        defaultTurf.Cell = cell;
                        existingLevel.Cells[x - 1, y - 1] = cell;
                        SetTurf(new Vector2i(x, y), existingLevel.Z, defaultTurfDef, new());
                    }
                }
            }
        }

        if (Size.X > size.X || Size.Y > size.Y) {
            Size = size;

            foreach (Level existingLevel in _levels) {
                var oldCells = existingLevel.Cells;

                existingLevel.Cells = new Cell[size.X, size.Y];
                for (var x = 1; x <= oldSize.X; x++) {
                    for (var y = 1; y <= oldSize.Y; y++) {
                        if (x > size.X || y > size.Y) {
                            var deleteCell = oldCells[x - 1, y - 1];
                            deleteCell.Turf.Delete();
                            _mapSystem.SetTile(existingLevel.Grid, new Vector2i(x, y), Tile.Empty);
                            foreach (var movableToDelete in deleteCell.Movables) {
                                movableToDelete.Delete();
                            }
                        } else {
                            existingLevel.Cells[x - 1, y - 1] = oldCells[x - 1, y - 1];
                        }
                    }
                }
            }
        }
    }

    public void SetZLevels(int levels) {
        if (levels > Levels) {
            var defaultTurfDef = _objectTree.GetTreeEntry(_defaultTurf.Type).ObjectDefinition;

            for (int z = Levels + 1; z <= levels; z++) {
                MapId mapId = new(z);
                _mapSystem.CreateMap(mapId);

                var grid = _mapManager.CreateGridEntity(mapId);
                Level level = new Level(z, grid, defaultTurfDef, DefaultArea, Size);
                _levels.Add(level);

                for (int x = 1; x <= Size.X; x++) {
                    for (int y = 1; y <= Size.Y; y++) {
                        Vector2i pos = (x, y);

                        SetTurf(pos, z, defaultTurfDef, new());
                    }
                }
            }

            UpdateTiles();
        } else if (levels < Levels) {
            _levels.RemoveRange(levels, Levels - levels);
            for (int z = Levels; z > levels; z--) {
                _mapSystem.DeleteMap(new MapId(z));
            }
        }
    }

    private bool IsInvalidCoordinate(Vector2i pos, int z) {
        return pos.X < 1 || pos.X > Size.X ||
               pos.Y < 1 || pos.Y > Size.Y ||
               z < 1 || z > Levels;
    }

    private void LoadMapAreasAndTurfs(MapBlockJson block, Dictionary<string, CellDefinitionJson> cellDefinitions) {
        int blockX = 1;
        int blockY = 1;

        // Order here doesn't really matter because it's not observable.
        foreach (string cell in block.Cells) {
            CellDefinitionJson cellDefinition = cellDefinitions[cell];
            DreamObjectArea area = GetOrCreateArea(cellDefinition.Area ?? _defaultArea);

            Vector2i pos = (block.X + blockX - 1, block.Y + block.Height - blockY);

            _levels[block.Z - 1].Cells[pos.X - 1, pos.Y - 1].Area = area;
            SetTurf(pos, block.Z, CreateMapObjectDefinition(cellDefinition.Turf ?? _defaultTurf), new());

            blockX++;
            if (blockX > block.Width) {
                blockX = 1;
                blockY++;
            }
        }
    }

    private void LoadMapObjectsAndMobs(MapBlockJson block, Dictionary<string, CellDefinitionJson> cellDefinitions) {
        // The order we call New() here should be (1,1), (2,1), (1,2), (2,2)
        int blockY = block.Y;
        foreach (var row in block.Cells.Chunk(block.Width).Reverse()) {
            int blockX = block.X;
            foreach (var cell in row) {
                CellDefinitionJson cellDefinition = cellDefinitions[cell];

                if (TryGetTurfAt((blockX, blockY), block.Z, out var turf)) {
                    foreach (MapObjectJson mapObject in cellDefinition.Objects) {
                        var objDef = CreateMapObjectDefinition(mapObject);

                        // TODO: Use modified types during compile so this hack isn't necessary
                        DreamObject obj;
                        if (objDef.IsSubtypeOf(_objectTree.Mob)) {
                            obj = new DreamObjectMob(objDef);
                        } else if (objDef.IsSubtypeOf(_objectTree.Movable)) {
                            obj = new DreamObjectMovable(objDef);
                        } else if (objDef.IsSubtypeOf(_objectTree.Atom)) {
                            obj = new DreamObjectAtom(objDef);
                        } else {
                            obj = new DreamObject(objDef);
                        }

                        obj.InitSpawn(new(new DreamValue(turf)));
                    }
                }

                ++blockX;
            }

            ++blockY;
        }
    }

    private DreamObjectDefinition CreateMapObjectDefinition(MapObjectJson mapObject) {
        DreamObjectDefinition definition = _objectTree.GetObjectDefinition(mapObject.Type);
        if (mapObject.VarOverrides?.Count > 0) {
            definition = new DreamObjectDefinition(definition);

            foreach (KeyValuePair<string, object> varOverride in mapObject.VarOverrides) {
                if (definition.HasVariable(varOverride.Key)) {
                    definition.Variables[varOverride.Key] = _objectTree.GetDreamValueFromJsonElement(varOverride.Value);
                }
            }
        }

        return definition;
    }

    public EntityUid GetZLevelEntity(int z) {
        return _levels[z - 1].Grid.Owner;
    }
}

public interface IDreamMapManager {
    public sealed class Level {
        public readonly int Z;
        public readonly Entity<MapGridComponent> Grid;
        public Cell[,] Cells;
        public readonly Dictionary<Vector2i, Tile> QueuedTileUpdates = new();

        public Level(int z, Entity<MapGridComponent> grid, DreamObjectDefinition turfType, DreamObjectArea area, Vector2i size) {
            Z = z;
            Grid = grid;

            Cells = new Cell[size.X, size.Y];
            for (int x = 0; x < size.X; x++) {
                for (int y = 0; y < size.Y; y++) {
                    var turf = new DreamObjectTurf(turfType, x + 1, y + 1, z);
                    var cell = new Cell(area, turf);

                    turf.Cell = cell;
                    Cells[x, y] = cell;
                }
            }
        }
    }

    public sealed class Cell {
        public DreamObjectArea Area {
            get => _area;
            set {
                _area.Turfs.Remove(Turf);
                _area.ResetCoordinateCache();

                var oldArea = _area;
                _area = value;
                _area.Turfs.Add(Turf);
                _area.ResetCoordinateCache();

                Turf.OnAreaChange(oldArea);
            }
        }

        public readonly DreamObjectTurf Turf;
        public readonly List<DreamObjectMovable> Movables = new();

        private DreamObjectArea _area;

        public Cell(DreamObjectArea area, DreamObjectTurf turf) {
            Turf = turf;
            _area = area;
            Area = area;
        }
    }

    public Vector2i Size { get; }
    public int Levels { get; }
    public DreamObjectArea DefaultArea { get; }

    public void Initialize();
    public void LoadMaps(List<DreamMapJson>? maps);
    public void InitializeAtoms();
    public void UpdateTiles();

    public void SetTurf(DreamObjectTurf turf, DreamObjectDefinition type, DreamProcArguments creationArguments);
    public void SetTurfAppearance(DreamObjectTurf turf, MutableAppearance appearance);
    public void SetAreaAppearance(DreamObjectArea area, MutableAppearance appearance);
    public bool TryGetCellAt(Vector2i pos, int z, [NotNullWhen(true)] out Cell? cell);
    public bool TryGetTurfAt(Vector2i pos, int z, [NotNullWhen(true)] out DreamObjectTurf? turf);
    public void SetZLevels(int levels);
    public void SetWorldSize(Vector2i size);
    public EntityUid GetZLevelEntity(int z);

    public IEnumerable<AtomDirection> CalculateSteps((int X, int Y, int Z) loc, (int X, int Y, int Z) dest, int distance);
}
