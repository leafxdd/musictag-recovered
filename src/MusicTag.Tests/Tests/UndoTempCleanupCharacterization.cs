using System;
using System.Collections.Generic;
using System.IO;
using MusicTagWinApp.Listeners;

namespace MusicTag.Tests;

internal static class UndoTempCleanupCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("TryDeleteUndoTempFile: normal file deleted", delegate
		{
			string path = Path.Combine(Path.GetTempPath(), "mtundo_" + Guid.NewGuid().ToString("N"));
			File.WriteAllText(path, "undo");
			Check.True(TagHistoryRepository.TryDeleteUndoTempFile(path), "delete succeeded");
			Check.True(!File.Exists(path), "file removed");
		});

		yield return ("TryDeleteUndoTempFile: read-only failure is contained", delegate
		{
			string path = Path.Combine(Path.GetTempPath(), "mtundo_" + Guid.NewGuid().ToString("N"));
			File.WriteAllText(path, "undo");
			try
			{
				File.SetAttributes(path, FileAttributes.ReadOnly);
				Check.True(!TagHistoryRepository.TryDeleteUndoTempFile(path), "failure returned");
				Check.True(File.Exists(path), "locked state preserved for later retry");
			}
			finally
			{
				File.SetAttributes(path, FileAttributes.Normal);
				File.Delete(path);
			}
		});
	}
}
