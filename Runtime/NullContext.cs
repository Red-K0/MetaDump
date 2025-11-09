// This code is taken directly from the .NET runtime (In other words, do not touch)

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;

namespace MetaDump.Runtime;

/// <summary>
/// A static, concurrent replacement for the runtime's <see cref="System.Reflection.NullabilityInfoContext"/> class.
/// </summary>
public static class NullContext
{
	[Flags]
	private enum NotAnnotatedStatus
	{
		None = 0x0,
		Private = 0x1,
		Internal = 0x2
	}

	private const string CompilerServicesNameSpace = "System.Runtime.CompilerServices";

	private static readonly ConcurrentDictionary<Module, NotAnnotatedStatus> _publicOnlyModules = [];
	private static readonly ConcurrentDictionary<MemberInfo, NullState> _context = [];

	public static NullInfo Create(ParameterInfo parameterInfo)
	{
		ArgumentNullException.ThrowIfNull(parameterInfo);

		IList<CustomAttributeData> attributes = parameterInfo.GetCustomAttributesData();
		NullableAttributeStateParser parser = parameterInfo.Member is MethodBase method && IsPrivateOrInternalMethodAndAnnotationDisabled(method) ? NullableAttributeStateParser.Unknown : CreateParser(attributes);
		NullInfo nullability = GetNullabilityInfo(parameterInfo.Member, parameterInfo.ParameterType, parser);

		if (nullability.ReadState != NullState.Unknown) CheckParameterMetadataType(parameterInfo, nullability);

		CheckNullabilityAttributes(nullability, attributes);
		return nullability;
	}

	public static NullInfo Create(FieldInfo fieldInfo)
	{
		ArgumentNullException.ThrowIfNull(fieldInfo);

		IList<CustomAttributeData> attributes = fieldInfo.GetCustomAttributesData();
		NullableAttributeStateParser parser = IsPrivateOrInternalFieldAndAnnotationDisabled(fieldInfo) ? NullableAttributeStateParser.Unknown : CreateParser(attributes);
		NullInfo nullability = GetNullabilityInfo(fieldInfo, fieldInfo.FieldType, parser);
		CheckNullabilityAttributes(nullability, attributes);
		return nullability;
	}

	public static NullInfo Create(EventInfo eventInfo)
	{
		ArgumentNullException.ThrowIfNull(eventInfo);

		return GetNullabilityInfo(eventInfo, eventInfo.EventHandlerType!, CreateParser(eventInfo.GetCustomAttributesData()));
	}

	#region Internals

	private static NullState? GetNullableContext(MemberInfo? memberInfo)
	{
		while (memberInfo != null)
		{
			if (_context.TryGetValue(memberInfo, out NullState state)) return state;

			foreach (CustomAttributeData attribute in memberInfo.GetCustomAttributesData())
			{
				if (attribute.AttributeType.Name == "NullableContextAttribute" &&
					attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
					attribute.ConstructorArguments.Count == 1)
				{
					state = TranslateByte(attribute.ConstructorArguments[0].Value);

					_context.TryAdd(memberInfo, state);
					return state;
				}
			}

			memberInfo = memberInfo.DeclaringType;
		}

		return null;
	}

	private static ParameterInfo? GetMetaParameter(MethodBase metaMethod, ParameterInfo parameter)
	{
		ReadOnlySpan<ParameterInfo> parameters = metaMethod.GetParameters();

		for (int i = 0; i < parameters.Length; i++) if (parameter.Position == i && parameter.Name == parameters[i].Name) return parameters[i];

		return null;
	}

	private static void CheckParameterMetadataType(ParameterInfo parameter, NullInfo nullability)
	{
		ParameterInfo? metaParameter;
		MemberInfo metaMember;

		switch (parameter.Member)
		{
			case ConstructorInfo ctor:
				ConstructorInfo metaCtor = (ConstructorInfo)GetMemberMetadataDefinition(ctor);
				metaMember = metaCtor;
				metaParameter = GetMetaParameter(metaCtor, parameter);
				break;

			case MethodInfo method:
				MethodInfo metaMethod = GetMethodMetadataDefinition(method);
				metaMember = metaMethod;
				metaParameter = string.IsNullOrEmpty(parameter.Name) ? metaMethod.ReturnParameter : GetMetaParameter(metaMethod, parameter);
				break;

			default: return;
		}

		if (metaParameter != null) CheckGenericParameters(nullability, metaMember, metaParameter.ParameterType, parameter.Member.ReflectedType);
	}

	private static MethodInfo GetMethodMetadataDefinition(MethodInfo method)
	{
		if (method.IsGenericMethod && !method.IsGenericMethodDefinition) method = method.GetGenericMethodDefinition();

		return (MethodInfo)GetMemberMetadataDefinition(method);
	}

