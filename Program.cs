using System;
using System.Collections.Generic;
using System.Linq;

// Модель водителя
public class Driver
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public Driver(int id, int x, int y)
    {
        Id = id;
        X = x;
        Y = y;
    }
}

// Водитель с расстоянием
public class DriverDistance
{
    public Driver D { get; }
    public double Dist { get; }
    public DriverDistance(Driver d, double dist) { D = d; Dist = dist; }
}

// Основной менеджер карты и алгоритмов
public class DriverLocator
{
    private readonly int N;
    private readonly int M;

    // Хранилище водителей по id
    private readonly Dictionary<int, Driver> driversById = new Dictionary<int, Driver>();

    // Для GridBuckets - размер бакета и список водителей
    private readonly int bucketSize;
    private readonly Dictionary<(int, int), List<Driver>> buckets = new Dictionary<(int, int), List<Driver>>();

    // Для KD-дерева
    private KdNode kdRoot = null;
    private bool kdDirty = true;

    public DriverLocator(int N, int M, int bucketSize = 10)
    {
        this.N = N;
        this.M = M;
        this.bucketSize = Math.Max(1, bucketSize);
    }

    // Добавить или обновить водителя
    public void AddOrUpdateDriver(int id, int x, int y)
    {
        if (x < 0 || x >= N || y < 0 || y >= M) throw new ArgumentOutOfRangeException("Координаты вне карты");

        // Если есть старый, удалить из бакетов
        if (driversById.TryGetValue(id, out var old))
        {
            RemoveFromBucket(old);
            old.X = x; old.Y = y;
            AddToBucket(old);
            kdDirty = true;
        }
        else
        {
            var d = new Driver(id, x, y);
            driversById[id] = d;
            AddToBucket(d);
            kdDirty = true;
        }
    }

    public bool RemoveDriver(int id)
    {
        if (!driversById.TryGetValue(id, out var d)) return false;
        RemoveFromBucket(d);
        driversById.Remove(id);
        kdDirty = true;
        return true;
    }

    private void AddToBucket(Driver d)
    {
        var key = (d.X / bucketSize, d.Y / bucketSize);
        if (!buckets.TryGetValue(key, out var list))
        {
            list = new List<Driver>();
            buckets[key] = list;
        }
        list.Add(d);
    }

    private void RemoveFromBucket(Driver d)
    {
        var key = (d.X / bucketSize, d.Y / bucketSize);
        if (buckets.TryGetValue(key, out var list))
        {
            list.RemoveAll(item => item.Id == d.Id);
            if (list.Count == 0) buckets.Remove(key);
        }
    }

    // Манхэттенское расстояние
    private static double Distance(Driver d, int x, int y) => Math.Abs(d.X - x) + Math.Abs(d.Y - y);
    private static double Distance(int x1, int y1, int x2, int y2) => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

    // 1) Brute force
    public List<DriverDistance> FindKNearestBruteForce(int x, int y, int k = 5)
    {
        var list = driversById.Values
            .Select(d => new DriverDistance(d, Distance(d, x, y)))
            .OrderBy(dd => dd.Dist)
            .ThenBy(dd => dd.D.Id)
            .Take(k)
            .ToList();
        return list;
    }

    // 1b) Brute force и min-heap
    public List<DriverDistance> FindKNearestHeap(int x, int y, int k = 5)
    {
        if (k <= 0) return new List<DriverDistance>();
        // max-heap размером k: оставляем k кратчайших дистанций
        var pq = new SimpleMaxHeap<DriverDistance>((a, b) =>
        {
            var cmp = a.Dist.CompareTo(b.Dist);
            if (cmp != 0) return cmp;
            return a.D.Id.CompareTo(b.D.Id);
        });

        foreach (var d in driversById.Values)
        {
            var dd = new DriverDistance(d, Distance(d, x, y));
            if (pq.Count < k) pq.Push(dd);
            else
            {
                // если настоящее расстояние короче, чем максимальное в куче, заменяем
                if (CompareDriverDistance(dd, pq.Peek()) < 0)
                {
                    pq.Pop();
                    pq.Push(dd);
                }
            }
        }

        var res = new List<DriverDistance>();
        while (pq.Count > 0) res.Add(pq.Pop());
        // так как сейчас от худших к лучшим, меняем наоборот
        res.Reverse();
        return res;
    }

