using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Abstractions.Responses;

namespace Ufo.Database.Repositories;

public class SearchRepository : ISearchRepository
{
    private readonly ILogger<SearchRepository> _logger;
    private readonly string _connectionString;

    public SearchRepository(IOptionsMonitor<DatabaseOptions> databaseOptionsMonitor, ILogger<SearchRepository>? logger)
    {
        _connectionString = databaseOptionsMonitor.CurrentValue.ConnectionString ?? throw new ArgumentNullException(nameof(databaseOptionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest searchRequest, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SearchAsync - Query: {Query}, UserId: {UserId}", searchRequest.Query, userId);

        var response = new SearchResponse();
        if (string.IsNullOrWhiteSpace(searchRequest.Query))
        {
            return response;
        }

        var rawQuery = searchRequest.Query.Trim();        

        try
        {
            await using var connection = new SqliteConnection(_connectionString);

            var tasks = new List<Task>();
            if (searchRequest.IncludeFiles)
            {
                tasks.Add(PerformFileSearch(connection, rawQuery, userId, response));
            }

            if (searchRequest.IncludeFolders)
            {
                tasks.Add(PerformFolderSearch(connection, rawQuery, userId, response));
            }

            await Task.WhenAll(tasks);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", searchRequest.Query);
            throw;
        }
    }

    private async Task PerformFileSearch(SqliteConnection sqLiteConnection, string query, Ulid userId, SearchResponse searchResponse)
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
                var snapEntry = fileEntry.Snapshots.FirstOrDefault(s => s.Id == snapshot.Id);
                if (snapEntry == null && snapshot != null)
                {
                    snapEntry = snapshot;
                    fileEntry.Snapshots.Add(snapEntry);
                }

                // Add label to the snapshot
                if (snapEntry != null && label != null && !snapEntry.Labels.Any(l => l.Id == label.Id))
                {
                    snapEntry.Labels.Add(label);
                }

                return fileEntry;
            },
            new { Query = query, UserId = userId },
            splitOn: "Id,Id");

        searchResponse.Files = fileDictionary.Values.ToList();
    }

    private async Task PerformFolderSearch(SqliteConnection sqLiteConnection, string query, Ulid userId, SearchResponse searchResponse)
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

                var snapEntry = folderEntry.Snapshots.FirstOrDefault(s => s.Id == snapshot.Id);
                if (snapEntry == null && snapshot != null)
                {
                    snapEntry = snapshot;
                    folderEntry.Snapshots.Add(snapEntry);
                }

                if (snapEntry != null && label != null && !snapEntry.Labels.Any(l => l.Id == label.Id))
                {
                    snapEntry.Labels.Add(label);
                }

                return folderEntry;
            },
            new { Query = query, UserId = userId },
            splitOn: "Id,Id");

        searchResponse.Folders = folderDictionary.Values.ToList();
    }       
}
