using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class HouseMesh : MonoBehaviour
{
    [Header("References")]
    public HouseGenerator generator;

    [Header("Prefabs")]
    public GameObject bedroomFloorPrefab;
    public GameObject bathroomFloorPrefab;
    public GameObject kitchenFloorPrefab;
    public GameObject livingFloorPrefab;
    public GameObject stairFloorPrefab;

    [Header("Layout")]
    public Transform parentRoot;
    public BoxCollider col;
    public float height = 0.2f;
    private float cellSize = 1f;

    [Header("Wall Settings")]
    public GameObject wallPrefab;
    public float wallHeightOffset = 0f;

    // ================= RUNTIME DATA =================
    HashSet<(int, int, CellDirection)> openEdges = new();
    HashSet<(int, int, CellDirection)> stairEntranceEdges = new();

    // ================= BUILD =================

    public void BuildFloors()
    {
        cellSize = generator.cellSize;

        int floorCount = generator.GetFloorCount();
        if (floorCount <= 0) floorCount = 1;

        // clear existing meshes
        ClearOld();

        // Build each floor at increasing vertical offsets (0 = ground)
        for (int f = 0; f < floorCount; f++)
        {
            HouseGrid grid = generator.GetGrid(f);
            if (grid == null) continue;

            // per-floor stair edges
            stairEntranceEdges = generator.GetStairEntranceEdges(f);

            openEdges.Clear();

            // Add stair entrance edges to openEdges so they behave like doorways (both directions)
            if (stairEntranceEdges != null)
            {
                foreach (var se in stairEntranceEdges)
                {
                    // reuse MarkEdgeOpen to add both sides
                    MarkEdgeOpen(new Edge(se.Item1, se.Item2, se.Item3));
                }
            }

            float yOffset = f * generator.GetFloorOffset();

            // ----- FLOORS -----
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    Cell cell = grid.GetCell(x, y);
                    if (cell == null || cell.CellType != CellType.Floor)
                        continue;

                    GameObject prefab = GetPrefab(cell.RoomType);
                    if (prefab == null) continue;

                    Vector3 pos = transform.position + new Vector3(x * cellSize, yOffset, y * cellSize);

                    // Determine rotation for stairs so lower/upper parts align with doorways.
                    Quaternion rot = Quaternion.identity;
                    if (cell.RoomType == RoomType.Stairs && stairEntranceEdges != null)
                    {
                        // Try to find a direct mark on this cell first.
                        CellDirection? foundDir = null;
                        foreach (CellDirection d in new[] { CellDirection.North, CellDirection.East, CellDirection.South, CellDirection.West })
                        {
                            if (stairEntranceEdges.Contains((x, y, d)))
                            {
                                foundDir = d;
                                break;
                            }
                        }

                        // If not found, check neighbor edge marks and use their direction (fallback).
                        if (foundDir == null)
                        {
                            // neighbor at north (this cell could be the opposite side)
                            if (stairEntranceEdges.Contains((x, y + 1, CellDirection.South))) foundDir = CellDirection.South;
                            else if (stairEntranceEdges.Contains((x, y - 1, CellDirection.North))) foundDir = CellDirection.North;
                            else if (stairEntranceEdges.Contains((x + 1, y, CellDirection.West))) foundDir = CellDirection.West;
                            else if (stairEntranceEdges.Contains((x - 1, y, CellDirection.East))) foundDir = CellDirection.East;
                        }

                        if (foundDir.HasValue)
                            rot = RotationForDirection(foundDir.Value);
                    }

                    GameObject go = Instantiate(prefab, pos, rot, parentRoot);
                    //go.transform.localScale = new Vector3(cellSize + 0.01f, 0.19881f, cellSize + 0.01f);
                }
            }

            // FitCollider only once (keeps original behavior); you can extend to include full height if desired.
            if (f == 0) FitCollider();

            BuildRoomConnections(grid);
            BuildWalls(grid, yOffset, f);
        }
    }

    // ================= PREFABS =================

    GameObject GetPrefab(RoomType type)
    {
        switch (type)
        {
            case RoomType.Bedroom: return bedroomFloorPrefab;
            case RoomType.Bathroom: return bathroomFloorPrefab;
            case RoomType.Kitchen: return kitchenFloorPrefab;
            case RoomType.LivingRoom: return livingFloorPrefab;
            case RoomType.Stairs: return stairFloorPrefab;
            default: return null;
        }
    }

    // ================= CLEANUP =================

    void ClearOld()
    {
        if (parentRoot == null) return;

        for (int i = parentRoot.childCount - 1; i >= 0; i--)
        {
            Transform t = parentRoot.GetChild(i);
            if (!t.CompareTag("Collider"))
                DestroyImmediate(t.gameObject);
        }
    }

    // ================= COLLIDER =================

    public void FitCollider()
    {
        var grid = generator.GetGrid(0);
        if (grid == null) return;

        float width = grid.Width * cellSize;
        float depth = grid.Height * cellSize;

        col.size = new Vector3(width, height, depth);
        col.center = new Vector3(
            width / 2f - cellSize / 2f,
            -height / 2f,
            depth / 2f - cellSize / 2f
        );
    }

    // ================= WALLS =================

    void BuildWalls(HouseGrid grid, float floorYOffset, int floorIndex)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                Cell cell = grid.GetCell(x, y);
                if (cell == null || cell.CellType != CellType.Floor)
                    continue;

                Vector3 basePos = transform.position + new Vector3(x * cellSize, wallHeightOffset + floorYOffset, y * cellSize);

                if (NeedsWall(grid, x, y, x, y + 1, floorIndex))
                    SpawnWall(basePos + new Vector3(0, 0, cellSize / 2f), Quaternion.identity);

                if (NeedsWall(grid, x, y, x, y - 1, floorIndex))
                    SpawnWall(basePos + new Vector3(0, 0, -cellSize / 2f), Quaternion.identity);

                if (NeedsWall(grid, x, y, x + 1, y, floorIndex))
                    SpawnWall(basePos + new Vector3(cellSize / 2f, 0, 0), Quaternion.Euler(0, 90, 0));

                if (NeedsWall(grid, x, y, x - 1, y, floorIndex))
                    SpawnWall(basePos + new Vector3(-cellSize / 2f, 0, 0), Quaternion.Euler(0, 90, 0));
            }
        }
    }

    void SpawnWall(Vector3 pos, Quaternion rot)
    {
        Instantiate(wallPrefab, pos, rot, parentRoot);
    }

    bool NeedsWall(HouseGrid grid, int x1, int y1, int x2, int y2, int floorIndex)
    {
        Cell a = grid.GetCell(x1, y1);
        Cell b = grid.GetCell(x2, y2);

        if (a == null || a.CellType != CellType.Floor)
            return false;

        // Planned doorway
        if (IsEdgeOpen(x1, y1, x2, y2))
            return false;

        // --- NEW RULE: if this tile sits above a stair on the lower floor, avoid surrounding it with walls.
        // Keep walls only where the neighbor is a floor of a different room (we still want separating walls between rooms).
        if (floorIndex - 1 >= 0)
        {
            var lowerGrid = generator.GetGrid(floorIndex - 1);
            if (lowerGrid != null && lowerGrid.InBounds(x1, y1))
            {
                var lowerCell = lowerGrid.GetCell(x1, y1);
                if (lowerCell != null && lowerCell.CellType == CellType.Floor && lowerCell.RoomType == RoomType.Stairs)
                {
                    // If neighbor is an existing floor and a different room, keep wall.
                    if (b != null && b.CellType == CellType.Floor && a.RoomType != b.RoomType)
                        return true;

                    // If there's a planned doorway or stair entrance on this edge, keep open.
                    if (IsStairEntranceEdge(x1, y1, x2, y2))
                        return false;

                    // Otherwise -- do not place a wall around a tile above a stair shaft.
                    return false;
                }
            }
        }

        // Don't place walls between two stair tiles (internal stair tiles)
        if (a.RoomType == RoomType.Stairs && b != null && b.RoomType == RoomType.Stairs)
            return false;

        // STAIRS SPECIAL RULE (tiles that are stairs or adjacent to stairs)
        if (a.RoomType == RoomType.Stairs || (b != null && b.RoomType == RoomType.Stairs))
        {
            if (IsStairEntranceEdge(x1, y1, x2, y2))
                return false;

            return true;
        }

        // Map edge
        if (b == null || b.CellType == CellType.Empty)
        {
            // If current floor has a stair mark on this cell toward the empty neighbor, treat as open
            if (IsStairEntranceEdge(x1, y1, x2, y2))
                return false;

            // Check lower floor marks (the stair that created the shaft was placed on floorIndex - 1)
            if (floorIndex - 1 >= 0)
            {
                var lowerSet = generator.GetStairEntranceEdges(floorIndex - 1);
                if (lowerSet != null)
                {
                    CellDirection dir = GetDir(x1, y1, x2, y2);
                    CellDirection opp = OppositeDir(dir);
                    // If the lower floor has a stair entrance on the shaft cell pointing to this cell, open the wall.
                    if (lowerSet.Contains((x2, y2, opp)))
                        return false;
                }
            }

            return true;
        }

        // Different rooms
        if (a.RoomType != b.RoomType)
            return true;

        return false;
    }

    bool IsEdgeOpen(int x1, int y1, int x2, int y2)
    {
        CellDirection dir = GetDir(x1, y1, x2, y2);
        return openEdges.Contains((x1, y1, dir));
    }

    bool IsStairEntranceEdge(int x1, int y1, int x2, int y2)
    {
        CellDirection dir = GetDir(x1, y1, x2, y2);
        return stairEntranceEdges.Contains((x1, y1, dir));
    }

    CellDirection GetDir(int x1, int y1, int x2, int y2)
    {
        if (x2 == x1 && y2 == y1 + 1) return CellDirection.North;
        if (x2 == x1 && y2 == y1 - 1) return CellDirection.South;
        if (x2 == x1 + 1 && y2 == y1) return CellDirection.East;
        return CellDirection.West;
    }

    CellDirection OppositeDir(CellDirection dir)
    {
        return dir switch
        {
            CellDirection.North => CellDirection.South,
            CellDirection.South => CellDirection.North,
            CellDirection.East  => CellDirection.West,
            CellDirection.West  => CellDirection.East,
            _ => dir
        };
    }

    // ================= DOORWAYS =================

    struct Edge
    {
        public int x, y;
        public CellDirection dir;
        public Edge(int x, int y, CellDirection d) { this.x = x; this.y = y; dir = d; }
    }

    void BuildRoomConnections(HouseGrid grid)
    {
        int w = grid.Width;
        int h = grid.Height;

        // region id per cell (-1 = unassigned)
        int[,] regionId = new int[w, h];
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                regionId[i, j] = -1;

        var regions = new List<(RoomType type, List<(int x, int y)> cells)>();

        // Flood fill to find connected room regions (connected by same RoomType)
        int nextRegion = 0;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (!grid.InBounds(x, y)) continue;
                Cell c = grid.GetCell(x, y);
                if (c == null || c.CellType != CellType.Floor) continue;
                if (regionId[x, y] != -1) continue;

                // new region
                RoomType type = c.RoomType;
                var list = new List<(int, int)>();
                var q = new Queue<(int, int)>();
                q.Enqueue((x, y));
                regionId[x, y] = nextRegion;

                while (q.Count > 0)
                {
                    var (cx, cy) = q.Dequeue();
                    list.Add((cx, cy));

                    // 4 neighbors
                    var nbrs = new (int nx, int ny)[] {
                        (cx, cy+1),
                        (cx, cy-1),
                        (cx+1, cy),
                        (cx-1, cy)
                    };

                    foreach (var nb in nbrs)
                    {
                        int nx = nb.nx, ny = nb.ny;
                        if (!grid.InBounds(nx, ny)) continue;
                        if (regionId[nx, ny] != -1) continue;
                        Cell nc = grid.GetCell(nx, ny);
                        if (nc == null || nc.CellType != CellType.Floor) continue;
                        if (nc.RoomType != type) continue;
                        regionId[nx, ny] = nextRegion;
                        q.Enqueue((nx, ny));
                    }
                }

                regions.Add((type, list));
                nextRegion++;
            }
        }

        int regionCount = regions.Count;
        if (regionCount <= 1) return; // already connected or nothing to do

        // Collect candidate adjacency edges between regions (only check North and East to avoid duplicates)
        var candidateEdges = new List<RegionEdge>();
        for (int rid = 0; rid < regions.Count; rid++)
        {
            foreach (var cell in regions[rid].cells)
            {
                int cx = cell.x, cy = cell.y;

                // North
                int nx = cx, ny = cy + 1;
                if (grid.InBounds(nx, ny))
                {
                    Cell nb = grid.GetCell(nx, ny);
                    if (nb != null && nb.CellType == CellType.Floor)
                    {
                        int other = regionId[nx, ny];
                        if (other != rid)
                        {
                            var dir = CellDirection.North;
                            bool stairValid = IsStairEdgeValid(cx, cy, nx, ny, dir);
                            candidateEdges.Add(new RegionEdge(rid, other, cx, cy, dir, stairValid));
                        }
                    }
                }

                // East
                nx = cx + 1; ny = cy;
                if (grid.InBounds(nx, ny))
                {
                    Cell nb = grid.GetCell(nx, ny);
                    if (nb != null && nb.CellType == CellType.Floor)
                    {
                        int other = regionId[nx, ny];
                        if (other != rid)
                        {
                            var dir = CellDirection.East;
                            bool stairValid = IsStairEdgeValid(cx, cy, nx, ny, dir);
                            candidateEdges.Add(new RegionEdge(rid, other, cx, cy, dir, stairValid));
                        }
                    }
                }
            }
        }

        if (candidateEdges.Count == 0)
        {
            // No adjacencies at all — isolated, nothing to do
            return;
        }

        // Shuffle candidate edges
        for (int i = 0; i < candidateEdges.Count; i++)
        {
            int j = Random.Range(i, candidateEdges.Count);
            var tmp = candidateEdges[i];
            candidateEdges[i] = candidateEdges[j];
            candidateEdges[j] = tmp;
        }

        // Union-find to build a spanning tree across regions
        int[] parent = new int[regionCount];
        for (int i = 0; i < regionCount; i++) parent[i] = i;

        System.Func<int, int> find = null;
        find = (int a) =>
        {
            while (parent[a] != a)
            {
                parent[a] = parent[parent[a]];
                a = parent[a];
            }
            return a;
        };

        System.Action<int, int> unite = (int a, int b) =>
        {
            int ra = find(a), rb = find(b);
            if (ra == rb) return;
            parent[rb] = ra;
        };

        // Use edges to connect components; prefer non-stair edges unless stairValid
        foreach (var ce in candidateEdges)
        {
            int ra = find(ce.a), rb = find(ce.b);
            if (ra == rb) continue;

            bool involvesStairs = regions[ce.a].type == RoomType.Stairs || regions[ce.b].type == RoomType.Stairs;
            if (involvesStairs && !ce.stairValid)
                continue; // skip invalid stair edges for now

            // open this edge
            MarkEdgeOpen(new Edge(ce.x, ce.y, ce.dir));
            unite(ra, rb);
        }

        // If still disconnected, force-connect remaining components (fallback)
        // This ensures every room is accessible even if stair edges were not marked by generator.
        var components = new Dictionary<int, List<int>>();
        for (int i = 0; i < regionCount; i++)
        {
            int r = find(i);
            if (!components.ContainsKey(r)) components[r] = new List<int>();
            components[r].Add(i);
        }

        if (components.Count > 1)
        {
            // Find any candidate edges that connect different components (including stair edges)
            foreach (var ce in candidateEdges)
            {
                int ra = find(ce.a), rb = find(ce.b);
                if (ra == rb) continue;
                // force open
                MarkEdgeOpen(new Edge(ce.x, ce.y, ce.dir));
                unite(ra, rb);
            }
        }
    }

    // Helper to check whether an adjacent edge is marked as a valid stair entrance (either orientation)
    bool IsStairEdgeValid(int x1, int y1, int x2, int y2, CellDirection dir)
    {
        // if neither side is stairs, it's not a stair edge — consider valid
        // otherwise check if generator provided a stair entrance for either orientation
        if (!stairEntranceEdges.Any()) return true;

        if (stairEntranceEdges.Contains((x1, y1, dir)))
            return true;

        // check opposite orientation from the neighbor's perspective
        var opp = Opposite(new Edge(x1, y1, dir));
        if (stairEntranceEdges.Contains(opp))
            return true;

        return false;
    }

    // RegionEdge container
    class RegionEdge
    {
        public int a, b;
        public int x, y;
        public CellDirection dir;
        public bool stairValid;
        public RegionEdge(int a, int b, int x, int y, CellDirection dir, bool stairValid)
        {
            this.a = a; this.b = b; this.x = x; this.y = y; this.dir = dir; this.stairValid = stairValid;
        }
    }

    void TryAddEdge(HouseGrid grid, int x1, int y1, int x2, int y2, CellDirection dir,
        Dictionary<(RoomType, RoomType), List<Edge>> groups)
    {
        Cell a = grid.GetCell(x1, y1);
        Cell b = grid.GetCell(x2, y2);

        if (b == null || b.CellType != CellType.Floor) return;
        if (a.RoomType == b.RoomType) return;

        // ignore stairs for horizontal connections
        if (a.RoomType == RoomType.Stairs || b.RoomType == RoomType.Stairs)
            return;

        var key = a.RoomType < b.RoomType ? (a.RoomType, b.RoomType) : (b.RoomType, a.RoomType);

        if (!groups.ContainsKey(key))
            groups[key] = new List<Edge>();

        groups[key].Add(new Edge(x1, y1, dir));
    }

    void MarkEdgeOpen(Edge e)
    {
        openEdges.Add((e.x, e.y, e.dir));
        var o = Opposite(e);
        openEdges.Add(o);
    }

    (int, int, CellDirection) Opposite(Edge e)
    {
        return e.dir switch
        {
            CellDirection.North => (e.x, e.y + 1, CellDirection.South),
            CellDirection.South => (e.x, e.y - 1, CellDirection.North),
            CellDirection.East => (e.x + 1, e.y, CellDirection.West),
            _ => (e.x - 1, e.y, CellDirection.East)
        };
    }

    // Map CellDirection to a Y rotation (assumes stair prefab forward is +Z / North)
    Quaternion RotationForDirection(CellDirection dir)
    {
        return dir switch
        {
            CellDirection.North => Quaternion.Euler(0f, 0f, 0f),
            CellDirection.East => Quaternion.Euler(0f, 90f, 0f),
            CellDirection.South => Quaternion.Euler(0f, 180f, 0f),
            CellDirection.West => Quaternion.Euler(0f, 270f, 0f),
            _ => Quaternion.identity
        };
    }
}