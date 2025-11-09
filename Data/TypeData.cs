using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MetaDump.Backend;
using MetaDump.Data.Members;
using MetaDump.Runtime;

namespace MetaDump.Data;

public sealed class TypeData
{
	private static readonly FrozenDictionary<Type, string> _keywords = new Dictionary<Type, string>
	{
		 [typeof(void)] =  "void", [typeof(object)] = "object",  [typeof(string)] =  "string",   [typeof(nint)] =   "nint", [typeof(nuint)] = "nuint",

		 [typeof(byte)] =  "byte",  [typeof(sbyte)] =  "sbyte",   [typeof(short)] =   "short", [typeof(ushort)] = "ushort",
		
		  [typeof(int)] =   "int",   [typeof(uint)] =   "uint",    [typeof(long)] =    "long",  [typeof(ulong)] =  "ulong",

		 [typeof(bool)] =  "bool",   [typeof(char)] =   "char", [typeof(decimal)] = "decimal",

		[typeof(float)] = "float", [typeof(double)] = "double",

	}.ToFrozenDictionary();

	public IMemberData[] Members { get; private set; } = [];

	public TypeData[] InheritedTypes { get; } = [];

	public TypeData[] NestedData { get; } = [];

	public TypeInfo Type { get; }

	public Modifiers Keywords { private set; get; }

	public TypeData(Type type, NullInfo? info, bool skipInheritance = false)
	{
		Type = type.GetTypeInfo();

		Keywords = DetermineModifiers() | DetermineNullability(info);

		NestedData = DetermineNestedData(info);

		if (!skipInheritance) InheritedTypes = DetermineInheritance();
	}

	public int Populate()
	{
		Members = Utilities.GetMembers(Type).OrderBy(m => m.DataType).ThenByDescending(m => m.AccessModifier).ThenBy(m => m.BaseName).ToArray();

		return Members.Length;
	}

	public string GetFormattedName(bool verbose = false)
	{
		string name = Type.Name;
		bool skipColor = false;

		// Early exits first

		if (Keywords.HasFlag(Modifiers.Pointer)) return name = $"{NestedData[0].GetFormattedName()}{"*".WithColor(Color.Operator)}";

		if (Keywords.HasFlag(Modifiers.Parameter)) name = name.WithColor(Color.GenericParameter);

		if (Keywords.HasFlag(Modifiers.Keyword)) name = _keywords[Type].WithColor(Color.Keyword);

		if (Keywords.HasFlag(Modifiers.Array)) name = $"{NestedData[0].GetFormattedName()}{"[]".WithColor(1)}";

		if (Keywords.HasFlag(Modifiers.Ref)) name = $"{"ref".WithColor(Color.Keyword)} {NestedData[0].GetFormattedName()}";

		// Complex formatting

		if (Keywords.HasFlag(Modifiers.Delegate))
		{
			StringBuilder builder = new(64);

			builder.Append("delegate".WithColor(Color.Keyword)).Append("*".WithColor(Color.Operator));

			builder.Append((Type.IsUnmanagedFunctionPointer ? " unmanaged" : " managed").WithColor(Color.Keyword));

			builder.Append($"<{string.Join(", ", NestedData.Select(t => t.GetFormattedName()))}>");

			return name = builder.ToString();
		}

		if (Keywords.HasFlag(Modifiers.Generic) || (Keywords.HasFlag(Modifiers.Nullable | Modifiers.ValueType)))
		{
			StringBuilder builder = new(64);

			int backtickIndex = name.IndexOf('`');

			builder.Append(Type.Name.AsSpan()[..(backtickIndex != -1 ? backtickIndex : ^0)].WithColor(GetColor()));

			if (NestedData.Length != 0) builder.Append($"<{string.Join(", ", NestedData.Select(t => t.GetFormattedName()))}>");

			name = builder.ToString();
		}

		if (verbose)
		{
			if (Keywords.HasFlag(Modifiers.Inherits)) name += $" : {string.Join(", ", InheritedTypes.Select(t => t.GetFormattedName()))}";

			if (Keywords.HasFlag(Modifiers.Generic)) name += Utilities.GetGenericConstraints(NestedData);
		}

		name = name.WithColor(GetColor());

		return (Keywords.HasFlag(Modifiers.Nullable) && !Keywords.HasFlag(Modifiers.ValueType)) ? $"{name}{"?".WithColor(Color.Operator)}" : name;

		Color GetColor()
		{
			if (skipColor == true) return Color.Standard; else skipColor = true;

			return Keywords.HasFlag(Modifiers.Interface) ? Color.Interface
				 : Keywords.HasFlag(Modifiers.Class)     ? Color.Class
				 : Keywords.HasFlag(Modifiers.Enum)      ? Color.Literal
				 : Keywords.HasFlag(Modifiers.ValueType) ? Color.Struct
				 : Color.Standard;
		}
	}

