using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;

namespace Ufo.Database.Repositories;

public class SearchRepository : ISearchRepository
{
    private readonly ILogger<SearchRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SearchRepository(IDbConnectionFactory dbConnectionFactory, ILogger<SearchRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(List<FolderEntity>, List<FileEntity>)> SearchAsync(SearchRequest searchRequest, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SearchAsync - Query: {Query}, UserId: {UserId}", searchRequest.Query, userId);

        if (!searchRequest.HasAnyCriteria)
        {
            return ([], []);
        }

        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

        try
        {
            var files = new List<FileEntity>();
            var folders = new List<FolderEntity>();
            if (searchRequest.IncludeFiles)
            {
                files = await PerformFileSearchAsync(sqLiteConnection, searchRequest, userId);
            }

            if (searchRequest.IncludeFolders)
            {
                folders = await PerformFolderSearchAsync(sqLiteConnection, searchRequest, userId);
            }

            return (folders, files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", searchRequest.Query);
            throw;
        }
    }

    /// <summary>
    /// Composes the WHERE clause and parameters for the current filters.
    /// <paramref name="itemAlias"/> is "f" for files, "fo" for folders.
    /// </summary>
    private static (string Conditions, DynamicParameters Parameters) BuildFilter(SearchRequest request, Ulid userId, string itemAlias, bool isFileSearch)
    {
        var conditions = new List<string> { $"{itemAlias}.UserId = @UserId" };
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        if (request.Query.Length > 0)
        {
            conditions.Add($"{itemAlias}.Name LIKE '%' || @Query || '%'");
            parameters.Add("Query", request.Query);
        }

        if (isFileSearch && !string.IsNullOrWhiteSpace(request.Extension))
        {
            conditions.Add("LOWER(f.FileExtension) = LOWER(@Extension)");
            var extension = request.Extension.Trim();
            parameters.Add("Extension", extension.StartsWith('.') ? extension : "." + extension);
        }

        if (request.MinSize.HasValue)
        {
            conditions.Add($"{itemAlias}.Size >= @MinSize");
            parameters.Add("MinSize", request.MinSize.Value);
        }

        if (request.MaxSize.HasValue)
        {
            conditions.Add($"{itemAlias}.Size <= @MaxSize");
            parameters.Add("MaxSize", request.MaxSize.Value);
        }

        // Timestamps are stored as ISO-8601 ("O") text, so range comparisons work lexicographically.
        if (request.DateFrom.HasValue)
        {
            conditions.Add("s.Timestamp >= @DateFrom");
            parameters.Add("DateFrom", request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            conditions.Add("s.Timestamp <= @DateTo");
            parameters.Add("DateTo", request.DateTo.Value);
        }

        // Dapper does not run type handlers on IN-list elements, so bind the
        // Ulids as the 26-char strings they are stored as.
        if (request.SnapshotIds.Count > 0)
        {
            conditions.Add("s.Id IN @SnapshotIds");
            parameters.Add("SnapshotIds", request.SnapshotIds.Select(id => id.ToString()).ToList());
        }

        if (request.LabelIds.Count > 0)
        {
            conditions.Add("lts.LabelId IN @LabelIds");
            parameters.Add("LabelIds", request.LabelIds.Select(id => id.ToString()).ToList());
        }

        return (string.Join(" AND ", conditions), parameters);
    }

    private async Task<List<FileEntity>> PerformFileSearchAsync(SqliteConnection sqLiteConnection, SearchRequest request, Ulid userId)
    {
        var fileDictionary = new Dictionary<Ulid, FileEntity>();
        var (conditions, parameters) = BuildFilter(request, userId, "f", isFileSearch: true);
        var sql = string.Format(SqlScripts.SearchFilesBaseSql, conditions);

        await sqLiteConnection.QueryAsync<FileEntity, SnapshotEntity, LabelEntity, FileEntity>(
            sql,
            (file, snapshot, label) =>
            {
                if (!fileDictionary.TryGetValue(file.Id, out var fileEntry))
                {
                    fileEntry = file;
                    fileDictionary.Add(fileEntry.Id, fileEntry);
                }

                // Find or add snapshot to the file
                var snapshotEntry = fileEntry.Snapshots.FirstOrDefault(s => s.Id == snapshot.Id);
                if (snapshotEntry == null && snapshot != null)
                {
                    snapshotEntry = snapshot;
                    fileEntry.Snapshots.Add(snapshotEntry);
                }

                // Add label to the snapshot
                if (snapshotEntry != null && label != null && !snapshotEntry.Labels.Any(l => l.Id == label.Id))
                {
                    snapshotEntry.Labels.Add(label);
                }

                return fileEntry;
            },
            parameters,
            splitOn: "Id,Id");

        return [.. fileDictionary.Values];
    }

    private async Task<List<FolderEntity>> PerformFolderSearchAsync(SqliteConnection sqLiteConnection, SearchRequest request, Ulid userId)
    {
        var folderDictionary = new Dictionary<Ulid, FolderEntity>();
        var (conditions, parameters) = BuildFilter(request, userId, "fo", isFileSearch: false);
        var sql = string.Format(SqlScripts.SearchFoldersBaseSql, conditions);

        await sqLiteConnection.QueryAsync<FolderEntity, SnapshotEntity, LabelEntity, FolderEntity>(
            sql,
            (folder, snapshot, label) =>
            {
                if (!folderDictionary.TryGetValue(folder.Id, out var folderEntry))
                {
                    folderEntry = folder;
                    folderDictionary.Add(folderEntry.Id, folderEntry);
                }

                var snapshotEntry = folderEntry.Snapshots.FirstOrDefault(s => s.Id == snapshot.Id);
                if (snapshotEntry == null && snapshot != null)
                {
                    snapshotEntry = snapshot;
                    folderEntry.Snapshots.Add(snapshotEntry);
                }

                if (snapshotEntry != null && label != null && !snapshotEntry.Labels.Any(l => l.Id == label.Id))
                {
                    snapshotEntry.Labels.Add(label);
                }

                return folderEntry;
            },
            parameters,
            splitOn: "Id,Id");

        return [.. folderDictionary.Values];
    }
}
