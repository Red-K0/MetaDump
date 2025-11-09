using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using MetaDump.Backend;

namespace MetaDump.Data.Members;

internal class PropertyData : IMemberData
{
	public string BaseName => Info.Name;

	public PropertyInfo Info { get; }

	public Modifiers Keywords { get; }

	public DataType DataType => DataType.Property;

	public AccessModifier AccessModifier { get; }

	public (MethodData? Get, MethodData? Set) Accessors { get; }

	public TypeData Type { get; }

	public PropertyData(PropertyInfo info)
	{
		Info = info;

		Accessors = DetermineAccessors();

		AccessModifier = DetermineAccessModifer();

		Keywords = DetermineModifiers();

		Type = DeterminePropertyType();
	}

	public (MethodData? Get, MethodData? Set) DetermineAccessors() => (Info.GetMethod == null ? null : new(Info.GetMethod), Info.SetMethod == null ? null : new(Info.SetMethod));

	public AccessModifier DetermineAccessModifer() => (AccessModifier)Math.Min((int)(Accessors.Get?.AccessModifier ?? AccessModifier.Unknown), (int)(Accessors.Set?.AccessModifier ?? AccessModifier.Unknown));

	public Modifiers DetermineModifiers()
	{
		Modifiers modifiers = Modifiers.None;

		if (GetAnyAccessor().Info.IsStatic) modifiers |= Modifiers.Static;

		if (Accessors.Get != null)
		{
			modifiers |= Modifiers.HasGet;

			if (Accessors.Get.AccessModifier != AccessModifier) modifiers |= Modifiers.RestrictedGet;

			if (Accessors.Get.ReturnType.Keywords.HasFlag(TypeData.Modifiers.Ref))
			{
				modifiers |= Modifiers.IsReference;

				if (Accessors.Get.Info.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(InAttribute))) modifiers |= Modifiers.IsRefReadonly;
			}
		}

		if (Accessors.Set != null)
		{
			modifiers |= Modifiers.HasSet;

			if (Accessors.Set.AccessModifier != AccessModifier) modifiers |= Modifiers.RestrictedSet;

			if (Accessors.Set.Info.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit))) modifiers |= Modifiers.IsInit;

			if (Info.IsDefined(typeof(RequiredMemberAttribute), false)) modifiers |= Modifiers.IsRequired;
		}

		if (GetAnyAccessor().Info.IsAbstract) return modifiers | Modifiers.Abstract;

		// If none of the early exits above are taken, we can do virtual checks.

		if (GetAnyAccessor().Info.IsVirtual)
		{
			if (GetAnyAccessor().Info.GetBaseDefinition() != GetAnyAccessor().Info) return modifiers | (Modifiers.Override | (GetAnyAccessor().Info.IsFinal ? Modifiers.Sealed : 0));

			return modifiers | Modifiers.Virtual;
		}

		return modifiers;

		MethodData GetAnyAccessor() => (Accessors.Get ?? Accessors.Set)!;
	}

	public TypeData DeterminePropertyType() => (Accessors.Get?.ReturnType ?? Accessors.Set?.ReturnType)!;

	public string GetFormattedName()
	{
		StringBuilder builder = new(64);

		builder.Append($"{Utilities.GetAccessString(AccessModifier)} {GetKeywordString()}");

		if (Keywords.HasFlag(Modifiers.IsReference))
		{
			builder.Append($"{"ref".WithColor(Color.Keyword)} ");

			if (Keywords.HasFlag(Modifiers.IsRefReadonly)) builder.Append($"{"readonly".WithColor(Color.Keyword)} ");

			builder.Append(Type.NestedData[0].GetFormattedName());
		}
		else
		{
			builder.Append(Type.GetFormattedName());
		}

		builder.Append($" {BaseName.SanitizeInterfaceNames(this)} {{ ");

		if (Keywords.HasFlag(Modifiers.HasGet))
		{
			if (Keywords.HasFlag(Modifiers.RestrictedGet)) builder.Append($"{Utilities.GetAccessString(Accessors.Get!.AccessModifier)} ");

			builder.Append($"{"get".WithColor(Color.Keyword)}; ");
		}

		if (Keywords.HasFlag(Modifiers.HasSet))
		{
			if (Keywords.HasFlag(Modifiers.RestrictedSet)) builder.Append($"{Utilities.GetAccessString(Accessors.Set!.AccessModifier)} ");

			builder.Append($"{(Keywords.HasFlag(Modifiers.IsInit) ? "init" : "set").WithColor(Color.Keyword)}; ");
		}

		builder.Append('}');

		return builder.ToString();
	}

	private string GetKeywordString()
	{
		if (Keywords == Modifiers.None) return "";

		StringBuilder builder = new(32);

		if (Keywords.HasFlag(Modifiers.Static))     builder.Append("static ");
		if (Keywords.HasFlag(Modifiers.Abstract))   builder.Append("abstract ");
		if (Keywords.HasFlag(Modifiers.Virtual))    builder.Append("virtual ");
		if (Keywords.HasFlag(Modifiers.Sealed))     builder.Append("sealed ");
		if (Keywords.HasFlag(Modifiers.Override))   builder.Append("override ");
		if (Keywords.HasFlag(Modifiers.IsRequired)) builder.Append("required ");

		return builder.ToString().WithColor(Color.Keyword);
	}

	[Flags]
	public enum Modifiers
	{
		None,

		Static = 1 << 0,

		RestrictedGet = 1 <<  1,
		HasGet        = 1 <<  2,
		IsReference   = 1 <<  3,
		IsRefReadonly = 1 <<  4,

		RestrictedSet = 1 <<  5,
		HasSet        = 1 <<  6,
		IsInit        = 1 <<  7,
		IsRequired    = 1 <<  8,

		Abstract      = 1 <<  9,
		Virtual       = 1 << 10,
		Override      = 1 << 11,
		Sealed        = 1 << 12,
	}
}
