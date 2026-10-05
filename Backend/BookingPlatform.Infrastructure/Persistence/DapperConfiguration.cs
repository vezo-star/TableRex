using Dapper;

namespace BookingPlatform.Infrastructure.Persistence;

public static class DapperConfiguration
{
    private static int _configured;

    /// <summary>snake_case columns map to PascalCase properties; DateOnly/TimeOnly are handled natively by Npgsql.</summary>
    public static void Configure()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
        {
            return;
        }

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
    }

    private sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(System.Data.IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = System.Data.DbType.Date;
            parameter.Value = value;
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateOnly."),
        };
    }

    private sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
    {
        public override void SetValue(System.Data.IDbDataParameter parameter, TimeOnly value)
        {
            parameter.DbType = System.Data.DbType.Time;
            parameter.Value = value;
        }

        public override TimeOnly Parse(object value) => value switch
        {
            TimeOnly time => time,
            TimeSpan span => TimeOnly.FromTimeSpan(span),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to TimeOnly."),
        };
    }
}
