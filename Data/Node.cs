using System.Diagnostics;
using MetaDump.Backend;
using MetaDump.Data.Members;

namespace MetaDump.Data;

[DebuggerDisplay("[Name = {Name ?? \"<root>\"}, Children = {Children.Count}, Types = {Types.Count}]")]
public sealed class Node
{
	private readonly Dictionary<string, Node>.AlternateLookup<ReadOnlySpan<char>> SpanChildren;
	public readonly bool IsRoot;
	public string Name;

	public TypeData? Type;

	public Dictionary<string, Node> Children { get; } = [];
	public List<TypeData> Types { get; } = [];

	public Node(ReadOnlySpan<char> typeName, ReadOnlySpan<Type> types = new())
	{
		SpanChildren = Children.GetAlternateLookup<ReadOnlySpan<char>>();

		Name = typeName.ToString();
		IsRoot = true;

		foreach (Type type in types)
		{
			Node current = this;

			ReadOnlySpan<char> name = type.Name.Replace('+', '.');

			foreach (Range part in name.Split('.')) current = current.GetOrAddChild((name)[part]);

			current.Types.Add(new(type, null));
		}

		CleanTree();
		PromoteTypes();
	}

	private Node GetOrAddChild(ReadOnlySpan<char> name)
	{
		if (!SpanChildren.TryGetValue(name, out Node? value))
		{
			value = new Node(name);
			SpanChildren[name] = value;
		}

		return value;
	}

	private void CleanTree()
	{
		Stack<Node> stack = new();
		stack.Push(this);

		while (stack.Count > 0)
		{
			Node node = stack.Pop();

			foreach ((string name, Node child) in node.Children)
			{
				if (child.Types.Any(t => t.Type.Name == name && t.Type.Namespace?[(t.Type.Namespace.LastIndexOf('.') + 1)..] != name) && child.Children.Count == 0)
				{
					node.Types.AddRange(child.Types);
					node.Children.Remove(name);
					continue;
				}

				stack.Push(child);
			}
		}
	}

	private void PromoteTypes()
	{
		Stack<Node> stack = new();
		stack.Push(this);

		while (stack.Count > 0)
		{
			Node node = stack.Pop();

			for (int i = 0; i < node.Types.Count; i++)
			{
				TypeData data = node.Types[i];

				if (data.Type.Name == node.Name)
				{
					node.Types.RemoveAt(i);

					node.Name = data.GetFormattedName(true);
					node.Type = data;

					break;
				}
			}

			foreach (Node child in node.Children.Values) stack.Push(child);
		}
	}

	public int PopulateTypes()
	{
		int memberCount = 0;

		foreach (TypeData data in Types) memberCount += data.Populate();

		return memberCount;
	}

	public void Print(string indent = "", bool isLast = true, int depth = 0)
	{
		if (!string.IsNullOrEmpty(Name))
		{
			if (IsRoot)
			{
				Output.Add($"{Name}\n");
			}
			else
			{
				Output.Add(indent);
				Output.Add(isLast ? "└─" : "├─", depth);
				Output.Add($"{Name}\n");

				indent += (isLast ? "  " : "│ ").WithColor(depth);

				depth++;
			}
		}

		if (Type != null) PrintMembers(indent, Type, depth, false);

		Node[] children = [.. Children.Values];

		for (int i = 0; i < children.Length; i++) children[i].Print(indent, i == children.Length - 1 && Types.Count == 0, depth);

		for (int i = 0; i < Types.Count; i++)
		{
			TypeData type = Types[i];
			Output.Add(indent);
			Output.Add(i == Types.Count - 1 ? "└─" : "├─", depth);
			Output.Add($"{Types[i].GetFormattedName(true)}\n");

			PrintMembers(indent + (i == Types.Count - 1 ? "  " : "│ ") .WithColor(depth), type, depth + 1);
		}
	}

	private static void PrintMembers(string indent, TypeData type, int depth, bool end = true)
	{
		if (type.Members.Length == 0) { Output.Add($"{indent}\n"); return; }

		DataType lastDataType = type.Members[0].DataType;

		foreach (IMemberData member in type.Members)
		{
			if (member.DataType != lastDataType)
			{
				lastDataType = member.DataType;
				Output.Add($"{indent}{"│".WithColor(depth)}\n");
			}

			Output.Add($"{indent}{((end && ReferenceEquals(member, type.Members[^1])) ? "└─" : "├─").WithColor(depth)}{member.GetFormattedName()}\n");
		}

		Output.Add($"{indent}{(end ? "" : "│".WithColor(depth))}\n");
	}
}
