using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Analyzer;

internal static class Dependencies
{
	public static bool TryLoadDependencies(string assemblyPath, out int dependenciesLoaded, [NotNullWhen(true)] out Assembly? mainAssembly)
	{
		// Yes, this method throws a fucking *ton* of exceptions. But there is no way to avoid this, it is pure ass.
		// To fix it, the 'fallback' loading would have to take place before runtime loading.
		// This can be implemented, but frankly, is a pain. Just don't run this with a debugger attached.

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
		if (referenced is not { Length: > 0 })
			return true;

		string rootPath = Path.GetDirectoryName(assemblyPath)!;

		foreach (AssemblyName name in referenced)
		{
			if (TryLoadDependency(name, rootPath, out string? path, out string? message))
			{
				dependenciesLoaded++;

				if (CommandLine.Arguments.HasFlag(Arguments.Imports))
				{
					Console.Write($"Loaded dependency '{name.Name} ({name.Version})'");

					if (CommandLine.Arguments.HasFlag(Arguments.Paths))
					{
						bool isShared = path.StartsWith(
							Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared"),
							StringComparison.OrdinalIgnoreCase);

						Console.WriteLine($" [{(isShared ? "shared" : "local")}]: \"{path}\"");
					}
					else
					{
						Console.WriteLine(".");
					}
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

		try
		{
			assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(name);
		}
		catch (FileNotFoundException)
		{
			string fallbackPath = Path.Combine(rootPath, $"{name.Name}.dll");

			if (!File.Exists(fallbackPath))
			{
				message = "File does not exist.";
				return false;
			}

			try
			{
				assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fallbackPath);
			}
			catch (Exception ex)
			{
				message = ex.Message;
				return false;
			}
		}

		resolvedPath = assembly.Location;
		return true;
	}

	public static bool TryGetAssemblyTypes(Assembly assembly, [NotNullWhen(true)] out Type[]? types)
	{
		types = null;

		try
		{
			types = CommandLine.Arguments.HasFlag(Arguments.AllTypes)
				? assembly.GetTypes()
				: assembly.GetExportedTypes();

			return true;
		}
		catch (ReflectionTypeLoadException rex)
		{
			types = rex.Types.Where(t => t is not null).ToArray()!;
			Console.WriteLine($"Loaded {types.Length} types (some failed): {rex.LoaderExceptions.Length} errors.");
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to get types: {ex.Message}");
			return false;
		}
	}

	public static bool GetResolvedTypes(string path, out int dependenciesLoaded, [NotNullWhen(true)] out Type[]? types)
	{
		if (!TryLoadDependencies(path, out dependenciesLoaded, out Assembly? mainAssembly))
		{
			types = null;
			return false;
		}

		return TryGetAssemblyTypes(mainAssembly, out types);
	}
}
