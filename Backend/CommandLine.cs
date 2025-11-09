using System.Collections.Frozen;
using System.Runtime.InteropServices;
using static MetaDump.Backend.Arguments;

namespace MetaDump.Backend;

internal static class CommandLine
{
	public static OutputMode OutputMode = OutputMode.PlainText;
	public static Arguments Arguments;

	public static string Assembly = "";
	public static string? OutputPath;

	public static ExitCode ExitCode = ExitCode.Success;
	public static bool IsError = false;

	public static void PrintHelp()
	{
		if (!IsError) Console.Out.WriteLine($"Inspects and displays the metadata of a given assembly.\n");

		Console.Out.WriteLine("""
			Usage: metadump <assembly> [options]

			Options:
			   -h, --help           Show this help message and exit.
			   -a, --alltypes       Show all types, including non-public ones.
			   -i, --imports        Show loaded dependencies.
			   -p, --paths          Show dependency paths (implies --imports).
			   -m, --no-color       Disable colorization of console output.
			   -f, --format <fmt>   Specify output format: text, json, html.
			   -o, --output [path]  Write output to a file (defaults to "output.<fmt>").
			   -q, --quiet          Suppress console output (file output only).
			""");
	}

	public static bool Parse(ReadOnlySpan<string> args)
	{
		if (args.Length == 0)
		{
			IsError = true;
			Console.Error.WriteLine("No arguments provided. Use --help for usage information.");
			ExitCode = ExitCode.InvalidArguments;
			return false;
		}

		bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];

			if (arg.StartsWith('-') || (isWindows && arg.StartsWith('/')))
			{
				string normalized = arg.StartsWith('/') ? '-' + arg[1..] : arg;

				if (ArgumentMap.TryGetValue(normalized, out Arguments flag))
				{
					Arguments |= flag;
				}
				else if (normalized is "-o" or "--output")
				{
					Arguments |= PipeFile;

					// Optional file path
					if (i + 1 < args.Length && !args[i + 1].StartsWith('-') && !(isWindows && args[i + 1].StartsWith('/'))) OutputPath = Path.GetFullPath(args[++i]);
				}
				else if (normalized is "-f" or "--format")
				{
					if (i + 1 >= args.Length)
					{
						Console.Error.WriteLine("Error: Missing format after -f / --format.");
						IsError = true;
						ExitCode = ExitCode.InvalidArguments;
						return false;
					}

					string format = args[++i].ToLowerInvariant();
					OutputMode = format switch
					{
						"text" or "txt" or "plain" or "plaintext" => OutputMode.PlainText,
						"json" => OutputMode.Json,
						"html" or "htm" => OutputMode.Html,
						_ => throw new ArgumentException($"Unknown format: {format}. Expected text, json, or html.")
					};
				}
				else
				{
					Console.Error.WriteLine($"Error: Unknown option '{arg}'.\nUse --help for usage information.");
					IsError = true;
					ExitCode = ExitCode.InvalidArguments;
					return false;
				}
			}
			else
			{
				if (!string.IsNullOrEmpty(Assembly))
				{
					Console.Error.WriteLine("Error: Multiple assemblies specified. Only one input is allowed.");
					IsError = true;
					ExitCode = ExitCode.InvalidArguments;
					return false;
				}

				Assembly = Path.GetFullPath(arg);

				if (!File.Exists(Assembly))
				{
					Console.Error.WriteLine($"Error: File not found: {arg}");
					IsError = true;
					ExitCode = ExitCode.GeneralError;
					return false;
				}
			}
		}

		// -p implies -i
		if (Arguments.HasFlag(Paths)) Arguments |= Imports;

		// If -o used but no path, default to "output.<ext>"
		if (Arguments.HasFlag(PipeFile) && string.IsNullOrWhiteSpace(OutputPath))
		{
			string ext = OutputMode switch
			{
				OutputMode.Json => "json",
				OutputMode.Html => "html",
				_ => "txt"
			};
			OutputPath = Path.GetFullPath($"output.{ext}");
		}

		// Valid argument check
		if (string.IsNullOrWhiteSpace(Assembly) && !Arguments.HasFlag(Help))
		{
			Console.Error.WriteLine("Error: Missing input assembly.\nUse --help for usage information.");
			IsError = true;
			ExitCode = ExitCode.InvalidArguments;
			return false;
		}

		return true;
	}

	private static readonly FrozenDictionary<string, Arguments> ArgumentMap = new Dictionary<string, Arguments>(StringComparer.OrdinalIgnoreCase)
	{
		["-h"] = Help,       ["--help"]     = Help,
		["-a"] = AllTypes,   ["--alltypes"] = AllTypes,
		["-i"] = Imports,    ["--imports"]  = Imports,
		["-p"] = Paths,      ["--paths"]    = Paths,
		["-m"] = Monotone,   ["--monotone	"] = Monotone,
		["-q"] = Silent,     ["--quiet"]    = Silent,
	}.ToFrozenDictionary();
}

[Flags]
internal enum Arguments : int
{
	    Help = 0b0000_0001,
	AllTypes = 0b0000_0010,
	 Imports = 0b0000_0100,
	   Paths = 0b0000_1000,
	Monotone = 0b0001_0000,
	PipeFile = 0b0010_0000,
	  Silent = 0b0100_0000,
}

internal enum OutputMode
{
	PlainText = 0,
	Json = 1,
	Html = 2,
}

internal enum ExitCode
{
	Success = 0,
	GeneralError = 1,
	InvalidArguments = 2
}
