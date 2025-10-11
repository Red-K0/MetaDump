using static Analyzer.Colors;
using System.Text;

namespace Analyzer;

internal static partial class Formatter
{
	internal static StringBuilder Result = new();

#if RELEASE // Enable actual buffer in release builds
	public static void Add(string text) => Result.Append(text);
	public static void AddLine(string text = "") => Result.AppendLine(text);
#else
	public static void Add(string text) => Console.Write(text);
	public static void AddLine(string text = "") => Console.WriteLine(text);
#endif

	public static void AddColored(string text, string ansiCode) => Add($"{ansiCode}{text}{Reset}");
	public static string CreateTree(Type[] types)
	{
		NamespaceNode root = new("");

		foreach (Type type in types)
		{
			NamespaceNode current = root;
			foreach (string part in (type.Namespace ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries)) current = current.GetOrAddChild(part);
			current.Types.Add(type);
		}

		AddLine($"{LayerColors[0]}┐{Reset}");

		root.Print();

		return Result.ToString();
	}

	public static string FormatParameter(string modifier, string typeName, string typeColor, string name, object? defaultValue)
	{
		StringBuilder builder = new();

		if (!string.IsNullOrEmpty(modifier)) builder.Append($"{ModifierColor}{modifier}{Reset} ");

		builder.Append($"{typeColor}{typeName}{Reset} {ParameterColor}{name}{Reset}");

		if (defaultValue == null)
		{
			if (defaultValue is null && typeName != "string" && typeName != "object") builder.Append($" = {KeywordColor}null{Reset}");
		}
		else
		{
			string? value = defaultValue switch
			{
				string @string => $"\"{@string}\"",
				  char   @char => $"'{@char}'",
				_ => defaultValue.ToString()
			};

			builder.Append($" = {StandardColor}{value}{Reset}");
		}

		return builder.ToString();
	}
	public static string FormatTypeName(Type type, out string color)
	{
		if (type.IsByRef) return $"ref {FormatTypeName(type.GetElementType()!, out color)}";
		if (type.IsArray) return $"{FormatTypeName(type.GetElementType()!, out color)}[]";

		string? keyword = Type.GetTypeCode(type) switch
		{
			TypeCode.Boolean =>    "bool",
			TypeCode.   Char =>    "char",
			TypeCode. Single =>   "float",
			TypeCode. Double =>  "double",
			TypeCode.Decimal => "decimal",
			TypeCode.  SByte =>   "sbyte",
			TypeCode.  Int16 =>   "short",
			TypeCode.  Int32 =>     "int",
			TypeCode.  Int64 =>    "long",
			TypeCode.   Byte =>    "byte",
			TypeCode. UInt16 =>  "ushort",
			TypeCode. UInt32 =>    "uint",
			TypeCode. UInt64 =>   "ulong",
			TypeCode. String =>  "string",
			TypeCode.  Empty =>    "void",

			TypeCode.Object when type == typeof(object) => "object",
			_ => null
		};

		if (keyword is not null)
		{
			color = KeywordColor;
			return keyword;
		}

		if (type.IsGenericParameter)
		{
			color = GenericParameterColor;
			return type.Name;
		}

		if (!type.IsGenericType)
		{
			color = GetTypeColor(type);
			return type.Name;
		}

		int backtickIndex = type.Name.IndexOf('`');
		string name = backtickIndex >= 0 ? type.Name[..backtickIndex] : type.Name;
		Type[] arguments = type.GetGenericArguments();
		color = GetTypeColor(type);

		StringBuilder builder = new();
		builder.Append($"{GenericBracketColor}<{Reset}");

		bool first = true;
		foreach (Type argument in arguments)
		{
			if (!first) builder.Append(", "); else first = false;

			string argumentName = FormatTypeName(argument, out string argumentColor);
			builder.Append($"{argumentColor}{argumentName}{Reset}");
		}

		builder.Append($"{GenericBracketColor}>{Reset}");

		return $"{name}{builder}";

		static string GetTypeColor(Type type)
		{
			if (type.IsInterface) return InterfaceColor;
			if (type.IsValueType) return StructColor;
			if (type.IsEnum) return EnumColor;
			return ClassColor;
		}
	}
}
