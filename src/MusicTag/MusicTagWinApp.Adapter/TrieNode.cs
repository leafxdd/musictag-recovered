using System;
using System.Runtime.CompilerServices;
using MusicTagWinApp.Listeners;

namespace MusicTagWinApp.Adapter;

internal class TrieNode<T> : IComparable<TrieNode<T>>
{
	private const int DirectIndexSize = 65536;

	private const double DirectIndexThreshold = 0.9;

	private TrieNode<T>[] children;

	private char character;

	public TrieNodeKind Kind { get; private set; } = TrieNodeKind.Prefix;

	public T Value { get; private set; }

	public TrieNode()
	{
	}

	private TrieNode(char character)
	{
		this.character = character;
	}

	public TrieNode(char character, int kind, T value)
	{
		this.character = character;
		Kind = (TrieNodeKind)kind;
		Value = value;
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public TrieNode<T> AddChild(TrieNode<T> child)
	{
		if (children == null)
		{
			children = new TrieNode<T>[0];
		}

		int childIndex = FindChildIndex(child.character);
		if (childIndex >= 0)
		{
			TrieNode<T> existingChild = children[childIndex];
			if (existingChild == null)
			{
				children[childIndex] = child;
				return child;
			}

			switch (child.Kind)
			{
			case (TrieNodeKind)byte.MaxValue:
				existingChild.Kind = TrieNodeKind.Prefix;
				break;
			case TrieNodeKind.Word:
				if (existingChild.Kind != TrieNodeKind.Word)
				{
					existingChild.Kind = TrieNodeKind.IntermediateWord;
				}
				existingChild.Value = child.Value;
				break;
			case TrieNodeKind.Prefix:
				if (existingChild.Kind == TrieNodeKind.Word)
				{
					existingChild.Kind = TrieNodeKind.IntermediateWord;
				}
				break;
			}
			return existingChild;
		}

		if (children.Length >= DirectIndexSize * DirectIndexThreshold)
		{
			TrieNode<T>[] directChildren = new TrieNode<T>[DirectIndexSize];
			foreach (TrieNode<T> existingChild in children)
			{
				directChildren[(uint)existingChild.character] = existingChild;
			}
			directChildren[(uint)child.character] = child;
			children = directChildren;
		}
		else
		{
			TrieNode<T>[] sortedChildren = new TrieNode<T>[children.Length + 1];
			int insertIndex = -(childIndex + 1);
			Array.Copy(children, 0, sortedChildren, 0, insertIndex);
			Array.Copy(children, insertIndex, sortedChildren, insertIndex + 1, children.Length - insertIndex);
			sortedChildren[insertIndex] = child;
			children = sortedChildren;
		}

		return child;
	}

	public int FindChildIndex(char childCharacter)
	{
		if (children == null)
		{
			return -1;
		}
		if (children.Length == DirectIndexSize)
		{
			return childCharacter;
		}
		return Array.BinarySearch(children, new TrieNode<T>(childCharacter));
	}

	public void AddWord(string word, T value)
	{
		TrieNode<T> currentNode = this;
		for (int index = 0; index < word.Length; index++)
		{
			TrieNodeKind kind = word.Length == index + 1 ? TrieNodeKind.Word : TrieNodeKind.Prefix;
			currentNode.AddChild(new TrieNode<T>(word[index], (int)kind, value));
			currentNode = currentNode.children[currentNode.FindChildIndex(word[index])];
		}
	}

	public int CompareTo(TrieNode<T> other)
	{
		if (other == null)
		{
			return 1;
		}
		return character.CompareTo(other.character);
	}

	public override bool Equals(object obj)
	{
		return obj is TrieNode<T> other && character == other.character;
	}

	public override int GetHashCode()
	{
		return character;
	}

	public virtual TrieNode<T> GetChild(char childCharacter)
	{
		int childIndex = FindChildIndex(childCharacter);
		if (childIndex < 0)
		{
			return null;
		}
		return children[childIndex];
	}

	public virtual TrieNode<T> GetChild(string word)
	{
		TrieNode<T> currentNode = this;
		for (int index = 0; index < word.Length; index++)
		{
			int childIndex = currentNode.FindChildIndex(word[index]);
			if (childIndex < 0)
			{
				return null;
			}
			currentNode = currentNode.children[childIndex];
			if (currentNode == null)
			{
				return null;
			}
		}
		return currentNode;
	}

	public virtual TrieMatcher<T> GetMatcher(string text)
	{
		return GetMatcher(text.ToCharArray());
	}

	public virtual TrieMatcher<T> GetMatcher(char[] text)
	{
		return new TrieMatcher<T>(this, text);
	}
}

internal enum TrieNodeKind : byte
{
	Prefix = 1,
	IntermediateWord = 2,
	Word = 3
}