    private static int CompareDriverDistance(DriverDistance a, DriverDistance b)
    {
        var cmp = a.Dist.CompareTo(b.Dist);
        if (cmp != 0) return cmp;
        return a.D.Id.CompareTo(b.D.Id);
    }

    // 2) GridBuckets: расширяем кольца бакетов пока не набрали k кандидатов, затем отфильтровываем
    public List<DriverDistance> FindKNearestGridBuckets(int x, int y, int k = 5)
    {
        var bx = x / bucketSize;
        var by = y / bucketSize;

        var candidates = new List<DriverDistance>();
        var visitedBuckets = new HashSet<(int, int)>();

        int radius = 0;
        // предел — пока не прошли все бакеты (в худшем случае)
        int maxBx = (N - 1) / bucketSize;
        int maxBy = (M - 1) / bucketSize;

        while (true)
        {
            // итерируем по сегментам (бакетам) на расстоянии Чебышева, равном радиусу от (bx,by)
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                    int nbx = bx + dx, nby = by + dy;
                    if (nbx < 0 || nby < 0 || nbx > maxBx || nby > maxBy) continue;
                    var key = (nbx, nby);
                    if (visitedBuckets.Contains(key)) continue;
                    visitedBuckets.Add(key);
                    if (buckets.TryGetValue(key, out var list))
                    {
                        foreach (var d in list) candidates.Add(new DriverDistance(d, Distance(d, x, y)));
                    }
                }
            }

