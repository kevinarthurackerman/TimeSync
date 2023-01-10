namespace TimeSync;

public class CsvTimeLog
{
    private readonly Options _options;

    public CsvTimeLog(IOptions<Options> options)
    {
        _options = options.Value;
    }

    public Task<IEnumerable<TimeLogEntry>> GetEntries(DateOnly from, DateOnly to)
    {
        using var reader = new StreamReader(_options.FilePath.AbsolutePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        csv.Read();
        csv.ReadHeader();

        if (csv.HeaderRecord == null
            || csv.HeaderRecord.Length != 5
            || csv.HeaderRecord[0] != "Date"
            || csv.HeaderRecord[1] != "Start"
            || csv.HeaderRecord[2] != "End"
            || csv.HeaderRecord[3] != "Service"
            || csv.HeaderRecord[4] != "Description")
            throw new InvalidOperationException("Unexpected header row. Expected: Date, Start, End, Service, Description");

        var records = csv.GetRecords<CsvTimeLogEntry>()
            .Where(x => x.Date >= from && x.Date <= to)
            .Select(x => new TimeLogEntry
            {
                Date = x.Date,
                Start = x.Start,
                End = x.End,
                Service = x.Service,
                Description = x.Description
            })
            .ToArray()
            .AsEnumerable();

        return Task.FromResult(records);
    }

    private sealed class CsvTimeLogEntry
    {
        public DateOnly Date { get; set; }
        public TimeOnly Start { get; set; }
        public TimeOnly End { get; set; }
        public string Service { get; set; } = null!;
        public string Description { get; set; } = null!;
    }

    public class Options
    {
        public Uri FilePath { get; set; } = null!;
    }
}
