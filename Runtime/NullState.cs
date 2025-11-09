// This code is taken directly from the .NET runtime (In other words, do not touch)

namespace MetaDump.Runtime;

public enum NullState
{
	/// <summary>
	/// Nullability context not enabled (oblivious)
	/// </summary>
	Unknown,
	/// <summary>
	/// Non nullable value or reference type
	/// </summary>
	NotNull,
	/// <summary>
	/// Nullable value or reference type
	/// </summary>
	Nullable
}
