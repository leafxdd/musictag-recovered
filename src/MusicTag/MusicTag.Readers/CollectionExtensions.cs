using System;
using System.Collections.Generic;

namespace MusicTag.Readers;

internal static class CollectionExtensions
{
	public static void ForEachItem<T>(this IEnumerable<T> source, Action<T> action)
	{
		foreach (T item in source)
		{
			action(item);
		}
	}

	public static void ForEachWhile<T>(this IEnumerable<T> source, Func<T, bool> predicate)
	{
		foreach (T item in source)
		{
			if (!predicate(item))
			{
				break;
			}
		}
	}

	public static void AddEntriesFrom<TKey, TValue>(this IDictionary<TKey, TValue> target, IEnumerable<KeyValuePair<TKey, TValue>> source)
	{
		foreach (KeyValuePair<TKey, TValue> entry in source)
		{
			target.Add(entry.Key, entry.Value);
		}
	}
}
