using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using SportsBooking.Infrastructure.Bookings;

namespace SportsBooking.Infrastructure.Persistence;

/// <summary>
/// Logs every failed database command at Error, as EF Core does, except the one failure that is part of normal
/// operation: the exclusion constraint refusing an overlapping booking (T03), which <c>BookingStore</c> turns into
/// a 409 and logs itself at Information. EF's own <c>CommandError</c> event is lowered to Debug (see
/// <c>AddInfrastructure</c>) and this interceptor puts the Error back for everything else, under the same category and
/// with the same command text, so no other failed command, query or constraint goes quiet. A failure nobody handles
/// is also logged by the global exception handler.
/// </summary>
internal sealed partial class FailedCommandLogger(ILoggerFactory loggerFactory) : DbCommandInterceptor
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(DbLoggerCategory.Database.Command.Name);

    /// <summary>A lost race for a slot: the booking exclusion constraint, and nothing else.</summary>
    public static bool IsExpectedBookingConflict(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: BookingConfiguration.NoOverlapConstraint };

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => LogIfUnexpected(command, eventData);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        LogIfUnexpected(command, eventData);
        return Task.CompletedTask;
    }

    private void LogIfUnexpected(DbCommand command, CommandErrorEventData eventData)
    {
        if (!IsExpectedBookingConflict(eventData.Exception))
        {
            LogFailedCommand(_logger, eventData.Exception, eventData.Duration.TotalMilliseconds, command.CommandText);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed executing DbCommand ({Duration}ms)\n{CommandText}")]
    private static partial void LogFailedCommand(ILogger logger, Exception exception, double duration, string commandText);
}
