using MetaDump.Backend;
using MetaDump.Data;
using MetaDump.Runtime;

#if DEBUG
CommandLine.IsError = !CommandLine.Parse([]);
#else
CommandLine.IsError = !CommandLine.Parse([args]);
#endif

if (CommandLine.IsError) Environment.Exit((int)CommandLine.ExitCode);

if (CommandLine.Arguments.HasFlag(Arguments.Help))
{
	CommandLine.PrintHelp();
	return;
}

if (!Dependencies.GetResolvedTypes(CommandLine.Assembly, out int dependenciesLoaded, out string? name, out Type[]? types)) return;

if (CommandLine.Arguments.HasFlag(Arguments.Imports)) Console.WriteLine($"\nLoaded {dependenciesLoaded} dependencies with {types.Length} types.\n");

Configuration.LoadColors();

Node node = new([], types);

Console.WriteLine($"Found {node.PopulateTypes()} members.");

node.Print();

#if !DEBUG

Console.WriteLine(Output.Result);

#endif