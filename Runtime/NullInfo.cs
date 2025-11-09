// This code is taken directly from the .NET runtime (In other words, do not touch).

namespace MetaDump.Runtime;

public sealed class NullInfo
{
	internal NullInfo(Type type, NullState readState, NullState writeState, NullInfo? elementType, NullInfo[] typeArguments)
	{
		GenericTypeArguments = typeArguments;
		ElementType = elementType;
		WriteState = writeState;
		ReadState = readState;
		Type = type;
	}

	/// <summary>
	/// The <see cref="System.Type" /> of the member or generic parameter to which this NullabilityInfo belongs
	/// </summary>
	public Type Type { get; }

	/// <summary>
	/// The nullability read state of the member
	/// </summary>
	public NullState ReadState { get; internal set; }

	/// <summary>
	/// The nullability write state of the member
	/// </summary>
	public NullState WriteState { get; internal set; }

	/// <summary>
	/// If the member type is an array, gives the <see cref="NullInfo" /> of the elements of the array, null otherwise
	/// </summary>
	public NullInfo? ElementType { get; }

	/// <summary>
	/// If the member type is a generic type, gives the array of <see cref="NullInfo" /> for each type parameter
	/// </summary>
	public NullInfo[] GenericTypeArguments { get; }
}
