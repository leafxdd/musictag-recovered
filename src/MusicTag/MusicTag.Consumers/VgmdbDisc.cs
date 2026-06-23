using System.Collections.Generic;

namespace MusicTag.Consumers;

internal class VgmdbDisc
{
	public string Duration { get; set; }

	public string Name { get; set; }

	private readonly List<Dictionary<string, string>> tracks = new List<Dictionary<string, string>>();

	public List<Dictionary<string, string>> Tracks => tracks;
}
