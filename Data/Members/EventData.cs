using System.Reflection;
using System.Text;
using MetaDump.Backend;
using MetaDump.Runtime;

namespace MetaDump.Data.Members;

internal sealed class EventData : IMemberData
{
	public string BaseName => Info.Name;

	public EventInfo Info { get; }

	public Modifiers Keywords { get; }

	public DataType DataType => DataType.Event;

	public AccessModifier AccessModifier { get; }

	public TypeData HandlerType { get; }

	public (MethodData? Add, MethodData? Remove) Accessors { get; }

	public EventData(EventInfo info)
	{
		Info = info;

		Accessors = DetermineAccessors();

		AccessModifier = DetermineAccessModifier();

		Keywords = DetermineModifiers();

		HandlerType = new(info.EventHandlerType!, NullContext.Create(Info));
	}

	public (MethodData? Add, MethodData? Remove) DetermineAccessors() => (Info.AddMethod == null ? null : new(Info.AddMethod), Info.RemoveMethod == null ? null : new(Info.RemoveMethod));

	public AccessModifier DetermineAccessModifier() => (AccessModifier)Math.Min((int)(Accessors.Add?.AccessModifier ?? AccessModifier.Unknown), (int)(Accessors.Remove?.AccessModifier ?? AccessModifier.Unknown));

	public Modifiers DetermineModifiers()
	{
		MethodData accessor = GetAnyAccessor();

		Modifiers modifiers = Modifiers.None;

		if (accessor.Info.IsStatic) modifiers |= Modifiers.Static;

		if (accessor.Info.IsAbstract) return modifiers | Modifiers.Abstract;

		if (accessor.Info.IsVirtual)
		{
			if (accessor.Info.GetBaseDefinition() != accessor.Info) return modifiers | (Modifiers.Override | (accessor.Info.IsFinal ? Modifiers.Sealed : 0));

			return modifiers | Modifiers.Virtual;
		}

		return modifiers;

		MethodData GetAnyAccessor() => (Accessors.Add ?? Accessors.Remove)!;
	}

	public string GetFormattedName() => $"{Utilities.GetAccessString(AccessModifier)} {GetKeywordString()}{"event".WithColor(Color.Keyword)} {HandlerType.GetFormattedName()} {BaseName.SanitizeInterfaceNames(this)}";

	private string GetKeywordString()
	{
		if (Keywords == Modifiers.None) return "";

		StringBuilder builder = new(32);

		if (Keywords.HasFlag(Modifiers.Static))   builder.Append("static ");
		if (Keywords.HasFlag(Modifiers.Abstract)) builder.Append("abstract ");
		if (Keywords.HasFlag(Modifiers.Virtual))  builder.Append("virtual ");
		if (Keywords.HasFlag(Modifiers.Override)) builder.Append("override ");
		if (Keywords.HasFlag(Modifiers.Sealed))  builder.Append("sealed ");

		return builder.ToString().WithColor(Color.Keyword);
	}

	[Flags]
	public enum Modifiers
	{
		None,

		Static   = 1 << 0,
		Abstract = 1 << 1,
		Virtual  = 1 << 2,
		Override = 1 << 3,
		Sealed   = 1 << 4,
	}
}
