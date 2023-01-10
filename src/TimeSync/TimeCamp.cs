namespace TimeSync;

public class TimeCamp : IDisposable
{
    private readonly TimeCampClient _timeCampClient;
    private readonly Dictionary<string, string> _timeCampTimeEntryTaskNameLookup;
    private readonly Dictionary<string, string> _timeLogEntryServiceLookup;

    public TimeCamp(IOptions<Options> options, HttpClient httpClient, IOptions<JsonSerializerOptions> jsonSerializerOptions)
    {
        _timeCampClient = new TimeCampClient(httpClient, jsonSerializerOptions.Value, options.Value.AuthToken);

        _timeCampTimeEntryTaskNameLookup = options.Value.ServiceMappings
            .ToDictionary(x => x.TimeLogEntryService, x => x.TimeCampTimeEntryTaskName);

        _timeLogEntryServiceLookup = _timeCampTimeEntryTaskNameLookup.ToDictionary(x => x.Value, x => x.Key);
    }

    public async Task<IEnumerable<TimeLogEntry>> GetEntries(DateOnly from, DateOnly to)
    {
        var entries = await _timeCampClient.GetEntries(from, to);

        var result = new List<TimeLogEntry>();
        foreach(var entry in entries)
            result.Add(await Map(entry));

        return result.ToArray();
    }

    public async Task SetEntries(DateOnly from, DateOnly to, IEnumerable<TimeLogEntry> timeEntries)
    {
        if (timeEntries.Any(x => x.Date < from || x.Date > to))
            throw new InvalidOperationException("One or more time entries were outside of the date range.");

        var originalEntries = await _timeCampClient.GetEntries(from, to);

        var currentEntries = new List<TimeCampClient.ApiTimeEntry>();
        foreach (var timeEntry in timeEntries)
            currentEntries.Add(await Map(timeEntry));

        var entries = originalEntries.FullOuterJoin(
            currentEntries.AsEnumerable(),
            x => x,
            x => x,
            (l, r, k) => (Original: l, Current: r),
            default,
            default,
            TimeCampClient.ApiTimeEntryEqualityComparer.Default)
            .ToArray();

        var entriesToAdd = new List<TimeCampClient.ApiTimeEntry>();
        var entriesToRemove = new List<TimeCampClient.ApiTimeEntry>();
        foreach (var (original, current) in entries)
        {
            if (original == null)
            {
                if (current == null)
                {
                    throw new InvalidOperationException("Unexpected pairing of null original and current entry found.");
                }
                else
                {
                    entriesToAdd.Add(current);
                }
            }
            else
            {
                if (current == null)
                {
                    entriesToRemove.Add(original);
                }
                else
                {
                    // do nothing, the entry already exists
                }
            }
        }

        await _timeCampClient.AddEntries(entriesToAdd);
        await _timeCampClient.RemoveEntries(entriesToRemove);
    }

    private async ValueTask<TimeCampClient.ApiTimeEntry> Map(TimeLogEntry timeEntry)
        => new()
        {
            UserId = (await _timeCampClient.GetCurrentUser()).UserId,
            Date = timeEntry.Date,
            Start = timeEntry.Start,
            End = timeEntry.End,
            TaskId = (await GetTaskId(timeEntry)).ToString(),
            Name = timeEntry.Service,
            Description = timeEntry.Description
        };

    private async ValueTask<TimeLogEntry> Map(TimeCampClient.ApiTimeEntry timeCampTimeEntry)
        => new()
        {
            Date = timeCampTimeEntry.Date,
            Start = timeCampTimeEntry.Start,
            End = timeCampTimeEntry.End,
            Description = timeCampTimeEntry.Description,
            Service = await GetServiceName(timeCampTimeEntry)
        };

    private async ValueTask<string> GetServiceName(TimeCampClient.ApiTimeEntry timeCampTimeEntry)
    {
        var taskNames = new List<string>();
        var taskId = int.Parse(timeCampTimeEntry.TaskId);
        while (taskId != 0)
        {
            var task = (await _timeCampClient.GetTasks()).Single(x => x.TaskId == taskId);

            taskNames.Add(task.Name);

            taskId = task.ParentId;
        }

        var taskName = string.Join(" - ", taskNames);

        return _timeLogEntryServiceLookup.TryGetValue(taskName, out var remappedTaskName)
            ? remappedTaskName
            : taskName;
    }

    private async ValueTask<int> GetTaskId(TimeLogEntry timeEntry)
    {
        var description = _timeCampTimeEntryTaskNameLookup.TryGetValue(timeEntry.Service, out var remappedDescription)
            ? remappedDescription
            : timeEntry.Service;

        var taskNames = description
            .Split(" - ")
            .Reverse()
            .ToArray();

        TimeCampClient.ApiTask? task = null;
        foreach(var taskName in taskNames)
        {
            var parentId = task?.TaskId ?? 0;

            task = (await _timeCampClient.GetTasks()).Single(x => x.Name == taskName && x.ParentId == parentId);
        }

        return task!.TaskId;
    }

