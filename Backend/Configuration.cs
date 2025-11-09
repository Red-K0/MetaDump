using System.Text.Json;
using MetaDump.Backend;
using static System.Globalization.NumberStyles;

namespace MetaDump.Backend;

internal static class Configuration
{
	private readonly record struct ColorConfig(Dictionary<string, string> NamedColors, string[] LayerColors);
	private readonly record struct RGB(byte R, byte G, byte B)
	{
		public  string ToColor() => CommandLine.OutputMode switch { OutputMode.PlainText => ToAnsi(), OutputMode.Html => ToHtml(), _ => "" };
		private string ToHtml() => $"<span style = color:rgb({R},{G},{B})>";
		private string ToAnsi() => $"\e[38;2;{R};{G};{B}m";

		public static bool TryParse(string? hex, out RGB rgb)
		{
			rgb = new();

			if (string.IsNullOrWhiteSpace(hex)) return false;

			hex = hex.Trim();

			if (hex.Length != 7 || hex[0] != '#') return false;

			static bool Parse(ReadOnlySpan<char> s, int start, out byte value) => byte.TryParse(s[start..(start + 2)], HexNumber, null, out value);

			ReadOnlySpan<char> span = hex.AsSpan();

			if (!Parse(span, 1, out var r) || !Parse(span, 3, out var g) || !Parse(span, 5, out var b)) return false;

			rgb = new RGB(r, g, b);

			return true;
		}
	}

	private readonly static string[] _colorNames = Enum.GetNames<Color>();

	public static string[] NamedColors = (string[])Array.CreateInstance(typeof(string), _colorNames.Length);
	public static string[] LayerColors = [""];
	public static string Reset = "";
	
	extension(ReadOnlySpan<char> text)
	{
		public string WithColor(Color option) => $"{NamedColors[(int)option]}{text}{Reset}";
		public string WithColor(int layer) => $"{LayerColors[layer % LayerColors.Length]}{text}{Reset}";
	}

	/// <summary>
	/// Loads color configuration from a JSON file into immutable RGB structures.
	/// </summary>
	public static bool LoadColors(string path = "colors.json")
	{
		if (CommandLine.OutputMode == OutputMode.Json || CommandLine.Arguments.HasFlag(Arguments.Monotone)) return false;

		if (!File.Exists(path))
		{
			Console.Error.WriteLine($"[Colors] File not found: {path}");
			return false;
		}

		try
		{
			ColorConfig config = JsonSerializer.Deserialize<ColorConfig>(File.ReadAllText(path));

			if (config.NamedColors.Count != _colorNames.Length)
			{
				Console.Error.WriteLine($"[Colors] Some colors weren't specified ({config.NamedColors.Count} found out of {_colorNames.Length}).");
				return false;
			}

			foreach ((string key, string hex) in config.NamedColors)
			{
				if (!_colorNames.Contains(key))
				{
					Console.Error.WriteLine($"[Colors] Missing color entry for '{key}'.");
					return false;
				}

				if (!RGB.TryParse(hex, out RGB rgb))
				{
					Console.Error.WriteLine($"[Colors] Invalid hex for '{key}': {hex}");
					return false;
				}

				if (Enum.TryParse(key, out Color index)) NamedColors[(int)index] = rgb.ToColor();
			}

			LayerColors = new string[config.LayerColors.Length];

			for (int i = 0; i < config.LayerColors.Length; i++)
			{
				if (!RGB.TryParse(config.LayerColors[i], out RGB rgb))
				{
					Console.Error.WriteLine($"[Colors] Invalid layer color at index {i}: {config.LayerColors[i]}");
					return false;
				}

				LayerColors[i] = rgb.ToColor();
			}

			if (LayerColors.Length == 0)
			{
				Console.Error.WriteLine("[Colors] No layer colors specified; defaulting to standard color.");
				LayerColors = [NamedColors[(int)Color.Standard]];
			}

			Reset = CommandLine.OutputMode switch
			{
				OutputMode.PlainText => "\e[0m",
				OutputMode.Html => "</span>",
				_ => ""
			};

			return true;
		}
		catch (JsonException jx) { Console.Error.WriteLine($"[Colors] Invalid JSON format: {jx.Message}"); }
		catch   (IOException io) { Console.Error.WriteLine($"[Colors] Failed to read '{path}': {io.Message}"); }
		catch     (Exception ex) { Console.Error.WriteLine($"[Colors] Unexpected error: {ex.Message}"); }

		return false;
	}
}

public enum Color
{
	Standard,
	String,
	Struct,
	Class,
	Interface,
	Literal,
	Method,
	Parameter,
	Keyword,
	GenericParameter,
	Operator
}