	private static void CheckNullabilityAttributes(NullInfo nullability, IList<CustomAttributeData> attributes)
	{
		NullState codeAnalysisReadState = NullState.Unknown, codeAnalysisWriteState = NullState.Unknown;

		foreach (CustomAttributeData attribute in attributes)
		{
			if (attribute.AttributeType.Namespace != "System.Diagnostics.CodeAnalysis") continue;

			if (attribute.AttributeType.Name == "NotNullAttribute")
			{
				codeAnalysisReadState = NullState.NotNull;
			}
			else if ((attribute.AttributeType.Name == "MaybeNullAttribute" || attribute.AttributeType.Name == "MaybeNullWhenAttribute") && codeAnalysisReadState == NullState.Unknown && !IsValueTypeOrValueTypeByRef(nullability.Type))
			{
				codeAnalysisReadState = NullState.Nullable;
			}
			else if (attribute.AttributeType.Name == "DisallowNullAttribute")
			{
				codeAnalysisWriteState = NullState.NotNull;
			}
			else if (attribute.AttributeType.Name == "AllowNullAttribute" && codeAnalysisWriteState == NullState.Unknown && !IsValueTypeOrValueTypeByRef(nullability.Type))
			{
				codeAnalysisWriteState = NullState.Nullable;
			}
		}

		if (codeAnalysisReadState != NullState.Unknown) nullability.ReadState = codeAnalysisReadState;

		if (codeAnalysisWriteState != NullState.Unknown) nullability.WriteState = codeAnalysisWriteState;
	}

	private static bool IsPrivateOrInternalMethodAndAnnotationDisabled(MethodBase method)
	{
		if ((method.IsPrivate || method.IsFamilyAndAssembly || method.IsAssembly) &&
		   IsPublicOnly(method.IsPrivate, method.IsFamilyAndAssembly, method.IsAssembly, method.Module))
		{
			return true;
		}

		return false;
	}

	private static bool IsPrivateOrInternalFieldAndAnnotationDisabled(FieldInfo fieldInfo)
	{
		if ((fieldInfo.IsPrivate || fieldInfo.IsFamilyAndAssembly || fieldInfo.IsAssembly) &&
			IsPublicOnly(fieldInfo.IsPrivate, fieldInfo.IsFamilyAndAssembly, fieldInfo.IsAssembly, fieldInfo.Module))
		{
			return true;
		}

		return false;
	}

	private static bool IsPublicOnly(bool isPrivate, bool isFamilyAndAssembly, bool isAssembly, Module module)
	{
		if (!_publicOnlyModules.TryGetValue(module, out NotAnnotatedStatus value))
		{
			value = PopulateAnnotationInfo(module.GetCustomAttributesData());
			_publicOnlyModules.TryAdd(module, value);
		}

		if (value == NotAnnotatedStatus.None)
		{
			return false;
		}

		if ((isPrivate || isFamilyAndAssembly) && value.HasFlag(NotAnnotatedStatus.Private) ||
			 isAssembly && value.HasFlag(NotAnnotatedStatus.Internal))
		{
			return true;
		}

		return false;
	}

	private static NotAnnotatedStatus PopulateAnnotationInfo(IList<CustomAttributeData> customAttributes)
	{
		foreach (CustomAttributeData attribute in customAttributes)
		{
			if (attribute.AttributeType.Name == "NullablePublicOnlyAttribute" &&
				attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
				attribute.ConstructorArguments.Count == 1)
			{
				if (attribute.ConstructorArguments[0].Value is bool boolValue && boolValue)
				{
					return NotAnnotatedStatus.Internal | NotAnnotatedStatus.Private;
				}
				else
				{
					return NotAnnotatedStatus.Private;
				}
			}
		}

		return NotAnnotatedStatus.None;
	}

	private static NullInfo GetNullabilityInfo(MemberInfo memberInfo, Type type, NullableAttributeStateParser parser)
	{
		int index = 0;
		NullInfo nullability = GetNullabilityInfo(memberInfo, type, parser, ref index);

		if (nullability.ReadState != NullState.Unknown)
		{
			TryLoadGenericMetaTypeNullability(memberInfo, nullability);
		}

		return nullability;
	}

