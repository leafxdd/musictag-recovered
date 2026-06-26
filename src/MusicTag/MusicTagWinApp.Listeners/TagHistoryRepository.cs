using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using MusicTag.Readers;
using MusicTag.States;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Listeners;

internal class TagHistoryRepository : IDisposable
{
	private sealed class TagHistoryChangeDetector
	{
		private readonly ConfigDescriptorState currentTags;

		private readonly ConfigDescriptorState previousTags;

		public TagHistoryChangeDetector(ConfigDescriptorState currentTags, ConfigDescriptorState previousTags)
		{
			this.currentTags = currentTags;
			this.previousTags = previousTags;
		}

		public bool HasChangedValue(string fieldName)
		{
			return currentTags.GetDisplayValue(fieldName).Trim() != previousTags.GetDisplayValue(fieldName).Trim();
		}

		public bool HasNonEmptyValue(string fieldName)
		{
			return !string.IsNullOrWhiteSpace(currentTags.GetDisplayValue(fieldName));
		}
	}

	private static readonly string databasePath = DatabaseMapper.GetApplicationDirectory() + Assembly.GetExecutingAssembly().GetName().Name + ".db";

	private const int CurrentDatabaseVersion = 1;

	private const int MaxHistoryRecordsPerFile = 5;

	public static readonly string[] HistoryTagFields = new string[6] { "title", "artist", "album", "year", "trackstr", "discstr" };

	private static readonly string[] ExtraUndoTagFields = new string[5] { "genre", "albumartist", "composer", "comment", "lyrics" };

	private const long MaxInMemoryUndoPayloadBytes = 104857600L;

	private static readonly object syncRoot = new object();

	private static readonly List<ConfigDescriptorState> undoTags = new List<ConfigDescriptorState>();

	private static SQLiteConnection sharedConnection;

	private static long historySerialPrefix;

	private static long historySerialSequence;

	private readonly SQLiteTransaction currentTransaction;

	private bool transactionFailed;

	private long undoPayloadByteCount;

	private bool syncLockHeld;

	private static readonly List<(string oldPath, string newPath, bool failed)> renameUndoOperations = new List<(string, string, bool)>();

	public TagHistoryRepository(bool useTransaction)
	{
		Monitor.Enter(syncRoot);
		syncLockHeld = true;
		try
		{
			EnsureConnectionOpen();
			if (useTransaction)
			{
				currentTransaction = sharedConnection.BeginTransaction();
			}
		}
		catch
		{
			ReleaseSyncLock();
			throw;
		}
	}

	public void Dispose()
	{
		try
		{
			if (currentTransaction == null)
			{
				return;
			}
			if (transactionFailed)
			{
				RollbackFailedTransaction();
			}
			else
			{
				currentTransaction.Commit();
			}
		}
		catch
		{
			CloseSharedConnectionCore();
			throw;
		}
		finally
		{
			currentTransaction?.Dispose();
			ReleaseSyncLock();
		}
	}

	private void RollbackFailedTransaction()
	{
		try
		{
			currentTransaction.Rollback();
		}
		catch (Exception ex)
		{
			Console.WriteLine("RollbackTagsHistory fail:" + ex.Message);
			CloseSharedConnectionCore();
		}
	}

	private static void EnsureConnectionOpen()
	{
		if (sharedConnection == null)
		{
			if (!File.Exists(databasePath))
			{
				SQLiteConnection.CreateFile(databasePath);
			}
			sharedConnection = new SQLiteConnection("data source = " + databasePath);
			sharedConnection.Open();
		}
	}

	public static void CloseSharedConnection()
	{
		lock (syncRoot)
		{
			CloseSharedConnectionCore();
		}
	}

	private static void CloseSharedConnectionCore()
	{
		if (sharedConnection != null)
		{
			if (sharedConnection.State != ConnectionState.Closed)
			{
				sharedConnection.Close();
			}
		}
		sharedConnection = null;
	}

	private void ReleaseSyncLock()
	{
		if (!syncLockHeld)
		{
			return;
		}
		syncLockHeld = false;
		Monitor.Exit(syncRoot);
	}

