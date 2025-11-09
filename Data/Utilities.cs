using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using MetaDump.Backend;
using MetaDump.Data.Members;

namespace MetaDump.Data;

public enum AccessModifier { Public, Internal, Protected, ProtectedInternal, PrivateProtected, Private, Unknown }

internal static class Utilities
{
	private static readonly FrozenDictionary<AccessModifier, string> _accessModifierMap = new Dictionary<AccessModifier, string>()
	{
		[AccessModifier.Private]           = "private",
		[AccessModifier.Protected]         = "protected",
		[AccessModifier.Internal]          = "internal",
		[AccessModifier.ProtectedInternal] = "protected internal",
		[AccessModifier.PrivateProtected]  = "private protected",
		[AccessModifier.Public]            = "public",
	}.ToFrozenDictionary();

	private static List<IMemberData> _localMembers = new(64);
	private static HashSet<string> _fieldFilter = [];
	private static HashSet<int> _methodFilter = [];

	public static string GetAccessString(AccessModifier modifier) => _accessModifierMap[modifier].WithColor(Color.Keyword);

	public static IMemberData[] GetMembers(Type type)
	{
		const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		_methodFilter.Clear(); _fieldFilter.Clear(); _localMembers.Clear();

		EventInfo[] e = null!; PropertyInfo[] p = null!; MethodInfo[] m = type.GetMethods(Flags); FieldInfo[] f = type.GetFields(Flags);

		Parallel.Invoke(
			() => m = type.GetMethods(),
			() => f = type.GetFields(),
			() =>
			{
				e = type.GetEvents(Flags);

				foreach (EventInfo e in e)
				{
					if (e.   AddMethod is { } add) _methodFilter.Add(add.MetadataToken);
					if (e.RemoveMethod is { } rem) _methodFilter.Add(rem.MetadataToken);
					if (e. RaiseMethod is { } rai) _methodFilter.Add(rai.MetadataToken);
				}
			},
			() =>
			{
				p = type.GetProperties(Flags);

				foreach (PropertyInfo p in p)
				{
					if (p.GetMethod is { } get) _methodFilter.Add(get.MetadataToken);
					if (p.SetMethod is { } set) _methodFilter.Add(set.MetadataToken);

					_fieldFilter.Add($"<{p.Name}>k__BackingField");
				}
			}
		);

		int eIndex = 0, pIndex = eIndex + e.Length, mIndex = pIndex + p.Length, fIndex = mIndex + m.Length;

		IMemberData[] members = new IMemberData[fIndex + f.Length];

		Memory<IMemberData> memory = members.AsMemory();

		Parallel.Invoke(
			() => { Span<IMemberData> s = memory.Slice(mIndex, m.Length).Span; for (int i = 0; i < m.Length; i++) if (!_methodFilter.Contains(m[i].MetadataToken)) s[i] = new   MethodData(m[i]); },
			() => { Span<IMemberData> s = memory.Slice(fIndex, f.Length).Span; for (int i = 0; i < f.Length; i++) if ( !_fieldFilter.Contains(f[i].Name))          s[i] = new    FieldData(f[i]); },
			() => { Span<IMemberData> s = memory.Slice(eIndex, e.Length).Span; for (int i = 0; i < e.Length; i++)                                                  s[i] = new    EventData(e[i]); },
			() => { Span<IMemberData> s = memory.Slice(pIndex, p.Length).Span; for (int i = 0; i < p.Length; i++)                                                  s[i] = new PropertyData(p[i]); }
		);

		_methodFilter.Clear(); _fieldFilter.Clear();

		Span<IMemberData> span = members; int write = 0;

		for (int read = 0; read < span.Length; read++) if (span[read] is not null) span[write++] = span[read];

		Array.Resize(ref members, write);

		return members;
	}

	public static string GetGenericConstraints(TypeData[] types)
	{
		StringBuilder builder = new(64);

		foreach (TypeData param in types)
		{
			if (!param.Type.IsGenericParameter) continue;

			GenericParameterAttributes attributes = param.Type.GenericParameterAttributes;
			List<string> parts = [];

			if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add("struct".WithColor(Color.Struct));
			if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))        parts.Add("class".WithColor(Color.Class));
			if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint))   parts.Add("new()".WithColor(Color.Keyword));

			parts.AddRange(param.Type.GetGenericParameterConstraints().Select(t => new TypeData(t, null, true).GetFormattedName()));

			if (parts.Count != 0) builder.Append($" {"where".WithColor(Color.Keyword)} {param.GetFormattedName()} : {string.Join(", ", parts)}");
		}

		return builder.ToString();
	}

	public static string SanitizeInterfaceNames(this string str, IMemberData data)
	{
		// This is unpleasant.

		if (str.Count('.') > 1)
		{
			List<string> parts = str.Split('.').ToList();

			if (str.Contains('<'))
			{
				string generic = string.Join('.', parts.Where(p => p.ContainsAny(SearchValues.Create("<>,"))));

				int genericStart = generic.IndexOf('<'), genericEnd = generic.IndexOf('>', genericStart);

				string name = generic[(genericStart + 1)..genericEnd];

				if (!generic.Contains(','))
				{
					generic = FormatInterfaceParameter(generic, name, genericStart, genericEnd);
				}
				else
				{
					string[] parameters = name.Split(',');

					int startIndex = genericStart;

					foreach (string parameter in parameters)
					{
						int endIndex = genericStart + parameter.Length;

						generic = FormatInterfaceParameter(generic, parameter, startIndex, endIndex);

						startIndex = generic.IndexOf(',', endIndex) + 2;
					}
				}

				ReadOnlySpan<char> span = generic.AsSpan();

				generic = string.Concat(span[..genericStart].WithColor(Color.Interface), span[genericStart..]);

				int start = parts.FindIndex(f => f.Contains('<'));

				parts.RemoveRange(start, parts.FindIndex(f => f.Contains('>')) - start + 1);

				parts.Insert(start, generic);
			}
			else
			{
				parts[^2] = parts[^2].WithColor(Color.Interface);
			}

			if (data.DataType == DataType.Method) parts[^1] = parts[^1].WithColor(Color.Method);

			return string.Join(".".WithColor(Color.Operator), parts[^2..]);
		}

		return (data.DataType == DataType.Method) ? str.WithColor(Color.Method) : str;
	}

	private static string FormatInterfaceParameter(ReadOnlySpan<char> fullString, string typeName, int start, int end)
	{
		Type? type = Type.GetType(typeName);

		string parsed = type != null ? new TypeData(type, null).GetFormattedName() : typeName.WithColor(Color.GenericParameter);

		return string.Concat(fullString[..(start + 1)], parsed, fullString[end..]);
	}

	public static string TransformDisplayClass(Type type) => string.Join(", ", GetMembers(type).OfType<FieldData>().Select(f => $"[local] {f.GetLocalName().WithColor(Color.Parameter)}"));
}
