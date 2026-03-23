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

    public async Task<(List<FsFolderEntity>, List<FsFileEntity>)> SearchAsync(SearchRequest searchRequest, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SearchAsync - Query: {Query}, UserId: {UserId}", searchRequest.Query, userId);

        //var response = new SearchResponse();
        if (string.IsNullOrWhiteSpace(searchRequest.Query))
        {
            return ([], []);
        }

        var rawQuery = searchRequest.Query.Trim();
        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

        try
        {
            var files = new List<FsFileEntity>();
            var folders = new List<FsFolderEntity>();
            if (searchRequest.IncludeFiles)
            {
                files = await PerformFileSearchAsync(sqLiteConnection, rawQuery, userId);
            }

            if (searchRequest.IncludeFolders)
            {
                folders = await PerformFolderSearchAsync(sqLiteConnection, rawQuery, userId);
            }

            return (folders, files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", searchRequest.Query);
            throw;
        }
    }

    private async Task<List<FsFileEntity>> PerformFileSearchAsync(SqliteConnection sqLiteConnection, string query, Ulid userId)
    {
        var fileDictionary = new Dictionary<Ulid, FsFileEntity>();

        await sqLiteConnection.QueryAsync<FsFileEntity, SnapshotEntity, LabelEntity, FsFileEntity>(
            SqlScripts.SearchFilesByNameSql,
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
            new { Query = query, UserId = userId },
            splitOn: "Id,Id");

        return [.. fileDictionary.Values];
    }

    private async Task<List<FsFolderEntity>> PerformFolderSearchAsync(SqliteConnection sqLiteConnection, string query, Ulid userId)
    {
        var folderDictionary = new Dictionary<Ulid, FsFolderEntity>();

        await sqLiteConnection.QueryAsync<FsFolderEntity, SnapshotEntity, LabelEntity, FsFolderEntity>(
            SqlScripts.SearchFoldersByNameSql,
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
            new { Query = query, UserId = userId },
            splitOn: "Id,Id");

        return [.. folderDictionary.Values];
    }       
}