	public static string InitializeDatabase()
	{
		try
		{
			using TagHistoryRepository tagHistory = new TagHistoryRepository(useTransaction: true);
			tagHistory.ExecuteNonQuery("create table if not exists tagshistory (\r\n                        serial varchar(21) primary key\r\n                        , filepath varchar(256) not null\r\n                        , createtime datetime default(STRFTIME('%Y-%m-%d %H:%M:%f', 'NOW'))\r\n                        , title varchar(64)\r\n                        , artist varchar(64)\r\n                        , album varchar(64)\r\n                        , year varchar(4)\r\n                        , trackstr varchar(2)\r\n                        , discstr varchar(2));\r\n                        create index if not exists th_idx01 on tagshistory(filepath);\r\n                        create table if not exists config (thserial_prefix integer);");
			using (SQLiteCommand config = new SQLiteCommand())
			{
				using SQLiteDataReader reader = tagHistory.ExecuteReader(config, "select thserial_prefix from config");
				if (reader.Read())
				{
					historySerialPrefix = reader.GetInt64(0) + 1L;
					tagHistory.ExecuteNonQuery("update config set thserial_prefix = ?", historySerialPrefix);
				}
				else
				{
					historySerialPrefix = 1L;
					tagHistory.ExecuteNonQuery("insert into config(thserial_prefix) values(?)", historySerialPrefix);
				}
			}
			if (Settings.Default.DatabaseVersion < CurrentDatabaseVersion)
			{
				Settings.Default.DatabaseVersion = CurrentDatabaseVersion;
				if (!DatabaseMapper.TrySaveApplicationSettings(showErrorMessage: false))
				{
					throw new InvalidOperationException("Failed to save database version setting.");
				}
			}
		}
		catch (Exception ex)
		{
			return ex.GetMessageChain();
		}
		return null;
	}

	private void PrepareCommand(SQLiteCommand command, string commandText, params object[] commandParameterValues)
	{
		command.Parameters.Clear();
		command.Connection = sharedConnection;
		command.Transaction = currentTransaction;
		command.CommandText = commandText;
		command.CommandType = CommandType.Text;
		foreach (object value in commandParameterValues)
		{
			command.Parameters.AddWithValue(string.Empty, value);
		}
	}

	private int ExecuteNonQuery(string commandText, params object[] commandParameterValues)
	{
		try
		{
			using SQLiteCommand sQLiteCommand = new SQLiteCommand();
			PrepareCommand(sQLiteCommand, commandText, commandParameterValues);
			return sQLiteCommand.ExecuteNonQuery();
		}
		catch
		{
			transactionFailed = currentTransaction != null;
			throw;
		}
	}

	private SQLiteDataReader ExecuteReader(SQLiteCommand command, string commandText, params object[] commandParameterValues)
	{
		try
		{
			PrepareCommand(command, commandText, commandParameterValues);
			return command.ExecuteReader();
		}
		catch
		{
			transactionFailed = currentTransaction != null;
			throw;
		}
	}

	private void MarkTransactionFailed()
	{
		transactionFailed = currentTransaction != null;
	}

	private string InsertHistoryRecord(string filePath, ConfigDescriptorState tags)
	{
		string serial = $"{historySerialPrefix}-{++historySerialSequence}";
		IEnumerable<string> columns = new string[2] { "serial", "filepath" }.Concat(HistoryTagFields);
		StringBuilder columnNames = new StringBuilder();
		StringBuilder parameterPlaceholders = new StringBuilder();
		List<object> commandParameterValues = new List<object>();
		foreach (string column in columns)
		{
			columnNames.Append(",").Append(column);
			parameterPlaceholders.Append(",?");
			if (column == "serial")
			{
				commandParameterValues.Add(serial);
			}
			else if (column == "filepath")
			{
				commandParameterValues.Add(filePath);
			}
			else
			{
				commandParameterValues.Add(tags.GetDisplayValue(column));
			}
		}
		columnNames.Remove(0, 1);
		parameterPlaceholders.Remove(0, 1);
		string sql = "insert into tagshistory (" + columnNames.ToString() + ") values (" + parameterPlaceholders.ToString() + ")";
		ExecuteNonQuery(sql, commandParameterValues.ToArray());
		return serial;
	}