            if (candidates.Count >= k) break;
            radius++;
            if (radius > Math.Max(maxBx, maxBy)) break; // все бакеты просмотрены
        }

        return candidates.OrderBy(cd => cd.Dist).ThenBy(cd => cd.D.Id).Take(k).ToList();
    }

    // 3) KD-дерево
    public List<DriverDistance> FindKNearestKdTree(int x, int y, int k = 5)
    {
        if (kdDirty)
        {
            BuildKdTree();
            kdDirty = false;
        }
        if (kdRoot == null) return new List<DriverDistance>();
        var pq = new SimpleMaxHeap<DriverDistance>((a, b) =>
        {
            var cmp = a.Dist.CompareTo(b.Dist);
            if (cmp != 0) return cmp;
            return a.D.Id.CompareTo(b.D.Id);
        });

        KdSearch(kdRoot, x, y, k, pq, 0);

        var result = new List<DriverDistance>();
        while (pq.Count > 0) result.Add(pq.Pop());
        result.Reverse();
        return result;
    }

    private void BuildKdTree()
    {
        var points = driversById.Values.ToList();
        kdRoot = BuildKd(points, 0);
    }

    private KdNode BuildKd(List<Driver> pts, int depth)
    {
        if (pts == null || pts.Count == 0) return null;
        int axis = depth % 2; // 0 -> X, 1 -> Y
        pts.Sort((a, b) => axis == 0 ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        int mid = pts.Count / 2;
        var node = new KdNode
        {
            Point = pts[mid],
            Left = BuildKd(pts.GetRange(0, mid), depth + 1),
            Right = BuildKd(pts.GetRange(mid + 1, pts.Count - (mid + 1)), depth + 1)
        };
        return node;
    }

    private void KdSearch(KdNode node, int x, int y, int k, SimpleMaxHeap<DriverDistance> pq, int depth)
    {
        if (node == null) return;
        var d = node.Point;
        var dist = Distance(d, x, y);
        var dd = new DriverDistance(d, dist);

        if (pq.Count < k) pq.Push(dd);
        else if (CompareDriverDistance(dd, pq.Peek()) < 0)
        {
            pq.Pop();
            pq.Push(dd);
        }

        int axis = depth % 2;
        int diff = (axis == 0) ? x - d.X : y - d.Y;

        var first = diff <= 0 ? node.Left : node.Right;
        var second = diff <= 0 ? node.Right : node.Left;

        KdSearch(first, x, y, k, pq, depth + 1);

        // Проверяем, стоит ли обходить другую ветвь:
        // нужно, если |diff| <= текущая худшая дистанция в pq (или pq не заполнена)
        double worstDist = pq.Count < k ? double.PositiveInfinity : pq.Peek().Dist;
        if (Math.Abs(diff) <= worstDist)
        {
            KdSearch(second, x, y, k, pq, depth + 1);
        }
    }

    // Вспомогательные структуры для KD и кучи
    private class KdNode
    {
        public Driver Point;
        public KdNode Left;
        public KdNode Right;
    }


    // Простая max-heap (по заданному компаратору)
    private class SimpleMaxHeap<T>
    {
        private readonly List<T> data = new List<T>();
        private readonly Comparison<T> cmp;
        public int Count => data.Count;
        public SimpleMaxHeap(Comparison<T> cmp) { this.cmp = cmp; }

        public void Push(T item)
        {
            data.Add(item);
            int i = data.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (cmp(data[i], data[p]) <= 0) break;
                Swap(i, p);
                i = p;
            }
        }

        public T Pop()
        {
            if (data.Count == 0) throw new InvalidOperationException();
            var ret = data[0];
            var last = data[data.Count - 1];
            data.RemoveAt(data.Count - 1);
            if (data.Count > 0)
            {
                data[0] = last;
                Heapify(0);
            }
            return ret;
        }

        public T Peek()
        {
            if (data.Count == 0) throw new InvalidOperationException();
            return data[0];
        }

        private void Heapify(int i)
        {
            int n = data.Count;
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, largest = i;
                if (l < n && cmp(data[l], data[largest]) > 0) largest = l;
                if (r < n && cmp(data[r], data[largest]) > 0) largest = r;
                if (largest == i) break;
                Swap(i, largest);
                i = largest;
            }
        }

        private void Swap(int a, int b)
        {
            var t = data[a]; data[a] = data[b]; data[b] = t;
        }
    }
}

class Program
{
    static void Main()
    {
        var locator = new DriverLocator(N: 100, M: 100, bucketSize: 10);

        // Добавим 200 случайных водителей
        var rand = new Random(0);
        for (int i = 1; i <= 200; i++)
        {
            int x = rand.Next(0, 100);
            int y = rand.Next(0, 100);
            locator.AddOrUpdateDriver(i, x, y);
        }

        int orderX = 50, orderY = 50;
        var topBrute = locator.FindKNearestBruteForce(orderX, orderY, 5);
        var topHeap = locator.FindKNearestHeap(orderX, orderY, 5);
        var topGrid = locator.FindKNearestGridBuckets(orderX, orderY, 5);
        var topKd = locator.FindKNearestKdTree(orderX, orderY, 5);

        Console.WriteLine("BruteForce:");
        foreach (var dd in topBrute) Console.WriteLine($"Id {dd.D.Id} @({dd.D.X},{dd.D.Y}) dist={dd.Dist}");
        Console.WriteLine("Heap:");
        foreach (var dd in topHeap) Console.WriteLine($"Id {dd.D.Id} @({dd.D.X},{dd.D.Y}) dist={dd.Dist}");
        Console.WriteLine("GridBuckets:");
        foreach (var dd in topGrid) Console.WriteLine($"Id {dd.D.Id} @({dd.D.X},{dd.D.Y}) dist={dd.Dist}");
        Console.WriteLine("KdTree:");
        foreach (var dd in topKd) Console.WriteLine($"Id {dd.D.Id} @({dd.D.X},{dd.D.Y}) dist={dd.Dist}");
    }
}