	private static NullInfo GetNullabilityInfo(MemberInfo memberInfo, Type type, NullableAttributeStateParser parser, ref int index)
	{
		NullState state = NullState.Unknown;
		NullInfo[] genericArgumentsState = [];
		NullInfo? elementState = null;
		Type underlyingType = type;

		if (underlyingType.IsByRef || underlyingType.IsPointer)
		{
			underlyingType = underlyingType.GetElementType()!;
		}

		if (underlyingType.IsValueType)
		{
			if (Nullable.GetUnderlyingType(underlyingType) is { } nullableUnderlyingType)
			{
				underlyingType = nullableUnderlyingType;
				state = NullState.Nullable;
			}
			else
			{
				state = NullState.NotNull;
			}

			if (underlyingType.IsGenericType)
			{
				++index;
			}
		}
		else
		{
			if (!parser.ParseNullableState(index++, ref state)
				&& GetNullableContext(memberInfo) is { } contextState)
			{
				state = contextState;
			}

			if (underlyingType.IsArray)
			{
				elementState = GetNullabilityInfo(memberInfo, underlyingType.GetElementType()!, parser, ref index);
			}
		}

		if (underlyingType.IsGenericType)
		{
			Type[] genericArguments = underlyingType.GetGenericArguments();
			genericArgumentsState = new NullInfo[genericArguments.Length];

			for (int i = 0; i < genericArguments.Length; i++)
			{
				genericArgumentsState[i] = GetNullabilityInfo(memberInfo, genericArguments[i], parser, ref index);
			}
		}

		return new NullInfo(type, state, state, elementState, genericArgumentsState);
	}

	private static NullableAttributeStateParser CreateParser(IList<CustomAttributeData> customAttributes)
	{
		foreach (CustomAttributeData attribute in customAttributes)
		{
			if (attribute.AttributeType.Name == "NullableAttribute" &&
				attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
				attribute.ConstructorArguments.Count == 1)
			{
				return new NullableAttributeStateParser(attribute.ConstructorArguments[0].Value);
			}
		}

		return new NullableAttributeStateParser(null);
	}

	private static void TryLoadGenericMetaTypeNullability(MemberInfo memberInfo, NullInfo nullability)
	{
		MemberInfo? metaMember = GetMemberMetadataDefinition(memberInfo);
		Type? metaType = null;
		if (metaMember is FieldInfo field)
		{
			metaType = field.FieldType;
		}
		else if (metaMember is PropertyInfo property)
		{
			metaType = GetPropertyMetaType(property);
		}

		if (metaType != null)
		{
			CheckGenericParameters(nullability, metaMember!, metaType, memberInfo.ReflectedType);
		}
	}

	private static MemberInfo GetMemberMetadataDefinition(MemberInfo member)
	{
		Type? type = member.DeclaringType;
		if ((type != null) && type.IsGenericType && !type.IsGenericTypeDefinition)
		{
			return type.GetGenericTypeDefinition().GetMemberWithSameMetadataDefinitionAs(member);
		}

		return member;
	}

	private static Type GetPropertyMetaType(PropertyInfo property)
	{
		if (property.GetGetMethod(true) is MethodInfo method)
		{
			return method.ReturnType;
		}

		return property.GetSetMethod(true)!.GetParameters()[0].ParameterType;
	}

	private static void CheckGenericParameters(NullInfo nullability, MemberInfo metaMember, Type metaType, Type? reflectedType)
	{
		if (metaType.IsGenericParameter)
		{
			if (nullability.ReadState == NullState.NotNull)
			{
				TryUpdateGenericParameterNullability(nullability, metaType, reflectedType);
			}
		}
		else if (metaType.ContainsGenericParameters)
		{
			if (nullability.GenericTypeArguments.Length > 0)
			{
				Type[] genericArguments = metaType.GetGenericArguments();

				for (int i = 0; i < genericArguments.Length; i++)
				{
					CheckGenericParameters(nullability.GenericTypeArguments[i], metaMember, genericArguments[i], reflectedType);
				}
			}
			else if (nullability.ElementType is { } elementNullability && metaType.IsArray)
			{
				CheckGenericParameters(elementNullability, metaMember, metaType.GetElementType()!, reflectedType);
			}
			// We could also follow this branch for metaType.IsPointer, but since pointers must be unmanaged this
			// will be a no-op regardless
			else if (metaType.IsByRef)
			{
				CheckGenericParameters(nullability, metaMember, metaType.GetElementType()!, reflectedType);
			}
		}
	}

	private static bool TryUpdateGenericParameterNullability(NullInfo nullability, Type genericParameter, Type? reflectedType)
	{
		Debug.Assert(genericParameter.IsGenericParameter);

		if (reflectedType is not null
			&& !genericParameter.IsGenericMethodParameter
			&& TryUpdateGenericTypeParameterNullabilityFromReflectedType(nullability, genericParameter, reflectedType, reflectedType))
		{
			return true;
		}

		if (IsValueTypeOrValueTypeByRef(nullability.Type))
		{
			return true;
		}

		var state = NullState.Unknown;
		if (CreateParser(genericParameter.GetCustomAttributesData()).ParseNullableState(0, ref state))
		{
			nullability.ReadState = state;
			nullability.WriteState = state;
			return true;
		}

		if (GetNullableContext(genericParameter) is { } contextState)
		{
			nullability.ReadState = contextState;
			nullability.WriteState = contextState;
			return true;
		}

		return false;
	}

