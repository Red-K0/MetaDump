using System.Runtime.InteropServices;
using static Analyzer.Formatter;
using System.Reflection;

namespace Analyzer;

internal abstract class Wrapper
{
	public abstract string Name { get; }
	public abstract string Kind { get; }
	public abstract int Rank { get; }

	public abstract void Print(string indent, bool isLast);

	protected static int GetRank(bool @public, bool @protected, bool @internal, bool @private) =>
		  @private ? 0 :
		@protected ? 1 :
		 @internal ? 2 :
		   @public ? 3 :
		             4 ;

	protected static string GetVisibility(bool @public, bool @protected, bool @internal, bool @private) =>
		   @public ?    "public" :
		@protected ? "protected" :
		 @internal ?  "internal" :
		  @private ?   "private" :
		               "unknown" ;
}

class FieldWrapper(FieldInfo field) : Wrapper
{
	public FieldInfo Field = field;

	public override string Name => Field.Name;
	public override string Kind => "Field";
	public override int Rank => GetRank(Field.IsPublic, Field.IsFamily, Field.IsAssembly, Field.IsPrivate);

	public override void Print(string indent, bool isLast)
	{
		Add(indent + (isLast ? "└─" : "├─"));

		string visibility = GetVisibility(Field.IsPublic, Field.IsFamily, Field.IsAssembly, Field.IsPrivate);
		string typeName = FormatTypeName(Field.FieldType, out var typeColor);

		AddColored($"{visibility} ", Colors.ModifierColor);

		if (Field.IsStatic) AddColored("static ", Colors.ModifierColor);

		AddColored($"{typeName} ", typeColor);

		AddColored($"{Field.Name}\n", Colors.StandardColor);
	}
}

class PropertyWrapper(PropertyInfo property) : Wrapper
{
	public PropertyInfo Property = property;

	public override string Name => Property.Name;
	public override string Kind => "Property";
	public override int Rank
	{
		get
		{
			MethodInfo? get = Property.GetGetMethod(true);
			MethodInfo? set = Property.GetSetMethod(true);

			return GetRank(
				get?.IsPublic   ?? set?.IsPublic   ?? false,
				get?.IsFamily   ?? set?.IsFamily   ?? false,
				get?.IsAssembly ?? set?.IsAssembly ?? false,
				get?.IsPrivate  ?? set?.IsPrivate  ?? true
			);
		}
	}

	public override void Print(string indent, bool isLast)
	{
		Add(indent + (isLast ? "└─" : "├─"));

		MethodInfo? get = Property.GetGetMethod(true);
		MethodInfo? set = Property.GetSetMethod(true);

		string visibility = GetVisibility(get?.IsPublic ?? set?.IsPublic ?? false, get?.IsFamily ?? set?.IsFamily ?? false, get?.IsAssembly ?? set?.IsAssembly ?? false, get?.IsPrivate ?? set?.IsPrivate ?? true);
		string typeName = FormatTypeName(Property.PropertyType, out string? typeColor);

		AddColored($"{visibility} ", Colors.ModifierColor);

		if ((get?.IsStatic ?? false) || (set?.IsStatic ?? false)) AddColored("static ", Colors.ModifierColor);

		AddColored($"{typeName} ", typeColor);

		AddColored($"{Property.Name}\n", Colors.StandardColor);
	}
}

class MethodWrapper(MethodInfo method) : Wrapper
{
	public MethodInfo Method = method;

	public override string Name => Method.Name;
	public override string Kind => "Method";
	public override int Rank => GetRank(Method.IsPublic, Method.IsFamily, Method.IsAssembly, Method.IsPrivate);

	public override void Print(string indent, bool isLast)
	{
		Add(indent + (isLast ? "└─" : "├─"));

		string visibility = GetVisibility(Method.IsPublic, Method.IsFamily, Method.IsAssembly, Method.IsPrivate);
		string returnType = FormatTypeName(Method.ReturnType, out string? returnColor);

		AddColored($"{visibility} ", Colors.ModifierColor);

		if (Method.IsStatic) AddColored("static ", Colors.ModifierColor);
		else if (Method.IsAbstract) AddColored("abstract ", Colors.ModifierColor);
		else if (Method.IsVirtual && !Method.IsFinal) AddColored("virtual ", Colors.ModifierColor);

		AddColored($"{returnType} ", returnColor);
		AddColored($"{Method.Name}", Colors.MethodColor);
		Add("(");

		string parameters = string.Join(", ", Method.GetParameters().Select(parameter =>
		{
			string modifier = parameter.IsOut ? "out" :
							  parameter.ParameterType.IsByRef && parameter.GetCustomAttribute<InAttribute>() != null ? "in" :
							  parameter.ParameterType.IsByRef ? "ref" :
							  parameter.GetCustomAttribute<ParamArrayAttribute>() != null ? "params" : "";

			Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;

			string formattedName = FormatTypeName(type, out string? parameterColor);

			return FormatParameter(modifier, formattedName, parameterColor, parameter.Name!, parameter.HasDefaultValue ? parameter.DefaultValue : null);
		}));

		AddColored($"{parameters}", Colors.StandardColor);
		AddLine(")");
	}
}
