using System.Numerics;

namespace ZombieCafe.Game.Cafe;

/// <summary>
/// Walkability graph over the map cells (original 0x1c-byte graph at Cafe+0x68 with its A* engine):
/// up to <see cref="MaxNeighbours"/> directed edges per node with float costs, a passable bit per node,
/// node positions for the Euclidean heuristic.
/// </summary>
public sealed class NavGraph
{
    public const int MaxNeighbours = 4;
    readonly int[] neighbours;
    readonly float[] costs;
    readonly byte[] counts;
    readonly bool[] passable;
    readonly Vector2[] positions;

    public int NodeCount { get; }

    public NavGraph(int nodes)
    {
        NodeCount = nodes;
        neighbours = new int[nodes * MaxNeighbours];
        Array.Fill(neighbours, -1);
        costs = new float[nodes * MaxNeighbours];
        Array.Fill(costs, -1f);
        counts = new byte[nodes];
        passable = new bool[nodes];
        Array.Fill(passable, true);
        positions = new Vector2[nodes];
    }

    public void SetPosition(int node, Vector2 p) => positions[node] = p;
    public Vector2 Position(int node) => positions[node];
    public bool IsPassable(int node) => node >= 0 && node < NodeCount && passable[node];
    public void SetPassable(int node, bool value) => passable[node] = value;

    /// <summary>FUN_000e2704: edges both ways (updates the cost if the edge exists).</summary>
    public void Connect(int a, int b, float cost)
    {
        if (a < 0 || b < 0)
            return;
        AddEdge(a, b, cost);
        AddEdge(b, a, cost);
    }

    /// <summary>FUN_000e27a8: removes the edges between a and b.</summary>
    public void Disconnect(int a, int b)
    {
        if (a < 0 || b < 0)
            return;
        RemoveEdge(a, b);
        RemoveEdge(b, a);
    }

    void AddEdge(int a, int b, float cost)
    {
        int n = counts[a];
        if (n >= MaxNeighbours)
            return;
        for (int k = 0; k < n; k++)
            if (neighbours[a * MaxNeighbours + k] == b)
            {
                costs[a * MaxNeighbours + k] = cost;
                return;
            }
        neighbours[a * MaxNeighbours + n] = b;
        costs[a * MaxNeighbours + n] = cost;
        counts[a]++;
    }

    void RemoveEdge(int a, int b)
    {
        int n = counts[a], baseI = a * MaxNeighbours;
        for (int k = 0; k < n; k++)
            if (neighbours[baseI + k] == b)
            {
                for (int j = k; j < n - 1; j++)
                {
                    neighbours[baseI + j] = neighbours[baseI + j + 1];
                    costs[baseI + j] = costs[baseI + j + 1];
                }
                counts[a]--;
                neighbours[baseI + n - 1] = -1;
                costs[baseI + n - 1] = -1;
                return;
            }
    }

    /// <summary>FUN_000e2894: cost of edge a -> b, or -1.</summary>
    public float Cost(int a, int b)
    {
        for (int k = 0; k < counts[a]; k++)
            if (neighbours[a * MaxNeighbours + k] == b)
                return costs[a * MaxNeighbours + k];
        return -1f;
    }

    public IEnumerable<int> Neighbours(int a)
    {
        for (int k = 0; k < counts[a]; k++)
            yield return neighbours[a * MaxNeighbours + k];
    }

    float Heuristic(int a, int b) => Vector2.Distance(positions[a], positions[b]);

    /// <summary>
    /// The original A* (FUN_00071460): open list scanned for the lowest f (first wins ties), closed nodes and
    /// impassable nodes are never entered (so the goal must be passable). Returns the node list or null.
    /// </summary>
    public List<int>? FindPath(int start, int goal)
    {
        if (start < 0 || goal < 0 || start >= NodeCount || goal >= NodeCount)
            return null;
        var g = new float[NodeCount];
        var f = new float[NodeCount];
        var parent = new int[NodeCount];
        var closed = new bool[NodeCount];
        var open = new List<int> { start };
        var inOpen = new bool[NodeCount];
        inOpen[start] = true;
        g[start] = 0;
        f[start] = Heuristic(start, goal);
        parent[start] = -1;
        while (open.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < open.Count; i++)
                if (f[open[i]] < f[open[best]])
                    best = i;
            int cur = open[best];
            if (cur == goal)
                return Reconstruct(parent, start, goal);
            open[best] = open[^1];
            open.RemoveAt(open.Count - 1);
            inOpen[cur] = false;
            closed[cur] = true;
            for (int k = 0; k < counts[cur]; k++)
            {
                int nb = neighbours[cur * MaxNeighbours + k];
                float c = costs[cur * MaxNeighbours + k];
                if (nb < 0 || c < 0 || closed[nb] || !passable[nb])
                    continue;
                float ng = g[cur] + c;
                if (inOpen[nb])
                {
                    if (ng >= g[nb])
                        continue;
                }
                else
                {
                    open.Add(nb);
                    inOpen[nb] = true;
                }
                parent[nb] = cur;
                g[nb] = ng;
                f[nb] = ng + Heuristic(nb, goal);
            }
        }
        return null;
    }

    static List<int> Reconstruct(int[] parent, int start, int goal)
    {
        var path = new List<int>();
        for (int n = goal; n != -1; n = parent[n])
        {
            path.Add(n);
            if (n == start)
                break;
        }
        path.Reverse();
        return path;
    }
}

/// <summary>
/// Movement along a path (original PathTween): each edge takes cost * msPerUnit milliseconds and the
/// position is interpolated linearly between node positions.
/// </summary>
public sealed class PathTween
{
    readonly NavGraph graph;
    readonly List<int> nodes;
    readonly float msPerUnit;
    int index;    // +0x10
    int elapsed;  // +0x14

    public PathTween(NavGraph graph, List<int> nodes, float msPerUnit, int trimEnd = 0)
    {
        this.graph = graph;
        this.nodes = nodes;
        this.msPerUnit = msPerUnit;
        // FUN_000e28e4: the caller may stop short of the goal
        if (trimEnd > 0 && nodes.Count - trimEnd >= 1)
            nodes.RemoveRange(nodes.Count - trimEnd, trimEnd);
    }

    public bool IsFinished => index >= nodes.Count - 1;
    public int CurrentCell => nodes[index];
    public int NextCell => index + 1 < nodes.Count ? nodes[index + 1] : nodes[index];
    public int GoalCell => nodes[^1];

    /// <summary>PathTween::update.</summary>
    public void Update(int dtMs)
    {
        int last = nodes.Count - 1;
        if (index >= last)
            return;
        elapsed += dtMs;
        while (true)
        {
            int duration = (int)(graph.Cost(nodes[index], nodes[index + 1]) * msPerUnit);
            if (elapsed <= duration)
                return;
            elapsed -= duration;
            index++;
            if (index == last)
            {
                elapsed = 0;
                return;
            }
        }
    }

    /// <summary>FUN_000e24dc: current position.</summary>
    public Vector2 Position
    {
        get
        {
            var a = graph.Position(nodes[index]);
            if (elapsed == 0 || index + 1 >= nodes.Count)
                return a;
            var b = graph.Position(nodes[index + 1]);
            float t = elapsed / (graph.Cost(nodes[index], nodes[index + 1]) * msPerUnit);
            return a + (b - a) * t;
        }
    }

    /// <summary>MoveTask::stop: finish at the next node instead of the goal.</summary>
    public void StopSoon()
    {
        int keep = Math.Min(nodes.Count, index + 2);
        if (keep < nodes.Count)
            nodes.RemoveRange(keep, nodes.Count - keep);
    }
}
