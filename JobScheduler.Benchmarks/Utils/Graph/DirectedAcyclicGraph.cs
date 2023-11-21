using System.Text;

namespace Schedulers.Benchmarks.Utils.Graph;

public class DirectedAcyclicGraph
{
    public class Node
    {
        public Node(int id)
        {
            ID = id;
        }
        public int ID { get; }
        public List<Node> Children { get; } = new();
        public List<Node> Parents { get; } = new();
        public int Degree
        {
            get => Children.Count + Parents.Count;
        }

        public object? Data { get; set; } = null;
    }

    public DirectedAcyclicGraph(Node root)
    {
        RootNode = root;
    }

    public Node RootNode { get; }

    /// <summary>
    /// Returns a string representation of the graph in DOT.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        static void addNode(Node node, HashSet<Node> nodes)
        {
            nodes.Add(node);
            foreach (var child in node.Children)
            {
                addNode(child, nodes);
            }
        }

        // ensure we don't track duplicate nodes
        HashSet<Node> nodes = new();
        addNode(RootNode, nodes);

        StringBuilder sb = new();
        sb.AppendLine($"digraph {nameof(DirectedAcyclicGraph)} {{");
        foreach (var node in nodes)
        {
            foreach (var child in node.Children)
            {
                sb.AppendLine($"{node.ID} -> {child.ID};");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