	private static bool TryUpdateGenericTypeParameterNullabilityFromReflectedType(NullInfo nullability, Type genericParameter, Type context, Type reflectedType)
	{
		Debug.Assert(genericParameter.IsGenericParameter &&
#if NET
			!genericParameter.IsGenericMethodParameter);
#else
				!genericParameter.IsGenericMethodParameter());
#endif

		Type contextTypeDefinition = context.IsGenericType && !context.IsGenericTypeDefinition ? context.GetGenericTypeDefinition() : context;
		if (genericParameter.DeclaringType == contextTypeDefinition)
		{
			return false;
		}

		Type? baseType = contextTypeDefinition.BaseType;
		if (baseType is null)
		{
			return false;
		}

		if (!baseType.IsGenericType
			|| (baseType.IsGenericTypeDefinition ? baseType : baseType.GetGenericTypeDefinition()) != genericParameter.DeclaringType)
		{
			return TryUpdateGenericTypeParameterNullabilityFromReflectedType(nullability, genericParameter, baseType, reflectedType);
		}

		Type[] genericArguments = baseType.GetGenericArguments();
		Type genericArgument = genericArguments[genericParameter.GenericParameterPosition];
		if (genericArgument.IsGenericParameter)
		{
			return TryUpdateGenericParameterNullability(nullability, genericArgument, reflectedType);
		}

		NullableAttributeStateParser parser = CreateParser(contextTypeDefinition.GetCustomAttributesData());
		int nullabilityStateIndex = 1; // start at 1 since index 0 is the type itself
		for (int i = 0; i < genericParameter.GenericParameterPosition; i++)
		{
			nullabilityStateIndex += CountNullabilityStates(genericArguments[i]);
		}
		return TryPopulateNullabilityInfo(nullability, parser, ref nullabilityStateIndex);

		static int CountNullabilityStates(Type type)
		{
			Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
			if (underlyingType.IsGenericType)
			{
				int count = 1;
				foreach (Type genericArgument in underlyingType.GetGenericArguments())
				{
					count += CountNullabilityStates(genericArgument);
				}
				return count;
			}

			if (underlyingType.HasElementType)
			{
				return (underlyingType.IsArray ? 1 : 0) + CountNullabilityStates(underlyingType.GetElementType()!);
			}

			return type.IsValueType ? 0 : 1;
		}
	}

	private static bool TryPopulateNullabilityInfo(NullInfo nullability, NullableAttributeStateParser parser, ref int index)
	{
		bool isValueType = IsValueTypeOrValueTypeByRef(nullability.Type);
		if (!isValueType)
		{
			var state = NullState.Unknown;
			if (!parser.ParseNullableState(index, ref state))
			{
				return false;
			}

			nullability.ReadState = state;
			nullability.WriteState = state;
		}

		if (!isValueType || (Nullable.GetUnderlyingType(nullability.Type) ?? nullability.Type).IsGenericType)
		{
			index++;
		}

		if (nullability.GenericTypeArguments.Length > 0)
		{
			foreach (NullInfo genericTypeArgumentNullability in nullability.GenericTypeArguments)
			{
				TryPopulateNullabilityInfo(genericTypeArgumentNullability, parser, ref index);
			}
		}
		else if (nullability.ElementType is { } elementTypeNullability)
		{
			TryPopulateNullabilityInfo(elementTypeNullability, parser, ref index);
		}

		return true;
	}

	private static NullState TranslateByte(object? value)
	{
		return value is byte b ? TranslateByte(b) : NullState.Unknown;
	}

	private static NullState TranslateByte(byte b) =>
		b switch
		{
			1 => NullState.NotNull,
			2 => NullState.Nullable,
			_ => NullState.Unknown
		};

	private static bool IsValueTypeOrValueTypeByRef(Type type) =>
		type.IsValueType || ((type.IsByRef || type.IsPointer) && type.GetElementType()!.IsValueType);

	private readonly struct NullableAttributeStateParser
	{
		private static readonly object UnknownByte = (byte)0;

		private readonly object? _nullableAttributeArgument;

		public NullableAttributeStateParser(object? nullableAttributeArgument)
		{
			this._nullableAttributeArgument = nullableAttributeArgument;
		}

		public static NullableAttributeStateParser Unknown => new(UnknownByte);

		public bool ParseNullableState(int index, ref NullState state)
		{
			switch (this._nullableAttributeArgument)
			{
				case byte b:
					state = TranslateByte(b);
					return true;
				case ReadOnlyCollection<CustomAttributeTypedArgument> args
					when index < args.Count && args[index].Value is byte elementB:
					state = TranslateByte(elementB);
					return true;
				default:
					return false;
			}
		}
	}

	#endregion
}
