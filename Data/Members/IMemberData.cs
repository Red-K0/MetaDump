namespace MetaDump.Data.Members;

public interface IMemberData
{
	public DataType DataType { get; }

	public AccessModifier AccessModifier { get; }

	public string BaseName { get; }

	public string GetFormattedName();
}

public enum DataType { Event, Field, Property, Method }