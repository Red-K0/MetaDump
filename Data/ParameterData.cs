using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MetaDump.Backend;
using MetaDump.Runtime;

namespace MetaDump.Data;

internal sealed class ParameterData
{
	private readonly static Type[] _enumTypes =
	[
		typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),

		typeof(double), typeof(float), typeof(decimal),

		typeof(nint), typeof(nuint)
	];

	private readonly static FrozenDictionary<Modifier, string> _keywords = new Dictionary<Modifier, string>()
	{
		[Modifier.ReadonlyReference] = "readonly ref",
		[Modifier.Reference] = "ref",
		[Modifier.Out] = "out",
		[Modifier.In] = "in"
	}.ToFrozenDictionary();

	public ParameterInfo Info { get; }

	public Modifier Keyword { get; }

	public TypeData TypeData { get; }

	public ParameterData(ParameterInfo info)
	{
		Info = info;

		Keyword = DetermineModifier();

		TypeData = new(info.ParameterType, NullContext.Create(Info));

		if (Keyword is not Modifier.None) TypeData = TypeData.NestedData[0];
	}

	public Modifier DetermineModifier()
	{
		if (Info.IsOut) return Modifier.Out;

		if (Info.IsIn) return Info.GetCustomAttribute<RequiresLocationAttribute>() != null ? Modifier.ReadonlyReference : Modifier.In;

		if (Info.ParameterType.IsByRef) return Modifier.Reference;

		if (Info.GetCustomAttribute<ParamArrayAttribute>() != null) return Modifier.None;

		return Modifier.None;
	}

	public string GetFormattedName(bool skipName = false)
	{
		StringBuilder builder = new(32);

		if (!skipName && Info.Name == null) // Compiler generated parameters behave strangely.
		{
			return Utilities.TransformDisplayClass(TypeData.Type);
		}
		else
		{
			if (Keyword is not Modifier.None) builder.Append($"{_keywords[Keyword].WithColor(Color.Keyword)} ");

			builder.Append($"{TypeData.GetFormattedName()}");

			if (!skipName) builder.Append($" {Info.Name!.WithColor(Color.Parameter)}");

			if (Info.HasDefaultValue) builder.Append($" {"=".WithColor(Color.Operator)} {GetFormattedValue()}");
		}

		return builder.ToString();
	}

	private string GetFormattedValue()
	{
		object value = Info.RawDefaultValue!;

		Type? type = value?.GetType();

		if (_enumTypes.Contains(type))
		{
			string? name;

			if (Info.ParameterType.IsEnum && (name = Enum.GetName(Info.ParameterType, value!)) != null)
			{
				return $"{TypeData.GetFormattedName()}{".".WithColor(Color.Operator)}{name}";
			}
			else
			{
				return value!.ToString()!.WithColor(Color.Literal);
			}
		}

		if (value is ValueType) return $"{"new".WithColor(Color.Keyword)}{"()".WithColor(1)}";

		return Type.GetTypeCode(type) switch
		{
			TypeCode.Boolean => value!.ToString()!.ToLower().WithColor(Color.Keyword),
			TypeCode.  Empty =>                       "null".WithColor(Color.Keyword),
			TypeCode.   Char =>               $"\'{value}\'".WithColor(Color.String),
			TypeCode. String =>               $"\"{value}\"".WithColor(Color.String),

			_ => throw new InvalidOperationException($"No default value could be processed for the type {value?.GetType()!.FullName}."),
		};
	}

	public enum Modifier
	{
		None,
		Reference,
		In,
		Out,
		ReadonlyReference
	}
}
