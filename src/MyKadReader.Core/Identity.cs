using System.Text.RegularExpressions;

namespace MyKadReader.Core;

public sealed record Identity(string Number, string? Name, string? BirthDate,
    string? Gender, string? Address, byte[]? Photo)
{
    public static string NormalizeNumber(string value)
    {
        var normalized = value.Replace("-", "").Replace(" ", "").Trim();
        if (!Regex.IsMatch(normalized, "^[0-9]{12}$"))
            throw new InvalidDataException("The card did not return a valid 12-digit MyKad number. Unsupported or malformed card data.");
        return normalized;
    }

    public string Availability => string.Join(", ", new[]
    {
        Name is null ? "Name unavailable" : null,
        BirthDate is null ? "Date of birth unavailable" : null,
        Gender is null ? "Gender unavailable" : null,
        Address is null ? "Address unavailable" : null,
        Photo is null ? "Photo unavailable" : null
    }.Where(x => x is not null));
}

public sealed record SavedIdentity(Identity Identity, string CreatedUtc, string UpdatedUtc);
public sealed record RecordSummary(string Number, string? Name, string UpdatedUtc);
public sealed record RecordPage(IReadOnlyList<RecordSummary> Items, long Total);

public interface IIdentityRepository
{
    void Save(Identity identity);
    RecordPage Search(string query, int page, int pageSize = 25);
    SavedIdentity? Get(string number);
    void Delete(string number);
}

public sealed class UserVisibleException(string message) : Exception(message);
