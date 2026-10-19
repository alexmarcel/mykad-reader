using Microsoft.Data.Sqlite;

namespace MyKadReader.Core;

public sealed class IdentityRepository(string path) : IIdentityRepository
{
    public string DatabasePath { get; } = Path.GetFullPath(path);

    private SqliteConnection Open(bool write)
    {
        if (write) Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = write ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 2
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(command.ExecuteScalar());
            if (version != 1)
            {
                command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                if (version != 0 || !write || Convert.ToInt32(command.ExecuteScalar()) != 0)
                    throw new UserVisibleException("This database has an unsupported schema. Keep it intact and use a compatible app version.");
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TABLE identities (
                        number TEXT PRIMARY KEY CHECK(length(number)=12 AND number NOT GLOB '*[^0-9]*'),
                        name TEXT, birth_date TEXT, gender TEXT, address TEXT, photo BLOB,
                        availability TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
                    CREATE INDEX identities_name ON identities(name);
                    PRAGMA user_version=1;
                    """;
                command.ExecuteNonQuery();
                transaction.Commit();
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public void Save(Identity identity)
    {
        var number = Identity.NormalizeNumber(identity.Number);
        using var connection = Open(true);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO identities(number,name,birth_date,gender,address,photo,availability,created_utc,updated_utc)
            VALUES($number,$name,$dob,$gender,$address,$photo,$availability,$now,$now)
            ON CONFLICT(number) DO UPDATE SET name=excluded.name,birth_date=excluded.birth_date,
                gender=excluded.gender,address=excluded.address,photo=excluded.photo,
                availability=excluded.availability,updated_utc=excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$number", number);
        command.Parameters.AddWithValue("$name", (object?)identity.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("$dob", (object?)identity.BirthDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$gender", (object?)identity.Gender ?? DBNull.Value);
        command.Parameters.AddWithValue("$address", (object?)identity.Address ?? DBNull.Value);
        command.Parameters.Add("$photo", SqliteType.Blob).Value = (object?)identity.Photo ?? DBNull.Value;
        command.Parameters.AddWithValue("$availability", identity.Availability);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public RecordPage Search(string query, int page, int pageSize = 25)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (!File.Exists(DatabasePath)) return new([], 0);
        using var connection = Open(false);
        using var command = connection.CreateCommand();
        var term = query.Trim();
        command.Parameters.AddWithValue("$q", term);
        command.Parameters.AddWithValue("$n", term.Replace("-", "").Replace(" ", ""));
        const string filter = " WHERE instr(lower(coalesce(name,'')),lower($q))>0 OR instr(number,$n)>0";
        command.CommandText = "SELECT count(*) FROM identities" + filter;
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = "SELECT number,name,updated_utc FROM identities" + filter +
            " ORDER BY updated_utc DESC,number LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (long)page * pageSize);
        using var reader = command.ExecuteReader();
        var rows = new List<RecordSummary>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        return new(rows, total);
    }

    public SavedIdentity? Get(string number)
    {
        if (!File.Exists(DatabasePath)) return null;
        using var connection = Open(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT number,name,birth_date,gender,address,photo,created_utc,updated_utc FROM identities WHERE number=$n";
        command.Parameters.AddWithValue("$n", number);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        string? Field(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
        return new(new(reader.GetString(0), Field(1), Field(2), Field(3), Field(4),
            reader.IsDBNull(5) ? null : (byte[])reader[5]), reader.GetString(6), reader.GetString(7));
    }

    public void Delete(string number)
    {
        if (!File.Exists(DatabasePath)) return;
        using var connection = Open(true);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM identities WHERE number=$n";
        command.Parameters.AddWithValue("$n", number);
        command.ExecuteNonQuery();
    }
}
