using UnityEngine;

public enum CellType
{
    Empty,
    Floor
}

public enum RoomType
{
    None,
    Bedroom,
    Bathroom,
    Kitchen,
    LivingRoom,
    Hallway,
    Stairs
}

public class Cell
{
    public CellType CellType;
    public RoomType RoomType;

    public Cell()
    {
        CellType = CellType.Empty;
        RoomType = RoomType.None;
    }
}
public struct RoomRect
{
    public int x, y, w, h;

    public RoomRect(int x, int y, int w, int h)
    {
        this.x = x;
        this.y = y;
        this.w = w;
        this.h = h;
    }
}

[System.Serializable]
public struct RoomRequirement
{
    public RoomType type;
    public int minCount;
    public int maxCount;

    public Vector2Int minSize;
    public Vector2Int maxSize;
}
public enum CellDirection { North, South, East, West }

public class HouseGrid
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    private Cell[,] grid;

    public HouseGrid(int width, int height)
    {
        Width = width;
        Height = height;
        grid = new Cell[width, height];

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = new Cell();
    }

    public RoomType GetRoomType(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return RoomType.None;

        return grid[x, y].RoomType;
    }

    public Cell GetCell(int x, int y)
    {
        if (!InBounds(x, y)) return null;
        return grid[x, y];
    }

    public void SetCell(int x, int y, CellType cellType, RoomType roomType)
    {
        if (!InBounds(x, y)) return;

        grid[x, y].CellType = cellType;
        grid[x, y].RoomType = roomType;
    }

    public bool InBounds(int x, int y)
    {
        return x >= 0 && y >= 0 && x < Width && y < Height;
    }
}
