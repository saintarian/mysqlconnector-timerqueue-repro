// See https://aka.ms/new-console-template for more information

using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

// Not using cancellation tokens anywhere - docker will force-kill the process after 10s grace period

await InsertRecords();

var readingTask = Task.WhenAll(Enumerable.Range(0, 250).Select(i => ListAllRecordsRepeatedly(i)));
var queueMonitoringTask = MonitorTimerQueue();

await Task.WhenAll(readingTask, queueMonitoringTask, schedulingDelayMonitoringTask);

async Task DeleteExistingRecords()
{
    using (var dbContext = new RecordDbContext())
    {
        // make sure the table exists for Records
        dbContext.Database.EnsureCreated();
        dbContext.Records.RemoveRange(dbContext.Records);
        await dbContext.SaveChangesAsync();
    }
}

async Task ListAllRecordsRepeatedly(int index)
{
    int count = 0;
    while (true)
    {
        try
        {
            count++;
            var records = await ListRecords(1000);
            var lastId = 0;
            foreach (var record in records)
            {
                lastId = record.Id;
            }
            if (count % 10 == 0)
            {
                Console.WriteLine($"{index}: Listed {count} times");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Ignoring exception caught when listing records: {e.Message}");
        }
    }
}

async Task<List<Record>> ListRecords(int count)
{
    using (var dbContext = new RecordDbContext())
    {
        var list = await dbContext.Records.OrderBy(r => r.Id).Take(count).ToListAsync();
        return list;
    }
}

async Task InsertRecords()
{
    using (var dbContext = new RecordDbContext())
    {
        // make sure the table exists for Records
        dbContext.Database.EnsureCreated();
        dbContext.Records.RemoveRange(dbContext.Records);
        dbContext.SaveChanges();
        Console.WriteLine($"Records: {dbContext.Records.Count()}");

        // insert 1000 records
        foreach (var i in Enumerable.Range(0, 1000))
        {
            if (await dbContext.Records.AnyAsync(r => r.Id == i))
            {
                Console.WriteLine($"{i} already exists");
                continue;
            }
            if (i % 100 == 0)
            {
                Console.WriteLine($"Iserted {i} records");
            }

            // Generate a random string that is 128 characters long
            var randomString = new string(Enumerable.Repeat("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 128)
                .Select(s => s[new Random().Next(s.Length)]).ToArray());

            await dbContext.Records.AddAsync(new Record(i, randomString));
            await dbContext.SaveChangesAsync();
        }
        Console.WriteLine($"Records: {dbContext.Records.Count()}");
    }
}

async Task MonitorSchedulingDelay()
{
    while (true)
    {
        var before = Environment.TickCount;
        await Task.Delay(2000);
        var after = Environment.TickCount;
        Console.WriteLine($"Scheduling delay was {after - before - 2000}");
    }
}

async Task MonitorTimerQueue()
{
    try
    {
        var assembly = typeof(MySqlConnector.MySqlBatch).Assembly;
        var timerType = assembly.GetType("MySqlConnector.Utilities.TimerQueue");
        var timerInstanceProperty = timerType!.GetProperty("Instance");
        var timerInstance = timerInstanceProperty!.GetValue(null);
        var listField = timerInstance!.GetType().GetField("m_timeoutActions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var countField = timerInstance!.GetType().GetField("m_counter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (IList)listField.GetValue(timerInstance)!;
        uint count = 0;

        while (true)
        {
            await Task.Delay(1000);
            var newCount = (uint)countField.GetValue(timerInstance)!;
            Console.WriteLine($"Timer queue has a queue depth of {list.Count} with rate {newCount - count} and the counter at {count}",
                list.Count,
                newCount - count,
                count);
            count = newCount;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Caught exception in TimerQueueMonitor: {e}");
    }
}



public class RecordDbContext : DbContext
{
    public DbSet<Record> Records { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var server = Environment.GetEnvironmentVariable("DB_SERVER") ?? "localhost";
        var connectionString = $"server={server};port=3306;database=mysqltest;uid=root;pwd=pass;connection timeout=60";
        options.UseMySql(
            connectionString,
            ServerVersion.Create(new Version(5, 7), ServerType.MySql),
            options => options.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), new List<int>()));
    }
}

public class Record
{
    [Key]
    public int Id { get; init; }

    [MaxLength(128)]
    public string Value { get; init; }

    public Record(int id, string value)
    {
        Id = id;
        Value = value;
    }
}