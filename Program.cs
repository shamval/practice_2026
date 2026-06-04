using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

public record Driver(int Id, int X, int Y);

class DriverStore
{
    readonly int N, M;
    readonly Dictionary<int, Driver> drivers = new();
    readonly int[,] grid;

    public DriverStore(int n, int m)
    {
        N = n; M = m;
        grid = new int[N, M];
        for (int i = 0; i < N; i++)
            for (int j = 0; j < M; j++)
                grid[i, j] = -1;
    }

    public bool AddOrUpdate(int id, int x, int y)
    {
        if (x < 0 || x >= N || y < 0 || y >= M) return false;
        if (drivers.TryGetValue(id, out var old)) grid[old.X, old.Y] = -1;
        var occ = grid[x, y];
        if (occ != -1 && occ != id) return false;
        grid[x, y] = id;
        drivers[id] = new Driver(id, x, y);
        return true;
    }

    public bool Remove(int id)
    {
        if (!drivers.TryGetValue(id, out var d)) return false;
        grid[d.X, d.Y] = -1;
        drivers.Remove(id);
        return true;
    }

    public IReadOnlyCollection<Driver> AllDrivers() => drivers.Values;
    public int[,] Grid => grid;
    public int Count => drivers.Count;
}

static class Distance
{
    public static int Sq(int x1, int y1, int x2, int y2)
    {
        int dx = x1 - x2, dy = y1 - y2;
        return dx * dx + dy * dy;
    }
}

static class NearestSearch
{
    // 1) Brute-force
    public static List<Driver> BruteForceTopK(DriverStore store, int tx, int ty, int k = 5)
    {
        return store.AllDrivers()
            .Select(d => (driver: d, dist: Distance.Sq(d.X, d.Y, tx, ty)))
            .OrderBy(t => t.dist)
            .ThenBy(t => t.driver.Id)
            .Take(k)
            .Select(t => t.driver)
            .ToList();
    }

    // 2) Частичный выбор (Partial select) с помощью отсортированного множества макс. размера (max-size Sorted Set)
    // (сохранение наименьшего k). Используем именованные кортежи для надёжного доступа.
    public static List<Driver> PartialSelectTopK(DriverStore store, int tx, int ty, int k = 5)
    {
        var comparer = Comparer<(int dist, int id, Driver d)>.Create((a, b) =>
        {
            int c = a.dist.CompareTo(b.dist);
            if (c != 0) return c;
            c = a.id.CompareTo(b.id);
            if (c != 0) return c;
            return a.d.Id.CompareTo(b.d.Id);
        });

        var set = new SortedSet<(int dist, int id, Driver d)>(comparer);

        foreach (var drv in store.AllDrivers())
        {
            int dist = Distance.Sq(drv.X, drv.Y, tx, ty);
            var entry = (dist: dist, id: drv.Id, d: drv);
            set.Add(entry);
            if (set.Count > k)
            {
                // удалить максимальный элемент, т.е. последний
                var max = set.Max;
                set.Remove(max);
            }
        }

        // Преобразуем к списку, сортируя по dist и id
        return set
            .OrderBy(t => t.dist)
            .ThenBy(t => t.id)
            .Take(k)
            .Select(t => t.d)
            .ToList();
    }

    // 3) Grid expansion (BFS по манхэттену). Для поиска id в клетке используем прямой доступ к grid и словарь drivers.
    public static List<Driver> GridExpansionTopK(DriverStore store, int tx, int ty, int k = 5)
    {
        var grid = store.Grid;
        int N = grid.GetLength(0), M = grid.GetLength(1);
        if (tx < 0 || tx >= N || ty < 0 || ty >= M) return new List<Driver>();

        var visited = new bool[N, M];
        var q = new Queue<(int x, int y)>();
        var found = new List<(Driver d, int dist)>();

        q.Enqueue((tx, ty));
        visited[tx, ty] = true;

        var dirs = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        while (q.Count > 0 && found.Count < k)
        {
            var (x, y) = q.Dequeue();
            int id = grid[x, y];
            if (id != -1)
            {
                // получить водителя по id из коллекции
                var drv = store.AllDrivers().FirstOrDefault(d => d.Id == id);
                if (drv != null)
                    found.Add((drv, Distance.Sq(drv.X, drv.Y, tx, ty)));
            }

            foreach (var (dx, dy) in dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < N && ny >= 0 && ny < M && !visited[nx, ny])
                {
                    visited[nx, ny] = true;
                    q.Enqueue((nx, ny));
                }
            }
        }

        if (found.Count < k)
            return BruteForceTopK(store, tx, ty, k);

        return found
            .OrderBy(t => t.dist)
            .ThenBy(t => t.d.Id)
            .Take(k)
            .Select(t => t.d)
            .ToList();
    }
}

public class SearchBenchmark
{
    DriverStore store;
    (int x, int y) query;
    Random rnd = new(123);
    const int N = 1000;
    const int M = 1000;
    const int DriversCount = 20000;
    const int K = 5;

    [GlobalSetup]
    public void Setup()
    {
        store = new DriverStore(N, M);
        var used = new HashSet<(int, int)>();
        int id = 0;
        while (store.Count < DriversCount)
        {
            int x = rnd.Next(0, N);
            int y = rnd.Next(0, M);
            if (used.Add((x, y)))
                store.AddOrUpdate(id++, x, y);
        }
        query = (rnd.Next(0, N), rnd.Next(0, M));
    }

    [Benchmark(Baseline = true)]
    public List<Driver> BruteForce() => NearestSearch.BruteForceTopK(store, query.x, query.y, K);

    [Benchmark]
    public List<Driver> PartialSelect() => NearestSearch.PartialSelectTopK(store, query.x, query.y, K);

    [Benchmark]
    public List<Driver> GridExpansion() => NearestSearch.GridExpansionTopK(store, query.x, query.y, K);
}

class Program
{
    static void Main()
    {
        var store = new DriverStore(10, 10);
        store.AddOrUpdate(1, 2, 2);
        store.AddOrUpdate(2, 7, 2);
        store.AddOrUpdate(3, 3, 3);
        store.AddOrUpdate(4, 0, 0);
        store.AddOrUpdate(5, 9, 9);
        store.AddOrUpdate(6, 2, 3);

        int qx = 2, qy = 2;
        Console.WriteLine("BruteForce:");
        foreach (var d in NearestSearch.BruteForceTopK(store, qx, qy)) Console.WriteLine(d);
        Console.WriteLine("PartialSelect:");
        foreach (var d in NearestSearch.PartialSelectTopK(store, qx, qy)) Console.WriteLine(d);
        Console.WriteLine("GridExpansion:");
        foreach (var d in NearestSearch.GridExpansionTopK(store, qx, qy)) Console.WriteLine(d);

        Console.WriteLine("Running benchmarks...");
        var summary = BenchmarkRunner.Run<SearchBenchmark>();
    }
}