	private List<ConfigDescriptorState> LoadHistoryRecords(string filePath, bool newestFirst)
	{
		List<ConfigDescriptorState> historyRecords = new List<ConfigDescriptorState>();
		using SQLiteCommand command = new SQLiteCommand();
		using SQLiteDataReader reader = ExecuteReader(command, "select *, datetime(createtime, 'localtime') ctime from tagshistory where filepath = ? order by createtime " + (newestFirst ? "desc" : ""), filePath);
		while (reader.Read())
		{
			ConfigDescriptorState historyRecord = new ConfigDescriptorState
			{
				["his_serial"] = reader.GetString(reader.GetOrdinal("serial")),
				["his_ctime"] = reader.GetDateTime(reader.GetOrdinal("ctime"))
			};
			foreach (string fieldName in HistoryTagFields)
			{
				historyRecord[fieldName] = reader[fieldName].ToString();
			}
			historyRecords.Add(historyRecord);
		}
		return historyRecords;
	}

	public static List<ConfigDescriptorState> GetHistoryRecords(string filePath, bool newestFirst, TagHistoryRepository tagHistory)
	{
		try
		{
			return tagHistory.LoadHistoryRecords(filePath, newestFirst);
		}
		catch (Exception ex)
		{
			Console.WriteLine("GetTagsHistory fail:" + ex.Message);
		}
		return new List<ConfigDescriptorState>();
	}

	public static int UpdateHistoryFilePath(string oldPath, string newPath, TagHistoryRepository tagHistory)
	{
		try
		{
			return tagHistory.ExecuteNonQuery("update tagshistory set filepath = ? where filepath = ?", newPath, oldPath);
		}
		catch (Exception ex)
		{
			Console.WriteLine("UpdateFilePath fail:" + ex.Message);
		}
		return -1;
	}

	public static int DeleteHistoryByFilePath(string filePath, TagHistoryRepository tagHistory)
	{
		try
		{
			return tagHistory.ExecuteNonQuery("delete from tagshistory where filepath = ?", filePath);
		}
		catch (Exception ex)
		{
			Console.WriteLine("DeleteTagsHistoryByFilePath fail:" + ex.Message);
		}
		return -1;
	}

	public static int DeleteHistoryBySerial(string serial, TagHistoryRepository tagHistory)
	{
		try
		{
			return tagHistory.ExecuteNonQuery("delete from tagshistory where serial = ?", serial);
		}
		catch (Exception ex)
		{
			Console.WriteLine("DeleteTagsHistory fail:" + ex.Message);
		}
		return -1;
	}

	public static int ClearAllHistory()
	{
		return TryClearAllHistory(out string _);
	}

	public static int TryClearAllHistory(out string errorMessage)
	{
		try
		{
			errorMessage = null;
			int result;
			using (TagHistoryRepository tagHistory = new TagHistoryRepository(useTransaction: true))
			{
				result = tagHistory.ExecuteNonQuery("delete from tagshistory;update config set thserial_prefix = 1");
			}
			using (TagHistoryRepository tagHistory = new TagHistoryRepository(useTransaction: false))
			{
				tagHistory.TryVacuum();
			}
			historySerialPrefix = 1L;
			historySerialSequence = 0L;
			CloseSharedConnection();
			return result;
		}
		catch (Exception ex)
		{
			errorMessage = ex.GetMessageChain();
			Console.WriteLine("DeleteAllTagsHistory fail:" + errorMessage);
		}
		return -1;
	}

	private void TryVacuum()
	{
		try
		{
			ExecuteNonQuery("VACUUM");
		}
		catch (Exception ex)
		{
			Console.WriteLine("VacuumTagsHistory fail:" + ex.Message);
		}
	}

