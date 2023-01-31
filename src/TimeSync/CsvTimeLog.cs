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
            || !csv.HeaderRecord.Any(x => x == "Date")
            || !csv.HeaderRecord.Any(x => x == "Start")
            || !csv.HeaderRecord.Any(x => x == "End")
            || !csv.HeaderRecord.Any(x => x == "Service")
            || !csv.HeaderRecord.Any(x => x == "Description"))
            throw new InvalidOperationException("Unexpected header row. Expected: Date, Start, End, Service, Description");

        var records = csv.GetRecords<CsvTimeLogEntry>()
            .Where(x => x.Date >= from && x.Date <= to)
            .Where(x => x.Client == null || x.Client == "CieloWorks")
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
        public DateOnly Date { get; set; } = default;
        public TimeOnly Start { get; set; } = default;
        public TimeOnly End { get; set; } = default;
        public string Client { get; set; } = null;
        public string Service { get; set; } = null!;
        public string Description { get; set; } = null!;
    }

    public class Options
    {
        public Uri FilePath { get; set; } = null!;
    }
}
