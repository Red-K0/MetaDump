using System.Collections.Frozen;
using static Analyzer.Arguments;
namespace Analyzer;

internal static class CommandLine
{
	private const string Description = "Inspects and displays the metadata of a given assembly.";


	public static bool IsError = false;
	public static Arguments Arguments;
	public static string Assembly = "";

	public static void PrintHelp()
	{
		if (!IsError) Console.WriteLine($"{Description}\n");

		foreach (var item in HelpText) Console.WriteLine(item);
	}

	public static bool Parse(ReadOnlySpan<string> args)
	{
		foreach (string arg in args)
		{
			if (arg.Length > 0 && (arg[0] == '-' || arg[0] == '/'))
			{
				if (ArgumentMap.TryGetValue(arg, out var flag))
				{
					Arguments |= flag;
				}
				else
				{
					Console.WriteLine($"Unknown argument: {arg}");
					return false;
				}
			}
			else
			{
				Assembly = Path.GetFullPath(arg);
					
				if (!File.Exists(Assembly))
				{
					Console.WriteLine($"File not found: {arg}");
					return false;
				}
			}
		}

		// -p implies -i
		if (Arguments.HasFlag(Paths)) Arguments |= Imports;

		return true;
	}

	private static readonly FrozenDictionary<string, Arguments> ArgumentMap = new Dictionary<string, Arguments>(StringComparer.OrdinalIgnoreCase)
	{
		["/?"] = Help,

		["-h"] = Help,     ["/h"] = Help,     ["--help"]     = Help,     ["/help"]     = Help,
		["-a"] = AllTypes, ["/a"] = AllTypes, ["--alltypes"] = AllTypes, ["/alltypes"] = AllTypes,
		["-i"] = Imports,  ["/i"] = Imports,  ["--imports"]  = Imports,  ["/imports"]  = Imports,
		["-p"] = Paths,    ["/p"] = Paths,    ["--paths"]    = Paths,    ["/paths"]    = Paths,
	}
	.ToFrozenDictionary();

	private static readonly string[] HelpText =
	[
		"   -h | /h | --help     | /help       Show this help message and exit.",
		"   -a | /a | --alltypes | /alltypes   Show all types, including non-public ones.",
		"   -i | /i | --imports  | /imports    Show loaded dependencies.",
		"   -p | /p | --paths    | /paths      Show loaded dependency paths. Implies -i.",
	];
}

[Flags]
internal enum Arguments : int
{
	    Help = 0b0000_0001,
	AllTypes = 0b0000_0010,
	 Imports = 0b0000_0100,
	   Paths = 0b0000_1000
}
