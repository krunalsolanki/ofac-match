namespace OfacMatch.Data;

/// <summary>The kind of party an SDN entry describes.</summary>
public enum EntryType
{
    /// <summary>A company, organization or other entity (blank type in SDN.CSV).</summary>
    Entity,
    /// <summary>A natural person.</summary>
    Individual,
    /// <summary>A vessel.</summary>
    Vessel,
    /// <summary>An aircraft.</summary>
    Aircraft,
}

/// <summary>How a name relates to its entry.</summary>
public enum NameKind
{
    /// <summary>The entry's primary SDN name.</summary>
    Primary,
    /// <summary>Also known as.</summary>
    Aka,
    /// <summary>Formerly known as.</summary>
    Fka,
    /// <summary>Now known as.</summary>
    Nka,
}

/// <summary>A date that may be known only to the year or month, as OFAC often records dates of birth.</summary>
public sealed class PartialDate
{
    /// <summary>Creates a partial date. A day needs a month.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The year, month or day is out of range.</exception>
    /// <exception cref="ArgumentException">A day is given without a month.</exception>
    public PartialDate(int year, int? month = null, int? day = null)
    {
        Guard.InRange(year, 1, 9999, nameof(year));
        if (month is int m)
        {
            Guard.InRange(m, 1, 12, nameof(month));
        }

        if (day is int d)
        {
            if (month is not int monthOfDay)
            {
                throw new ArgumentException("A day needs a month.", nameof(day));
            }

            Guard.InRange(d, 1, DateTime.DaysInMonth(year, monthOfDay), nameof(day));
        }

        Year = year;
        Month = month;
        Day = day;
    }

    /// <summary>The year.</summary>
    public int Year { get; }

    /// <summary>The month (1-12), if known.</summary>
    public int? Month { get; }

    /// <summary>The day of the month, if known.</summary>
    public int? Day { get; }

    /// <inheritdoc />
    public override string ToString() =>
        Day is int d && Month is int m ? $"{Year:0000}-{m:00}-{d:00}"
        : Month is int mo ? $"{Year:0000}-{mo:00}"
        : $"{Year:0000}";
}

/// <summary>An alternate name (aka, fka or nka) from ALT.CSV.</summary>
public sealed class SanctionsAlias
{
    /// <summary>Creates an alias.</summary>
    public SanctionsAlias(NameKind kind, string name, string? remarks = null)
    {
        Kind = kind;
        Name = Guard.NotNull(name, nameof(name));
        Remarks = remarks;
    }

    /// <summary>Whether this is an aka, fka or nka.</summary>
    public NameKind Kind { get; }

    /// <summary>The alternate name as published.</summary>
    public string Name { get; }

    /// <summary>OFAC's remarks on the alias, if any.</summary>
    public string? Remarks { get; }
}

/// <summary>An address from ADD.CSV.</summary>
public sealed class SanctionsAddress
{
    /// <summary>Creates an address.</summary>
    public SanctionsAddress(string? street, string? cityStateZip, string? country)
    {
        Street = street;
        CityStateZip = cityStateZip;
        Country = country;
    }

    /// <summary>Street address.</summary>
    public string? Street { get; }

    /// <summary>City, state or province, and postal code.</summary>
    public string? CityStateZip { get; }

    /// <summary>Country.</summary>
    public string? Country { get; }
}

/// <summary>One entry on the OFAC Specially Designated Nationals (SDN) list.</summary>
public sealed class SanctionsEntry
{
    /// <summary>Creates an entry.</summary>
    public SanctionsEntry(
        int id,
        string name,
        EntryType type,
        IReadOnlyList<string> programs,
        string? title,
        string? remarks)
    {
        Id = id;
        Name = Guard.NotNull(name, nameof(name));
        Type = type;
        Programs = Guard.NotNull(programs, nameof(programs));
        Title = title;
        Remarks = remarks;
        DatesOfBirth = RemarksParser.ParseDatesOfBirth(remarks);
    }

    /// <summary>OFAC's unique entry number (ent_num).</summary>
    public int Id { get; }

    /// <summary>The primary name as published, e.g. "DOE, John".</summary>
    public string Name { get; }

    /// <summary>Individual, entity, vessel or aircraft.</summary>
    public EntryType Type { get; }

    /// <summary>Sanctions program tags, e.g. "SDGT" or "IRAN".</summary>
    public IReadOnlyList<string> Programs { get; }

    /// <summary>Title, for individuals.</summary>
    public string? Title { get; }

    /// <summary>Free-text remarks (dates of birth, identifiers, linked entities).</summary>
    public string? Remarks { get; }

    /// <summary>Dates of birth parsed from <see cref="Remarks"/>.</summary>
    public IReadOnlyList<PartialDate> DatesOfBirth { get; }

    /// <summary>Alternate names.</summary>
    public IList<SanctionsAlias> Aliases { get; } = new List<SanctionsAlias>();

    /// <summary>Known addresses.</summary>
    public IList<SanctionsAddress> Addresses { get; } = new List<SanctionsAddress>();

    /// <summary>Distinct countries from <see cref="Addresses"/>.</summary>
    public IEnumerable<string> Countries =>
        Addresses.Select(a => a.Country).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => $"{Id} {Name} ({Type})";
}
