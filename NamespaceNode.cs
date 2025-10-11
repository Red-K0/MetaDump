using System.Reflection;
using System.Text;

namespace Analyzer;

class NamespaceNode(string name)
{
	public Dictionary<string, NamespaceNode> Children { get; } = [];
	public List<Type> Types { get; } = [];
	public string Name => name;

	public NamespaceNode GetOrAddChild(string name)
	{
		if (!Children.TryGetValue(name, out NamespaceNode? value))
		{
			value = new NamespaceNode(name);
			Children[name] = value;
		}

		return value;
	}

	public void Print(string indent = "", bool isLast = true, int depth = 0)
	{
		if (!string.IsNullOrEmpty(Name))
		{
			string color = GetLayerColor(depth);
			Formatter.AddLine($"{indent}{color}{(isLast ? "└─" : "├─")}{Colors.Reset}{Name}");
			indent += $"{color}{(isLast ? "  " : "│ ")}{Colors.Reset}";
			depth++;
		}

		NamespaceNode[] children = [.. Children.Values];

		for (int i = 0; i < children.Length; i++) children[i].Print(indent, i == children.Length - 1 && Types.Count == 0, depth);

		for (int i = 0; i < Types.Count; i++)
		{
			Type type = Types[i];
			string formattedName = Formatter.FormatTypeName(type, out string typeColor);

			List<Type> inheritanceTypes = [];
			if (type.BaseType is { } baseType && baseType != typeof(object)) inheritanceTypes.Add(baseType);

			inheritanceTypes.AddRange(type.GetInterfaces().Except(type.BaseType?.GetInterfaces() ?? []));

			string inheritanceStr = "";
			if (inheritanceTypes.Count > 0)
			{
				inheritanceStr = " : " + string.Join(", ", inheritanceTypes.Select(static t =>
				{
					string formattedName = Formatter.FormatTypeName(t, out string tColor);
					return $"{tColor}{formattedName}{Colors.Reset}";
				}));
			}

			bool endFlag = i == Types.Count - 1;
			string layerColor = GetLayerColor(depth);

			Formatter.Add($"{indent}{layerColor}{(endFlag ? "└─" : "├─")}{Colors.Reset}");
			Formatter.AddColored(formattedName, typeColor);
			Formatter.Add(inheritanceStr);

			if (type.IsGenericTypeDefinition)
			{
				foreach (Type param in type.GetGenericArguments())
				{
					GenericParameterAttributes attrs = param.GenericParameterAttributes;
					Type[] constraints = param.GetGenericParameterConstraints();
					List<string> parts = [];

					if (attrs.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add($"{Colors.KeywordColor}struct{Colors.Reset}");
					if (attrs.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) parts.Add($"{Colors.KeywordColor}class{Colors.Reset}");

					parts.AddRange(constraints.Select(t =>
					{
						string tName = Formatter.FormatTypeName(t, out string tColor);
						return $"{tColor}{tName}{Colors.Reset}";
					}));

					if ((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0) parts.Add($"{Colors.KeywordColor}new(){Colors.Reset}");

					if (parts.Count > 0)
					{
						Formatter.Add($" {Colors.KeywordColor}where{Colors.Reset} ");
						Formatter.AddColored(param.Name, Colors.GenericParameterColor);
						Formatter.Add($" {Colors.GenericBracketColor}:{Colors.Reset} {string.Join($"{Colors.GenericBracketColor}, {Colors.Reset}", parts)}");
					}
				}
			}

			Formatter.AddLine();
			PrintMembers(indent + $"{layerColor}{(endFlag ? "  " : "│ ")}{Colors.Reset}", type, depth + 1);
		}
	}

	private static string GetLayerColor(int depth)
	{
		string[] colors = Colors.LayerColors;
		if (colors.Length == 0) return Colors.StandardColor;
		return colors[depth % colors.Length];
	}

	private static void PrintMembers(string indent, Type type, int depth)
	{
		const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		PropertyInfo[] properties = type.GetProperties(flags);
		MethodInfo[] methods = type.GetMethods(flags);
		FieldInfo[] fields = type.GetFields(flags);

		List<Wrapper> allMembers =
		[
			.. fields.Select(f => new FieldWrapper(f)),
			.. properties.Select(p => new PropertyWrapper(p)),
			.. methods.Select(m => new MethodWrapper(m)),
		];

		allMembers = [.. allMembers.OrderBy(m => m.Rank).ThenBy(static m => m.Name)];
		int? lastVisibilityRank = null;
		string color = GetLayerColor(depth);

		for (int i = 0; i < allMembers.Count; i++)
		{
			Wrapper member = allMembers[i];

			if (lastVisibilityRank.HasValue && member.Rank != lastVisibilityRank.Value) Formatter.AddLine($"{indent}{color}│{Colors.Reset}");

			lastVisibilityRank = member.Rank;
			member.Print($"{indent}{color}", i == allMembers.Count - 1);
		}

		Formatter.AddLine($"{indent}{color}{Colors.Reset}");
	}
}
