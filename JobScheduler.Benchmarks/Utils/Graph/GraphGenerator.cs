using static Schedulers.Benchmarks.Utils.Graph.DirectedAcyclicGraph;

namespace Schedulers.Benchmarks.Utils.Graph;

public class GraphGenerator
{
    public readonly struct RandomGraphSettings
    {
        /// <summary>
        /// How many total nodes are in the graph
        /// </summary>
        public required int Nodes { get; init; }

        /// <summary>
        /// How many nodes can appear in a given rank (end index is exclusive)
        /// For example, pass (1, 5) to get anywhere within [1, 2, 3, 4] nodes in a given rank.
        /// The expected amount of ranks E(Ranks) will then be E(Ranks) = Nodes / E(NodesPerRank)
        /// </summary>
        public required Range NodesPerRank { get; init; }

        /// <summary>
        /// The seed for the random number generator: if null, a random seed.
        /// </summary>
        public required int? Seed { get; init; }

        /// <summary>
        /// The chance that an edge will be spawned between any two nodes.
        /// </summary>
        public required float EdgeChance { get; init; }

        /// <summary>
        /// Limits the degree of the graph (how many connecting nodes is possible)
        /// </summary>
        public required int MaxDegree { get; init; }
    }

    // adapted from https://stackoverflow.com/a/12790718
    public static DirectedAcyclicGraph GenerateRandomGraph(in RandomGraphSettings config)
    {
        Random r = config.Seed is not null ? new(config.Seed.Value) : new();

        Dictionary<int, Node> nodes = new();

        var totalNodes = 0;
        var goalNodes = config.Nodes - 1; // exclude root node

        while (totalNodes < goalNodes)
        {
            var newNodes = r.Next(config.NodesPerRank.Start.Value, config.NodesPerRank.End.Value);

            // if we've exceeded our desired node count, only go up to the limit for the very last rank
            if (newNodes + totalNodes > goalNodes)
            {
                newNodes = goalNodes - totalNodes;
            }

            // add all the new nodes
            for (var newNode = totalNodes; newNode < totalNodes + newNodes; newNode++)
            {
                // + 1 to leave room for a root node ID; just for DOT (we don't actually use the node ID in this algo)
                nodes[newNode] = new Node(newNode + 1);
            }

            // check pairs of new nodes and old nodes from all previous ranks and make edges
            // randomized to prevent bias towards already-seen nodes from the degree limit
            foreach (var node in Enumerable.Range(0, totalNodes).OrderBy(x => r.Next()))
            {
                foreach (var newNode in Enumerable.Range(totalNodes, newNodes).OrderBy(x => r.Next()))
                {
                    // if we break the degree, don't even try to make a node between these two nodes
                    if (nodes[node].Degree >= config.MaxDegree)
                    {
                        continue;
                    }

                    if (nodes[newNode].Degree >= config.MaxDegree)
                    {
                        continue;
                    }

                    if (r.NextSingle() < config.EdgeChance)
                    {
                        nodes[node].Children.Add(nodes[newNode]);
                        nodes[newNode].Parents.Add(nodes[node]);
                    }
                }
            }

            totalNodes += newNodes;
        }

        // generate a root node that links to all existing roots.
        // Since it's a root, it can break our degree rule.
        var root = new Node(0);
        foreach (var (_, node) in nodes)
        {
            if (node.Parents.Count == 0)
            {
                root.Children.Add(node);
                node.Parents.Add(root);
            }
        }

        return new(root);
    }
}
