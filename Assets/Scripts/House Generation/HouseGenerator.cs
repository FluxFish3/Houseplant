using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class HouseGenerator : MonoBehaviour
{
    public HouseMesh meshBuilder; // drag your mesh builder here

    [Header("Grid Size")]
    public int width = 20;
    public int height = 20;
    public float cellSize = 1f;

    [Header("Room Requirements")]
    public RoomRequirement[] roomRequirements;

    [Header("Placement Tuning")]
    public int placementAttemptsPerRoom = 40;

    [Header("Stairs")]
    public int stairMinCount = 1;
    public int stairMaxCount = 2;
    public int stairPlacementAttemptsPerStair = 60;
    public RoomType[] stairAdjacencyTargets;

    [Header("Stair Shape")]
    public int stairShortSide = 2;
    public int stairLongSide = 3;

    [Header("Floors")]
    public int floors = 2;
    public float floorOffset = 3f;

    // per-floor placed rooms are constructed during generation
    private List<RoomRect> placedRooms = new List<RoomRect>();

    // per-floor grids & stair edges
    private List<HouseGrid> grids = new List<HouseGrid>();
    private List<HashSet<(int, int, CellDirection)>> stairEdgesPerFloor = new List<HashSet<(int, int, CellDirection)>>();

    // working reference to the current grid while generating a floor
    private HouseGrid grid;

    // ================= STAIRS: ENTRANCE DATA =================
    // (cellX, cellY, direction outward from this cell that is open)
    // kept per-floor in stairEdgesPerFloor; this getter is provided for compatibility
    public HashSet<(int, int, CellDirection)> GetStairEntranceEdges(int floorIndex)
    {
        if (floorIndex < 0 || floorIndex >= stairEdgesPerFloor.Count)
            return new HashSet<(int, int, CellDirection)>();
        return stairEdgesPerFloor[floorIndex];
    }

    // getters for mesh builder
    public int GetFloorCount() => Mathf.Max(1, floors);
    public float GetFloorOffset() => floorOffset;
    public HouseGrid GetGrid(int floorIndex)
    {
        if (floorIndex < 0 || floorIndex >= grids.Count) return null;
        return grids[floorIndex];
    }

    // ================= GENERATION =================

    [ContextMenu("Generate House")]
    public void GenerateHouse()
    {
        grids.Clear();
        stairEdgesPerFloor.Clear();

        // Precompute a single room plan to reuse on every floor
        var roomPlan = BuildFloorPlan();

        // Generate top-down so lower-floor stairs can validate adjacency against the floor above
        for (int floor = GetFloorCount() - 1; floor >= 0; floor--)
        {
            // set current working grid
            grid = new HouseGrid(width, height);
            placedRooms.Clear();

            // temporary set for this floor's stair edges
            var thisFloorStairEdges = new HashSet<(int, int, CellDirection)>();

            // place rooms
            PlaceClusteredRooms(roomPlan);

            // place stairs unless this is the topmost floor
            if (floor != GetFloorCount() - 1)
            {
                // pass the grid above (we already generated it and inserted at front of grids list)
                HouseGrid gridAbove = grids[0]; // grids[0] is the previously created (upper) floor
                PlaceStairsBatch(thisFloorStairEdges, gridAbove);
            }

            // store this floor data at the front so grids[0] always represents the topmost generated floor
            grids.Insert(0, grid);
            stairEdgesPerFloor.Insert(0, thisFloorStairEdges);

            // clear working grid reference
            grid = null;
        }

        // finally build meshes for all floors
        meshBuilder.BuildFloors();
    }

    private void Update()
    {
        if (Keyboard.current.gKey.wasPressedThisFrame)
            GenerateHouse();
    }

    // ================= CLUSTERED ROOMS =================

    void PlaceClusteredRooms(List<PlannedRoom> roomPlan)
    {
        for (int i = 0; i < roomPlan.Count; i++)
        {
            bool placed = TryPlaceClusteredRoom(roomPlan[i], i == 0);

            if (!placed)
                Debug.LogWarning($"Failed to place room: {roomPlan[i].type}");
        }
    }

    bool TryPlaceClusteredRoom(PlannedRoom plan, bool isFirstRoom)
    {
        for (int attempt = 0; attempt < placementAttemptsPerRoom; attempt++)
        {
            int w = Random.Range(plan.minSize.x, plan.maxSize.x + 1);
            int h = Random.Range(plan.minSize.y, plan.maxSize.y + 1);

            RoomRect candidate;

            if (isFirstRoom)
            {
                // Center the first placed room in the grid for the chosen size.
                int x = (width - w) / 2;
                int y = (height - h) / 2;
                candidate = new RoomRect(x, y, w, h);
            }
            else
            {
                candidate = GetClusteredPosition(w, h);
                if (candidate.w == 0) continue;
            }

            if (!IsInsideGrid(candidate)) continue;
            if (OverlapsAny(candidate)) continue;

            PlaceRoom(candidate, plan.type);
            return true;
        }

        return false;
    }

    RoomRect GetClusteredPosition(int w, int h)
    {
        if (placedRooms.Count == 0)
            return new RoomRect(0, 0, 0, 0);

        RoomRect anchor = placedRooms[Random.Range(0, placedRooms.Count)];
        int side = Random.Range(0, 4);

        int x = 0, y = 0;

        switch (side)
        {
            case 0: // above
                x = Random.Range(anchor.x - w + 1, anchor.x + anchor.w);
                y = anchor.y + anchor.h;
                break;

            case 1: // below
                x = Random.Range(anchor.x - w + 1, anchor.x + anchor.w);
                y = anchor.y - h;
                break;

            case 2: // left
                x = anchor.x - w;
                y = Random.Range(anchor.y - h + 1, anchor.y + anchor.h);
                break;

            case 3: // right
                x = anchor.x + anchor.w;
                y = Random.Range(anchor.y - h + 1, anchor.y + anchor.h);
                break;
        }

        return new RoomRect(x, y, w, h);
    }

    // ================= STAIRS =================

    void PlaceStairsBatch(HashSet<(int, int, CellDirection)> thisFloorStairEdges, HouseGrid gridAbove)
    {
        int target = Random.Range(stairMinCount, stairMaxCount + 1);
        int placed = 0;
        int attempts = 0;
        int safety = target * stairPlacementAttemptsPerStair * 2;

        while (placed < target && attempts < safety)
        {
            attempts++;

            if (TryPlaceSingleStair(thisFloorStairEdges, gridAbove))
                placed++;
        }

        if (placed < stairMinCount)
            Debug.LogWarning($"Only placed {placed}/{stairMinCount} minimum stairs");
    }

    bool TryPlaceSingleStair(HashSet<(int, int, CellDirection)> thisFloorStairEdges, HouseGrid gridAbove)
    {
        for (int attempt = 0; attempt < stairPlacementAttemptsPerStair; attempt++)
        {
            bool rotate = Random.value > 0.5f;

            int w = rotate ? stairLongSide : stairShortSide;
            int h = rotate ? stairShortSide : stairLongSide;

            int x = Random.Range(0, width - w);
            int y = Random.Range(0, height - h);

            RoomRect rect = new RoomRect(x, y, w, h);

            if (!IsInsideGrid(rect)) continue;
            if (OverlapsAny(rect)) continue;
            if (!HasValidStairEntrance(rect, gridAbove)) continue;
            if (TouchesOtherStairs(rect)) continue;

            PlaceRoom(rect, RoomType.Stairs);

            // Remove any floor tiles above this stair footprint so upper floors are open (stair shaft)
            // 'grids' currently holds all previously-generated upper floors (top-down generation),
            // so iterate them and clear the cells that overlap the stair rect.
            if (grids != null && grids.Count > 0)
            {
                foreach (var upperGrid in grids)
                {
                    if (upperGrid == null) continue;
                    for (int sx = rect.x; sx < rect.x + rect.w; sx++)
                    {
                        for (int sy = rect.y; sy < rect.y + rect.h; sy++)
                        {
                            if (!upperGrid.InBounds(sx, sy)) continue;
                            upperGrid.SetCell(sx, sy, CellType.Empty, RoomType.None);
                        }
                    }
                }
            }

            // record stair edge marks made by HasValidStairEntrance into thisFloorStairEdges
            if (lastMarkedStairEdges != null)
            {
                // add to current floor marks
                foreach (var e in lastMarkedStairEdges)
                    thisFloorStairEdges.Add(e);

                // also propagate corresponding opposite marks to upper floors so walls on floors above are opened
                // (iterate matching upper floors in 'grids' and stairEdgesPerFloor)
                for (int u = 0; u < grids.Count && u < stairEdgesPerFloor.Count; u++)
                {
                    var upperGrid = grids[u];
                    var upperSet = stairEdgesPerFloor[u];
                    if (upperGrid == null || upperSet == null) continue;

                    foreach (var e in lastMarkedStairEdges)
                    {
                        var opp = OppositeEdge(e);
                        // only add if the opposite cell on that upper grid actually has a floor cell
                        if (!upperGrid.InBounds(opp.Item1, opp.Item2)) continue;
                        Cell upCell = upperGrid.GetCell(opp.Item1, opp.Item2);
                        if (upCell == null || upCell.CellType != CellType.Floor) continue;
                        upperSet.Add(opp);
                    }
                }
            }
            lastMarkedStairEdges = null;

            return true;
        }

        return false;
    }

    // a temporary buffer used by TryMarkStairEdge to return marks to caller
    private HashSet<(int, int, CellDirection)> lastMarkedStairEdges = null;

    bool HasValidStairEntrance(RoomRect r, HouseGrid upperGrid)
    {
        // clear temp buffer
        lastMarkedStairEdges = new HashSet<(int, int, CellDirection)>();

        // Vertical stair: width = shortSide, height = longSide
        if (r.w == stairShortSide && r.h == stairLongSide)
        {
            int topY = r.y + r.h - 1;
            int leftX = r.x;
            int rightX = r.x + r.w - 1;

            // Top row (North) -- short edge
            if (TryMarkStairEdge(leftX, topY, rightX, topY, CellDirection.North, upperGrid)) return true;
            // Bottom row (South) -- short edge
            if (TryMarkStairEdge(leftX, r.y, rightX, r.y, CellDirection.South, upperGrid)) return true;
        }
        // Horizontal stair: width = longSide, height = shortSide
        else if (r.w == stairLongSide && r.h == stairShortSide)
        {
            int leftX = r.x;
            int rightX = r.x + r.w - 1;
            int topY = r.y;
            int bottomY = r.y + r.h - 1;

            // Left column (West) -- short edge
            if (TryMarkStairEdge(leftX, topY, leftX, bottomY, CellDirection.West, upperGrid)) return true;
            // Right column (East) -- short edge
            if (TryMarkStairEdge(rightX, topY, rightX, bottomY, CellDirection.East, upperGrid)) return true;
        }

        // nothing matched; clear buffer
        lastMarkedStairEdges = null;
        return false;
    }

    bool TryMarkStairEdge(int x1, int y1, int x2, int y2, CellDirection dir, HouseGrid upperGrid)
    {
        // Ensure we're iterating along a straight axis-aligned edge.
        int minX = Mathf.Min(x1, x2);
        int maxX = Mathf.Max(x1, x2);
        int minY = Mathf.Min(y1, y2);
        int maxY = Mathf.Max(y1, y2);

        bool foundAdjacentTargetCurrent = false;

        // Direction deltas for outward neighbor check
        int dx = 0, dy = 0;
        switch (dir)
        {
            case CellDirection.North: dy = 1; break;
            case CellDirection.South: dy = -1; break;
            case CellDirection.East: dx = 1; break;
            case CellDirection.West: dx = -1; break;
        }

        // Scan all cells along this edge on current floor. If any cell has the outward neighbor be a target room, accept.
        for (int cx = minX; cx <= maxX; cx++)
        {
            for (int cy = minY; cy <= maxY; cy++)
            {
                int nx = cx + dx;
                int ny = cy + dy;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                Cell neighbor = grid.GetCell(nx, ny);
                if (neighbor == null || neighbor.CellType != CellType.Floor) continue;
                if (neighbor.RoomType == RoomType.Stairs) continue;

                foreach (var target in stairAdjacencyTargets)
                {
                    if (neighbor.RoomType == target)
                    {
                        foundAdjacentTargetCurrent = true;
                        break;
                    }
                }

                if (foundAdjacentTargetCurrent) break;
            }
            if (foundAdjacentTargetCurrent) break;
        }

        if (!foundAdjacentTargetCurrent)
            return false;

        // If we have an upper grid, require that the same short edge on the upper floor also has adjacency to a target room.
        if (upperGrid != null)
        {
            bool foundAdjacentTargetUpper = false;

            for (int cx = minX; cx <= maxX; cx++)
            {
                for (int cy = minY; cy <= maxY; cy++)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    Cell neighbor = upperGrid.GetCell(nx, ny);
                    if (neighbor == null || neighbor.CellType != CellType.Floor) continue;
                    if (neighbor.RoomType == RoomType.Stairs) continue;

                    foreach (var target in stairAdjacencyTargets)
                    {
                        if (neighbor.RoomType == target)
                        {
                            foundAdjacentTargetUpper = true;
                            break;
                        }
                    }

                    if (foundAdjacentTargetUpper) break;
                }
                if (foundAdjacentTargetUpper) break;
            }

            if (!foundAdjacentTargetUpper)
                return false;
        }

        // Mark every cell along the stair edge with the outward direction into the temporary buffer
        if (lastMarkedStairEdges == null)
            lastMarkedStairEdges = new HashSet<(int, int, CellDirection)>();

        for (int cx = minX; cx <= maxX; cx++)
        {
            for (int cy = minY; cy <= maxY; cy++)
            {
                lastMarkedStairEdges.Add((cx, cy, dir));
            }
        }

        return true;
    }

    // compute opposite tuple for propagation (x,y,dir) -> neighbor cell and opposite dir
    (int, int, CellDirection) OppositeEdge((int, int, CellDirection) e)
    {
        switch (e.Item3)
        {
            case CellDirection.North: return (e.Item1, e.Item2 + 1, CellDirection.South);
            case CellDirection.South: return (e.Item1, e.Item2 - 1, CellDirection.North);
            case CellDirection.East:  return (e.Item1 + 1, e.Item2, CellDirection.West);
            default:                  return (e.Item1 - 1, e.Item2, CellDirection.East);
        }
    }

    bool EdgeTouchesRoom(int x, int y)
    {
        Vector2Int[] dirs = {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1),
        };

        foreach (var d in dirs)
        {
            int nx = x + d.x;
            int ny = y + d.y;

            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;

            Cell c = grid.GetCell(nx, ny);
            if (c.CellType != CellType.Floor) continue;
            if (c.RoomType == RoomType.Stairs) continue;

            foreach (var target in stairAdjacencyTargets)
            {
                if (c.RoomType == target)
                    return true;
            }
        }

        return false;
    }

    bool TouchesOtherStairs(RoomRect r)
    {
        for (int x = r.x; x < r.x + r.w; x++)
        {
            for (int y = r.y; y < r.y + r.h; y++)
            {
                if (CellAdjacentToStairs(x, y))
                    return true;
            }
        }

        return false;
    }

    bool CellAdjacentToStairs(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                int nx = x + dx;
                int ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                Cell c = grid.GetCell(nx, ny);
                if (c.CellType == CellType.Floor && c.RoomType == RoomType.Stairs)
                    return true;
            }
        }

        return false;
    }

    // ================= FLOOR PLAN =================

    List<PlannedRoom> BuildFloorPlan()
    {
        List<PlannedRoom> plan = new List<PlannedRoom>();

        foreach (var req in roomRequirements)
        {
            int count = Random.Range(req.minCount, req.maxCount + 1);

            for (int i = 0; i < count; i++)
                plan.Add(new PlannedRoom(req.type, req.minSize, req.maxSize));
        }

        for (int i = 0; i < plan.Count; i++)
        {
            int swap = Random.Range(i, plan.Count);
            (plan[i], plan[swap]) = (plan[swap], plan[i]);
        }

        return plan;
    }

    public struct PlannedRoom
    {
        public RoomType type;
        public Vector2Int minSize;
        public Vector2Int maxSize;

        public PlannedRoom(RoomType t, Vector2Int min, Vector2Int max)
        {
            type = t;
            minSize = min;
            maxSize = max;
        }
    }

    // ================= HELPERS =================

    void PlaceRoom(RoomRect r, RoomType type)
    {
        placedRooms.Add(r);
        for (int x = r.x; x < r.x + r.w; x++)
            for (int y = r.y; y < r.y + r.h; y++)
                grid.SetCell(x, y, CellType.Floor, type);
    }

    bool OverlapsAny(RoomRect candidate)
    {
        foreach (var r in placedRooms)
            if (Overlaps(candidate, r))
                return true;
        return false;
    }

    bool Overlaps(RoomRect a, RoomRect b)
    {
        return a.x < b.x + b.w &&
               a.x + a.w > b.x &&
               a.y < b.y + b.h &&
               a.y + a.h > b.y;
    }

    bool IsInsideGrid(RoomRect r)
    {
        return r.x >= 0 &&
               r.y >= 0 &&
               r.x + r.w <= width &&
               r.y + r.h <= height;
    }

    public HouseGrid GetGrid()
    {
        // legacy single-grid getter: return ground floor if present
        if (grids.Count > 0) return grids[0];
        return grid;
    }

    // ================= DEBUG =================

    void OnDrawGizmos()
    {
        // show only the active working grid in the editor when generating
        HouseGrid g = grid ?? (grids.Count > 0 ? grids[0] : null);
        if (g == null) return;

        for (int x = 0; x < g.Width; x++)
            for (int y = 0; y < g.Height; y++)
            {
                Cell cell = g.GetCell(x, y);
                Vector3 pos = transform.position + new Vector3(x * cellSize, 0f, y * cellSize);

                Gizmos.color = GetCellColor(cell);
                Gizmos.DrawCube(pos, new Vector3(cellSize, 0.05f, cellSize));

                Gizmos.color = Color.black;
                Gizmos.DrawWireCube(pos, new Vector3(cellSize, 0.05f, cellSize));
            }
    }

    Color GetCellColor(Cell cell)
    {
        if (cell == null) return Color.magenta;

        switch (cell.CellType)
        {
            case CellType.Empty: return new Color(0, 0, 0, 0.05f);

            case CellType.Floor:
                switch (cell.RoomType)
                {
                    case RoomType.Bedroom: return new Color(0.4f, 0.6f, 1f);
                    case RoomType.Bathroom: return new Color(0.6f, 1f, 1f);
                    case RoomType.Kitchen: return new Color(1f, 0.6f, 0.3f);
                    case RoomType.LivingRoom: return new Color(0.6f, 1f, 0.6f);
                    case RoomType.Stairs: return new Color(0.8f, 0.4f, 1f);
                    default: return Color.gray;
                }

            default: return Color.magenta;
        }
    }
}