using Analyzer;

CommandLine.IsError = !CommandLine.Parse(args);

if (CommandLine.IsError || CommandLine.Arguments.HasFlag(Arguments.Help))
{
	CommandLine.PrintHelp();
	return;
}

if (!Dependencies.GetResolvedTypes(CommandLine.Assembly, out int dependenciesLoaded, out Type[]? types)) return;

if (CommandLine.Arguments.HasFlag(Arguments.Imports)) Console.WriteLine($"Loaded {dependenciesLoaded} dependencies.\n");

Colors.LoadColors();

// TODO: Add option for file export.

Console.WriteLine(Formatter.CreateTree(types));