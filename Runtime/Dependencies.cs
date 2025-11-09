using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using MetaDump.Backend;

namespace MetaDump.Runtime;

internal static class Dependencies
{
	public static bool GetResolvedTypes(string path, out int dependenciesLoaded, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out Type[]? types)
	{
		if (TryLoadDependencies(path, out dependenciesLoaded, out Assembly? mainAssembly))
		{
			name = mainAssembly!.GetName().Name!;
			return TryGetAssemblyTypes(mainAssembly, out types);
		}

		types = null;
		name = null;

		return false;
	}

	private static bool TryLoadDependencies(string assemblyPath, out int dependenciesLoaded, [NotNullWhen(true)] out Assembly? mainAssembly)
	{
		// Yes, this method throws a fucking *ton* of exceptions. But there is no way to avoid this.
		// To fix it, the 'fallback' loading would have to take place before runtime loading.

		dependenciesLoaded = 0;
		mainAssembly = null;

		try
		{
			mainAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to load main assembly: {ex.Message}");
			return false;
		}

		AssemblyName[]? referenced = mainAssembly.GetReferencedAssemblies();

		if (referenced is not { Length: > 0 }) return true;

		string rootPath = Path.GetDirectoryName(assemblyPath)!;

		int padding = referenced.Select(a => a.Name!.Length).Max();

		foreach (AssemblyName name in referenced)
		{
			if (TryLoadDependency(name, rootPath, out string? path, out string? message))
			{
				dependenciesLoaded++;

				if (CommandLine.Arguments.HasFlag(Arguments.Imports))
				{
					Console.Write($"Loaded {name.Name!.PadRight(padding)} ({name.Version})");

					if (CommandLine.Arguments.HasFlag(Arguments.Paths))
					{
						bool isShared = path.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared"), StringComparison.OrdinalIgnoreCase);

						Console.Write($" from {(isShared ? "shared" : " local")} path \"{path}\"");
					}

					Console.WriteLine('.');
				}
			}
			else
			{
				Console.WriteLine($"Failed to load dependency: {name.Name}. {message}");
				return false;
			}
		}

		return true;
	}

	private static bool TryLoadDependency(AssemblyName name, string rootPath, [NotNullWhen(true)] out string? resolvedPath, [NotNullWhen(false)] out string? message)
	{
		resolvedPath = null;
		Assembly? assembly;
		message = null;

		string assemblyPath = Path.Combine(rootPath, $"{name.Name}.dll");

		try
		{
			if (File.Exists(assemblyPath))
			{
				assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
			}
			else
			{
				assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(name);
			}
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}


		resolvedPath = assembly.Location;
		return true;
	}

	private static bool TryGetAssemblyTypes(Assembly assembly, [NotNullWhen(true)] out Type[]? types)
	{
		types = null;

		try
		{
			types = CommandLine.Arguments.HasFlag(Arguments.AllTypes) ? assembly.GetTypes() : assembly.GetExportedTypes();

			return true;
		}
		catch (ReflectionTypeLoadException ex)
		{
			types = ex.Types.Where(t => t is not null).ToArray()!;

			Console.WriteLine($"Loaded {types.Length} types (some failed): {ex.LoaderExceptions.Length} errors.");

			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to get types: {ex.Message}");
			return false;
		}
	}
}
