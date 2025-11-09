using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MetaDump.Backend;
using MetaDump.Runtime;

namespace MetaDump.Data.Members;

internal sealed class MethodData : IMemberData
{
	private static readonly FrozenDictionary<string, string> _operators = new Dictionary<string, string>(EqualityComparer<string>.Default)
	{
		["op_Addition"]    = "+",
		["op_Subtraction"] = "-",
		["op_Multiply"]    = "*",
		["op_Division"]    = "/",
		["op_Modulus"]     = "%",

		["op_UnaryPlus"]      = "+",
		["op_UnaryNegation"]  = "-",
		["op_LogicalNot"]     = "!",
		["op_OnesComplement"] = "~",
		["op_Increment"]      = "++",
		["op_Decrement"]      = "--",

		["op_Equality"]           = "==",
		["op_Inequality"]         = "!=",
		["op_GreaterThan"]        = ">",
		["op_LessThan"]           = "<",
		["op_GreaterThanOrEqual"] = ">=",
		["op_LessThanOrEqual"]    = "<=",

		["op_BitwiseAnd"]  = "&",
		["op_BitwiseOr"]   = "|",
		["op_ExclusiveOr"] = "^",
		["op_LeftShift"]   = "<<",
		["op_RightShift"]  = ">>",

		["op_True"]  = "true",
		["op_False"] = "false",

		["op_Assign"]             = "=",
		["op_MemberSelection"]    = "->",
		["op_PointerDereference"] = "*",
	}.ToFrozenDictionary();

	public string BaseName => Info.Name;

	public MethodInfo Info { get; }

	public DataType DataType => DataType.Method;

	public AccessModifier AccessModifier { get; }

	public Modifiers Keywords { get; }

	public TypeData ReturnType { get; }

	public TypeData[] GenericParameters { get; }

	public ParameterData[] Parameters { get; }

	public MethodData(MethodInfo info)
	{
		Info = info;

		AccessModifier = DetermineAccessModifier();

		Keywords = DetermineModifier();

		ReturnType = new(Info.ReturnType, NullContext.Create(Info.ReturnParameter));

		Parameters = Info.GetParameters().Select(p => new ParameterData(p)).ToArray();

		GenericParameters = Info.GetGenericArguments().Select(t => new TypeData(t, null)).ToArray();
	}

	private AccessModifier DetermineAccessModifier() =>
		  Info.IsPublic            ? AccessModifier.Public
		: Info.IsPrivate           ? AccessModifier.Private
		: Info.IsAssembly          ? AccessModifier.Internal
		: Info.IsFamily            ? AccessModifier.Protected
		: Info.IsFamilyOrAssembly  ? AccessModifier.ProtectedInternal
		: Info.IsFamilyAndAssembly ? AccessModifier.PrivateProtected
		/* Ordered by frequency */ : AccessModifier.Unknown;

	private Modifiers DetermineModifier()
	{
		// This method should have some way to detect the 'new' keyword, but this is ridiculously expensive if accounting for overloads, so no check is in place.

		Modifiers modifiers = Modifiers.None;

		if (Info.IsGenericMethod) modifiers |= Modifiers.Generic;

		if (((ReadOnlySpan<char>)BaseName).StartsWith("op") && Info.IsSpecialName) modifiers |= Modifiers.Operator;

		// Order these checks first. If the 'static' check passes, we don't need to do anything else.

		if (Info.IsDefined(typeof(AsyncStateMachineAttribute), false)) modifiers |= Modifiers.Async;

		if (Info.Attributes.HasFlag(MethodAttributes.PinvokeImpl) || Info.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall)) modifiers |= Modifiers.Extern;

		if (Info.IsStatic) return modifiers | Modifiers.Static;

		// The 'readonly' modifier is only applicable to struct instance methods, so nothing past here should be checked.

		if (Info.GetCustomAttribute<IsReadOnlyAttribute>() != null) return modifiers | Modifiers.Readonly;

		// If the method is abstract, it can't also be virtual to begin with.

		if (Info.IsAbstract) return modifiers | Modifiers.Abstract;

		// If none of the early exits above are taken, we can do virtual checks.

		if (Info.IsVirtual)
		{
			if (Info.GetBaseDefinition() != Info) return modifiers | (Modifiers.Override | (Info.IsFinal ? Modifiers.Sealed : 0));

			return modifiers | Modifiers.Virtual;
		}

		// Just exit here normally.

		return modifiers;
	}

	public string GetFormattedName()
	{
		StringBuilder builder = new(64);

		builder.Append($"{Utilities.GetAccessString(AccessModifier)} {GetKeywordString()}");

		if (Keywords.HasFlag(Modifiers.Operator))
		{
			bool reverseOrder = false;

			if (string.Equals(BaseName, "op_Implicit")) { reverseOrder = true; builder.Append("implicit".WithColor(Color.Keyword)); }
			if (string.Equals(BaseName, "op_Explicit")) { reverseOrder = true; builder.Append("explicit".WithColor(Color.Keyword)); }

			if (reverseOrder)
			{
				builder.Append(' ').Append(ReturnType.GetFormattedName());
			}
			else
			{
				builder.Append($"{ReturnType.GetFormattedName()} {_operators[BaseName].WithColor(Color.Operator)}");
			}
		}
		else
		{
			builder.Append($"{ReturnType.GetFormattedName()} {Info.Name.SanitizeInterfaceNames(this)}");
		}

		if (Keywords.HasFlag(Modifiers.Generic)) builder.Append($"<{string.Join(", ", GenericParameters.Select(t => t.GetFormattedName()))}>");

		builder.Append($"({string.Join(", ", Parameters.Select(p => p.GetFormattedName()))})");

		return builder.ToString();
	}

	private string GetKeywordString()
	{
		if ((Keywords &~ Modifiers.Generic) == 0) return "";

		StringBuilder builder = new(32);

		if (Keywords.HasFlag(Modifiers.Static))   builder.Append("static ");
		if (Keywords.HasFlag(Modifiers.Readonly)) builder.Append("readonly ");
		if (Keywords.HasFlag(Modifiers.Abstract)) builder.Append("abstract ");
		if (Keywords.HasFlag(Modifiers.Virtual))  builder.Append("virtual ");
		if (Keywords.HasFlag(Modifiers.Sealed))   builder.Append("sealed ");
		if (Keywords.HasFlag(Modifiers.Override)) builder.Append("override ");
		if (Keywords.HasFlag(Modifiers.Extern))   builder.Append("extern ");
		if (Keywords.HasFlag(Modifiers.Async))    builder.Append("async ");
		if (Keywords.HasFlag(Modifiers.Operator)) builder.Append("operator ");

		return builder.ToString().WithColor(Color.Keyword);
	}

	[Flags]
	public enum Modifiers
	{
		None,
		Static   = 1 << 0,
		Readonly = 1 << 1,
		Abstract = 1 << 2,
		Virtual  = 1 << 3,
		Override = 1 << 4,
		Sealed   = 1 << 5,
		Extern   = 1 << 6,
		Async    = 1 << 7,
		Generic  = 1 << 8,
		Operator = 1 << 9,
	}
}
