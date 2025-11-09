using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MetaDump.Backend;
using MetaDump.Runtime;

namespace MetaDump.Data.Members;

internal sealed class FieldData : IMemberData
{
	public string BaseName => Info.Name;

	public FieldInfo Info { get; }

	public AccessModifier AccessModifier { get; }

	public DataType DataType => DataType.Field;

	public Modifiers Keywords { get; }

	public TypeData ReturnType { get; }

	public FieldData(FieldInfo info)
	{
		Info = info;

		AccessModifier = DetermineAccessModifier();

		Keywords = DetermineModifier();

		ReturnType = new(Info.FieldType, NullContext.Create(Info));
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
		Modifiers modifiers = Modifiers.None;

		if (Info.IsInitOnly) modifiers |= Modifiers.Readonly;

		 if (Info.IsStatic) return modifiers | Modifiers.Static;

		if (Info.GetRequiredCustomModifiers().Contains(typeof(IsVolatile))) modifiers |= Modifiers.Volatile;

		return modifiers;
	}

	public string GetFormattedName() => $"{Utilities.GetAccessString(AccessModifier)} {GetKeywordString()}{ReturnType.GetFormattedName()} {Info.Name.SanitizeInterfaceNames(this)}";

	public string GetLocalName() => $"{GetKeywordString()}{ReturnType.GetFormattedName()} {Info.Name}";

	private string GetKeywordString()
	{
		if (Keywords == 0) return "";

		if (Keywords.HasFlag(Modifiers.Const)) return "const".WithColor(Color.Keyword);

		StringBuilder builder = new(16);

		if (Keywords.HasFlag(Modifiers.Static)) builder.Append("static ");

		     if (Keywords.HasFlag(Modifiers.Readonly)) builder.Append("readonly ");
		else if (Keywords.HasFlag(Modifiers.Volatile)) builder.Append("volatile ");

		return builder.ToString().WithColor(Color.Keyword);
	}

	[Flags]
	public enum Modifiers
	{
		None = 0,

		Static        = 1 << 1,
		Readonly      = 1 << 2,
		Const         = 1 << 3,
		Volatile      = 1 << 4,
	}
}
