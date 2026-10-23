using System.Text.Json;
using FluentValidation.Results;

namespace SportsBooking.Application.Common;

public static class ValidationResultExtensions
{
    /// <summary>Per-field messages keyed by the JSON (camelCase) property name, as the frontend maps them to fields.</summary>
    public static IReadOnlyDictionary<string, string[]> ToFieldErrors(this ValidationResult result) =>
        result.Errors
            .GroupBy(failure => JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());
}
