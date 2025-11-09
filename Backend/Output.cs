using System.Text;

namespace MetaDump.Backend;

internal static class Output
{
	internal static readonly StringBuilder Result = new();


	public static void Add(string text)
	{

#if DEBUG
		Console.Write(text);
#endif

		Result.Append(text);
	}

	public static void Add(string text, int layer) => Add(text.WithColor(layer));
}