	public static ConfigDescriptorState CreateTagSnapshot(ConfigDescriptorState sourceTags, bool includePictures)
	{
		ConfigDescriptorState snapshot = new ConfigDescriptorState();
		CopyEditableTagFields(sourceTags, snapshot);
		if (includePictures)
		{
			List<ConfigDescriptorState.PictureData> pictureData = sourceTags["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
			foreach (ConfigDescriptorState.PictureData picture in pictureData)
			{
				using (ConfigDescriptorState.LoadPictureImage(picture))
				{
				}
			}
			snapshot["allpicturedata"] = pictureData;
		}
		return snapshot;
	}

	public static void CopyEditableTagFields(ConfigDescriptorState sourceTags, ConfigDescriptorState targetTags)
	{
		foreach (string fieldName in HistoryTagFields.Concat(ExtraUndoTagFields))
		{
			targetTags[fieldName] = sourceTags[fieldName];
		}
	}

	public static (string errMsg, string serial) AddHistoryRecordIfChanged(string filePath, ConfigDescriptorState currentTags, ConfigDescriptorState previousTags, TagHistoryRepository tagHistory)
	{
		TagHistoryChangeDetector changeDetector = new TagHistoryChangeDetector(currentTags, previousTags);
		currentTags["filepath"] = filePath;
		try
		{
			if ((previousTags == null || HistoryTagFields.Any(changeDetector.HasChangedValue)) && HistoryTagFields.Any(changeDetector.HasNonEmptyValue))
			{
				List<ConfigDescriptorState> historyRecords = tagHistory.LoadHistoryRecords(filePath, newestFirst: false);
				for (int i = 0; i < historyRecords.Count - MaxHistoryRecordsPerFile + 1; i++)
				{
					ConfigDescriptorState historyRecord = historyRecords[i];
					tagHistory.ExecuteNonQuery("delete from tagshistory where serial = ?", historyRecord["his_serial"] as string);
				}
				return (errMsg: null, serial: tagHistory.InsertHistoryRecord(filePath, currentTags));
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("AddTagsHistory fail:" + ex.Message);
			tagHistory.MarkTransactionFailed();
			return (errMsg: Resources.Msg_AddTagsHistoryFail, serial: null);
		}
		return (errMsg: null, serial: null);
	}

	public static string AddUndoRecord(ConfigDescriptorState tags, string historySerial, TagHistoryRepository undoStore)
	{
		try
		{
			int count = undoTags.Count;
			string lyrics;
			if ((lyrics = tags.GetDisplayValue("lyrics")) != null && !string.IsNullOrWhiteSpace(lyrics))
			{
				if (undoStore.undoPayloadByteCount + lyrics.Length * 2 > MaxInMemoryUndoPayloadBytes)
				{
					string lyricsPath = $"{DatabaseMapper.GetUndoTempDirectory()}{count}.lrc";
					File.WriteAllText(lyricsPath, lyrics);
					tags.RemoveRawValue("lyrics");
					tags["lyrics_path"] = lyricsPath;
				}
				undoStore.undoPayloadByteCount += lyrics.Length * 2;
			}
			if (tags.TryGetRawValue("allpicturedata", out var pictureValue) && pictureValue is List<ConfigDescriptorState.PictureData> pictures)
			{
				long pictureByteCount = 0L;
				foreach (ConfigDescriptorState.PictureData picture in pictures)
				{
					pictureByteCount += picture.ImageBytes.Length;
				}
				if (undoStore.undoPayloadByteCount + pictureByteCount > MaxInMemoryUndoPayloadBytes)
				{
					List<string> picturePaths = new List<string>();
					for (int i = 0; i < pictures.Count; i++)
					{
						ConfigDescriptorState.PictureData picture = pictures[i];
						string picturePath = string.Format("{0}{1}-{2}{3}", DatabaseMapper.GetUndoTempDirectory(), count, i, DatabaseMapper.GetImageExtensionForMimeType(picture.MimeType, ".jpg"));
						File.WriteAllBytes(picturePath, picture.ImageBytes);
						picture.ImageBytes = null;
						picturePaths.Add(picturePath);
					}
					tags["allpicturedata_path"] = picturePaths;
				}
				undoStore.undoPayloadByteCount += pictureByteCount;
			}
			tags["tags_history_serial"] = historySerial;
			undoTags.Add(tags);
		}
		catch (Exception ex)
		{
			Console.WriteLine("AddUndoTags fail:" + ex.Message);
			undoStore.MarkTransactionFailed();
			return Resources.Msg_AddUndoRecordFail;
		}
		return null;
	}

	public static string RestoreUndoPayloads(ConfigDescriptorState tags)
	{
		try
		{
			if (tags.TryGetRawValue("lyrics_path", out var lyricsPathValue))
			{
				if (lyricsPathValue is string lyricsPath)
				{
					tags["lyrics"] = File.ReadAllText(lyricsPath);
				}
			}
			if (tags.TryGetRawValue("allpicturedata_path", out var picturePathsValue) && picturePathsValue is List<string> picturePaths)
			{
				List<ConfigDescriptorState.PictureData> pictures = tags["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
				for (int i = 0; i < pictures.Count; i++)
				{
					pictures[i].ImageBytes = File.ReadAllBytes(picturePaths[i]);
				}
			}
			return null;
		}
		catch (Exception ex)
		{
			Console.WriteLine("RestoreLyricsAndPictureFromPath fail:" + ex.Message);
			return ex.Message;
		}
	}

	public static void ClearUndoState()
	{
		lock (syncRoot)
		{
			bool shouldCollectGarbage = undoTags.Any() || renameUndoOperations.Any();
			undoTags.Clear();
			if (Directory.Exists(DatabaseMapper.GetUndoTempDirectoryPath()))
			{
				Directory.GetFiles(DatabaseMapper.GetUndoTempDirectoryPath()).ForEachItem(DeleteUndoTempFile);
			}
			renameUndoOperations.Clear();
			if (shouldCollectGarbage)
			{
				GC.Collect();
			}
		}
	}

	private static void DeleteUndoTempFile(string filePath)
	{
		File.Delete(filePath);
	}

	public static bool HasPendingUndoActions()
	{
		lock (syncRoot)
		{
			return undoTags.Any() || renameUndoOperations.Any();
		}
	}

	public static int UndoTagsCount()
	{
		lock (syncRoot)
		{
			return undoTags.Count;
		}
	}

	public static int RenameUndoOperationsCount()
	{
		lock (syncRoot)
		{
			return renameUndoOperations.Count;
		}
	}

	public static List<ConfigDescriptorState> GetUndoTagSnapshots()
	{
		lock (syncRoot)
		{
			return new List<ConfigDescriptorState>(undoTags);
		}
	}

	public static List<(string oldPath, string newPath, bool failed)> GetRenameUndoOperations()
	{
		lock (syncRoot)
		{
			return new List<(string oldPath, string newPath, bool failed)>(renameUndoOperations);
		}
	}

	public static string BuildUndoTagsPreview()
	{
		lock (syncRoot)
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (ConfigDescriptorState tags in undoTags.Take(10))
			{
				stringBuilder.Append(Path.GetFileName(tags.GetDisplayValue("filepath")) + "\n");
			}
			if (undoTags.Count > 10)
			{
				stringBuilder.Append("...");
			}
			return stringBuilder.ToString();
		}
	}

	public static void AddRenameUndoRecord(string oldPath, string newPath)
	{
		lock (syncRoot)
		{
			renameUndoOperations.Add((oldPath, newPath, false));
		}
	}

	public static string BuildRenameUndoPreview()
	{
		lock (syncRoot)
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (var renameOperation in renameUndoOperations.Take(10))
			{
				stringBuilder.Append(Path.GetFileName(renameOperation.newPath) + "\n");
			}
			if (renameUndoOperations.Count > 10)
			{
				stringBuilder.Append("...");
			}
			return stringBuilder.ToString();
		}
	}

	public static void RemovePendingUndoForFile(string filePath)
	{
		lock (syncRoot)
		{
			undoTags.RemoveAll(tag => tag["filepath"] as string == filePath);
			renameUndoOperations.RemoveAll(renameOperation => renameOperation.newPath == filePath);
		}
	}

}