    public void Dispose() => _timeCampClient.Dispose();

    public class Options
    {
        public Uri BaseAddress { get; set; } = null!;
        public string AuthToken { get; set; } = null!;
        public IEnumerable<ServiceMappingOptions> ServiceMappings { get; set; } = null!;

        public class ServiceMappingOptions
        {
            public string TimeLogEntryService { get; set; } = null!;
            public string TimeCampTimeEntryTaskName { get; set; } = null!;
        }
    }
}

public class TimeCampClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private bool _isInitialized = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private ApiUser? _cachedUser;
    private IEnumerable<ApiTask>? _cachedTasks;

    public TimeCampClient(HttpClient httpClient, JsonSerializerOptions jsonSerializerOptions, string authToken)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://app.timecamp.com/third_party/api/");
        _httpClient.DefaultRequestHeaders.Authorization = new("Bearer", authToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        _jsonSerializerOptions = jsonSerializerOptions;
    }

    public async Task<IEnumerable<ApiTimeEntry>> GetEntries(DateOnly from, DateOnly to)
    {
        var query = new QueryString()
            .Add("from", from.ToString("yyyy-MM-dd"))
            .Add("to", to.ToString("yyyy-MM-dd"));

        return (await _httpClient.GetFromJsonAsync<IEnumerable<ApiTimeEntry>>($"entries{query.Value}", _jsonSerializerOptions))!;
    }

    public Task AddEntry(ApiTimeEntry timeEntry)
        => _httpClient.PostAsJsonAsync($"entries", timeEntry, _jsonSerializerOptions);

    public Task AddEntries(IEnumerable<ApiTimeEntry> timeEntries)
        => Task.WhenAll(timeEntries.Select(AddEntry));

    public Task RemoveEntry(ApiTimeEntry timeEntry)
    {
        var message = new HttpRequestMessage
        {
            Method = HttpMethod.Delete,
            RequestUri = new Uri("entries", UriKind.Relative),
            Content = JsonContent.Create(timeEntry, options: _jsonSerializerOptions)
        };

        return _httpClient.SendAsync(message);
    }

    public Task RemoveEntries(IEnumerable<ApiTimeEntry> timeEntries)
        => Task.WhenAll(timeEntries.Select(RemoveEntry));

    public async Task<ApiUser> GetCurrentUser()
    {
        await EnsureInitialized();
        return _cachedUser!;
    }

    public async Task<IEnumerable<ApiTask>> GetTasks()
    {
        await EnsureInitialized();
        return _cachedTasks!;
    }

    public async ValueTask EnsureInitialized()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();

        try
        {
            if (_isInitialized) return;

            var userTask = _httpClient.GetFromJsonAsync<ApiUser>($"me", _jsonSerializerOptions);
            var tasksTask = _httpClient.GetFromJsonAsync<Dictionary<string, ApiTask>>("tasks", _jsonSerializerOptions);

            await Task.WhenAll(userTask, tasksTask);

            _cachedUser = userTask.Result!;
            _cachedTasks = tasksTask.Result!.Select(y => y.Value).ToArray();

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose() => _httpClient.Dispose();

    public sealed class ApiTimeEntry
    {
        public int Id { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;

        public DateOnly Date { get; set; }

        [JsonPropertyName("start_time")]
        public TimeOnly Start { get; set; }

        [JsonPropertyName("end_time")]
        public TimeOnly End { get; set; }

        [JsonPropertyName("task_id")]
        public string TaskId { get; set; } = null!;

        public string Name { get; set; } = null!;

        public string Description { get; set; } = null!;
    }

    public sealed class ApiTimeEntryEqualityComparer : IEqualityComparer<ApiTimeEntry>
    {
        public static readonly ApiTimeEntryEqualityComparer Default = new();

        public bool Equals(ApiTimeEntry? x, ApiTimeEntry? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            if (x.Id != y.Id) return false;
            if (x.UserId != y.UserId) return false;
            if (x.Date != y.Date) return false;
            if (x.Start != y.Start) return false;
            if (x.End != y.End) return false;
            if (x.TaskId != y.TaskId) return false;
            if (x.Name != y.Name) return false;
            if (x.Description != y.Description) return false;
            return true;
        }

        public int GetHashCode([DisallowNull] ApiTimeEntry obj)
            => HashCode.Combine(obj.Id, obj.UserId, obj.Date, obj.Start, obj.End, obj.TaskId, obj.Name, obj.Description);
    }

    public sealed class ApiUser
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;
    }

    public sealed class ApiTask
    {
        [JsonPropertyName("task_id")]
        public int TaskId { get; set; }

        [JsonPropertyName("parent_id")]
        public int ParentId { get; set; }

        public string Name { get; set; } = null!;
    }
}
