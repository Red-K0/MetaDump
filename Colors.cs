using System.Runtime.InteropServices;
using System.Text.Json;

namespace Analyzer;

internal static class Colors
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct RGB(string hex)
	{
		public byte R = Convert.ToByte(hex.Substring(1, 2), 16);
		public byte G = Convert.ToByte(hex.Substring(3, 2), 16);
		public byte B = Convert.ToByte(hex.Substring(5, 2), 16);

		public override readonly string ToString() => $"\e[38;2;{R};{G};{B}m";
	}

	public static void LoadColors()
	{
		if (!File.Exists("colors.json"))
		{
			StreamWriter file = File.CreateText("colors.json");

			file.Write("""
			{
			          "StandardColor": "#dcdcdc",
			            "StringColor": "#d69d85",
			            "StructColor": "#86c691",
			             "ClassColor": "#4ec9b0",
			         "InterfaceColor": "#b4d7a3",
			              "EnumColor": "#b8d7a3",
			            "MethodColor": "#dcdcaa",
			          "ModifierColor": "#569cd6",
			         "ParameterColor": "#9cdcfe",
			           "KeywordColor": "#569cd6",
			  "GenericParameterColor": "#b8d7a3",
			    "GenericBracketColor": "#65ade5"
			}	
			""");

			file.Close();
		}

		Dictionary<string, string>? hexColors = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("colors.json")) ?? throw new InvalidOperationException("Failed to load or parse colors.json.");

		foreach ((string key, string hex) in hexColors)
		{
			string ansiCode = new RGB(hex).ToString();

			switch (key)
			{
				case nameof(StandardColor): StandardColor = ansiCode; break;
				case nameof(StringColor): StringColor = ansiCode; break;

				case nameof(StructColor): StructColor = ansiCode; break;
				case nameof(ClassColor): ClassColor = ansiCode; break;
				case nameof(InterfaceColor): InterfaceColor = ansiCode; break;
				case nameof(EnumColor): EnumColor = ansiCode; break;

				case nameof(MethodColor): MethodColor = ansiCode; break;
				case nameof(ModifierColor): ModifierColor = ansiCode; break;
				case nameof(ParameterColor): ParameterColor = ansiCode; break;
				case nameof(KeywordColor): KeywordColor = ansiCode; break;

				case nameof(GenericParameterColor): GenericParameterColor = ansiCode; break;
				case nameof(GenericBracketColor): GenericBracketColor = ansiCode; break;
			}
		}

		Reset = "\e[0m";

		// Don't hardcode this, but whatever.
		LayerColors =
		[
			new RGB("#E06C75").ToString(),
			new RGB("#E5C07B").ToString(),
			new RGB("#98C379").ToString(),
			new RGB("#56B6C2").ToString(),
			new RGB("#C678DD").ToString(),
			new RGB("#D19A66").ToString(),
			new RGB("#61AFEF").ToString(),
			new RGB("#ABB2BF").ToString(),
			new RGB("#89B8C2").ToString()
		];
	}

	public static string
					StandardColor = "",
					  StringColor = "",

					  StructColor = "",
					   ClassColor = "",
				   InterfaceColor = "",
						EnumColor = "",

					  MethodColor = "",
					ModifierColor = "",
				   ParameterColor = "",
					 KeywordColor = "",

			GenericParameterColor = "",
			  GenericBracketColor = "",
							Reset = "";

	public static string[] LayerColors = [];
}
