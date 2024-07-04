using Schedulers.Benchmarks.Utils.Graph;
using System.Diagnostics;

namespace Schedulers.Test;

/*
[TestFixture]
internal class GraphGeneratorTests
{
    [Test]
    [TestCase(16, 4, 0.1f)] // weakly connected tiny graph
    [TestCase(256, 4, 0.1f)] // weakly connected large graph
    [TestCase(128, 8, 0.3f)] // strongly connected medium graph
    [TestCase(128, 4, 0.1f)] // graph used in the benchmark
    public void GeneratedGraphHasCorrectProperties(int nodes, int maxDegree, float edgeChance)
    {
        var graph = GraphGenerator.GenerateRandomGraph(new()
        {
            EdgeChance = edgeChance,
            MaxDegree = maxDegree,
            Nodes = nodes,
            NodesPerRank = new(Math.Max((int)MathF.Sqrt(nodes) - 5, 0), (int)MathF.Sqrt(nodes) + 5), // make approximately square graph
            Seed = (nodes * 3) + (maxDegree * 13) + (int)(edgeChance * 11)
        });

        // Enable this line to log to output so we can check with a DOT viewer.
        // Don't leave it enabled, though -- it clogs test output and NUnit interprets it as a warning.
        // TestContext.Out.WriteLine(graph.ToString());

        HashSet<int> allNodes = new();

        void traverseNode(DirectedAcyclicGraph.Node node, bool isRoot, bool wasRoot)
        {
            allNodes.Add(node.ID);
            // exclude root node from degree validation
            if (!isRoot && !wasRoot)
            {
                Assert.That(node.Degree, Is.LessThanOrEqualTo(maxDegree));
            }

            // we expect level 1 nodes (directly underneath root) to have a single parent (root) and also match our degree constraint
            if (wasRoot)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(node.Degree - 1, Is.InRange(0, maxDegree));
                    Assert.That(node.Parents, Has.Count.EqualTo(1));
                });
            }

            foreach (var child in node.Children)
            {
                Assert.That(child.Parents, Contains.Item(node));
                traverseNode(child, false, isRoot);
            }
        }

        traverseNode(graph.RootNode, true, false);
        Assert.That(allNodes, Has.Count.EqualTo(nodes));
    }
}
*/
