﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gibson.Indexing;
using Gibson.Model;
using Gibson.Storage;
using Gibson.Storage.Pathing;
using Gibson.Yaml.Formatting;
using Gibson.Yaml.Indexing;
using Sitecore.StringExtensions;

namespace Gibson.Yaml
{
	public class YamlSerializationStore : IndexedDataStore
	{
		private readonly string _rootPath;
		private readonly IFileSystemPathProvider _pathProvider;
		private readonly YamlSerializationFormatter _formatter;

		public YamlSerializationStore(string rootPath, IFileSystemPathProvider pathProvider, YamlSerializationFormatter formatter)
			: base(new YamlFrontMatterIndexFactory(rootPath, pathProvider))
		{
			_rootPath = rootPath;
			_pathProvider = pathProvider;
			_formatter = formatter;
		}

		public override IEnumerable<string> GetDatabaseNames()
		{
			return _pathProvider.GetAllStoredDatabaseNames(_rootPath);
		}

		public override void Save(ISerializableItem item)
		{
			var path = _pathProvider.GetStoragePath(new IndexEntry().LoadFrom(item), item.DatabaseName, _rootPath);

			Directory.CreateDirectory(Path.GetDirectoryName(path));

			using (var writer = File.OpenWrite(path))
			{
				_formatter.WriteSerializedItem(item, writer);
			}

			GetIndexForDatabase(item.DatabaseName).Update(new IndexEntry().LoadFrom(item));
		}

		public override void CheckConsistency(string database, bool fixErrors, Action<string> logMessageReceiver)
		{
			// TODO: consistency check
			throw new NotImplementedException();
		}

		public override bool Remove(Guid itemId, string database)
		{
			var existingItem = GetById(itemId, database);

			if (existingItem == null) return false;

			var descendants = GetDescendants(itemId, database);

			foreach (var item in descendants.Concat(new[] { existingItem }))
			{
				var path = _pathProvider.GetStoragePath(new IndexEntry().LoadFrom(item), database, _rootPath);

				if (path == null || !File.Exists(path)) return false;
				if (!GetIndexForDatabase(database).Remove(item.Id)) return false;

				File.Delete(path);
			}

			return true;
		}

		protected override ISerializableItem Load(IndexEntry indexData, string database, bool assertExists)
		{
			var path = _pathProvider.GetStoragePath(indexData, database, _rootPath);

			if (path == null || !File.Exists(path))
			{
				if (!assertExists) return null;

				throw new DataConsistencyException("The item {0} was present in the index but no file existed for it on disk. This indicates corruption in the index or data store. Run fsck.".FormatWith(indexData));
			}

			return Load(path, database);
		}

		protected virtual ISerializableItem Load(string path, string database)
		{
			using (var reader = File.OpenRead(path))
			{
				// no need for the index here because front matter formatters will inject it
				var result = _formatter.ReadSerializedItem(reader, path);
				result.DatabaseName = database;

				return result;
			}
		}
	}
}