	private Modifiers DetermineModifiers()
	{
		Modifiers modifiers = Modifiers.None;

		// Generic parameters need no further input.

		if (Type.IsGenericParameter) return Modifiers.Parameter;

		// Simple type modifiers (T*, T[], ref T, T?)

		if (Type.IsPointer) modifiers |= Modifiers.Pointer;

		if (Type.IsArray) modifiers |= Modifiers.Array;

		if (Type.IsByRef) modifiers |= Modifiers.Ref;

		// Early exit for special types.

		if (_keywords.ContainsKey(Type)) return modifiers | Modifiers.Keyword;

		if (Nullable.GetUnderlyingType(Type) is not null) return modifiers | Modifiers.Nullable | Modifiers.ValueType;

		// Complex type modifiers (Nullable<T>, delegate, X<T>)

		if (Type.IsFunctionPointer) modifiers |= Modifiers.Delegate;

		if (Type.IsGenericType) modifiers |= Modifiers.Generic;

		// Base type modifiers (class, struct, interface, enum)

		if (Type.IsEnum) return modifiers | Modifiers.Enum;

		if (Type.IsValueType) return modifiers | Modifiers.ValueType;

		if (Type.IsInterface) return modifiers | Modifiers.Interface;

		// If nothing else, default to a basic class.

		return modifiers | Modifiers.Class;
	}

	private Modifiers DetermineNullability(NullInfo? info)
	{
		// If no info is given, this is a 'best effort' approach to the problem.

		return ((info?.WriteState | info?.ReadState) == NullState.Nullable) || (Type.GetCustomAttribute<NullableAttribute>()?.NullableFlags[0] == 2) ? Modifiers.Nullable : Modifiers.None;
	}

	private TypeData[] DetermineNestedData(NullInfo? info)
	{
		if (Keywords.HasFlag(Modifiers.Nullable | Modifiers.ValueType)) return [new(Nullable.GetUnderlyingType(Type)!, null)];

		if (Keywords.HasFlag(Modifiers.Generic))
		{
			List<Type> types = (Type.IsGenericTypeDefinition ? Type.GenericTypeParameters : Type.GenericTypeArguments).ToList();

			if (Type.IsNested)
			{
				TypeInfo declaringType = Type.DeclaringType!.GetTypeInfo();

				Type[] declaringGenerics = declaringType.IsGenericTypeDefinition ? declaringType.GenericTypeParameters : declaringType.GenericTypeArguments;

				types = types.Where(t => !declaringGenerics.Any(d => d.Name == t.Name)).ToList();
			}

			return types.Select((t, i) => new TypeData(t, info?.GenericTypeArguments[i], true)).ToArray();
		}

		if (Keywords.HasFlag(Modifiers.Delegate)) return Type.GetFunctionPointerParameterTypes().Select(t => new TypeData(t, null)).Append(new(Type.GetFunctionPointerReturnType(), null)).ToArray();

		if (Keywords.HasFlag(Modifiers.Pointer) || Keywords.HasFlag(Modifiers.Ref)) return [new(Type.GetElementType()!, null)];

		if (Keywords.HasFlag(Modifiers.Array)) return [new(Type.GetElementType()!, info?.ElementType)];

		return [];
	}

	private TypeData[] DetermineInheritance()
	{
		List<Type> inheritanceTypes = [];

		if (Type.BaseType is { } baseType && (baseType != typeof(object))) inheritanceTypes.Add(baseType);

		inheritanceTypes.AddRange(Type.GetInterfaces().Except(Type.BaseType?.GetInterfaces() ?? []));

		if (inheritanceTypes.Count != 0) Keywords |= Modifiers.Inherits;

		return inheritanceTypes.Select(t => new TypeData(t, null, true)).ToArray();
	}

	[Flags]
	public enum Modifiers
	{
		None = 0,

		Keyword   = 1 <<  0,
		ValueType = 1 <<  1,
		Class     = 1 <<  2,
		Interface = 1 <<  3,

		Array     = 1 <<  4,
		Pointer   = 1 <<  5,
		Nullable  = 1 <<  6,
		Ref       = 1 <<  7,

		Delegate  = 1 <<  8,
		Enum      = 1 <<  9,
		Generic   = 1 << 10,
		Parameter = 1 << 11,

		Inherits  = 1 << 12
	}
}
