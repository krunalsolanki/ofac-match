using OfacMatch.Data;

namespace OfacMatch.Tests;

/// <summary>A small synthetic list in OFAC's legacy flat-file format. All names are fictional.</summary>
internal static class TestList
{
    public const string Sdn =
        "100,\"DOE, John Michael\",\"individual\",\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"DOB 15 Mar 1970; POB Exampletown, Freedonia.\"\r\n" +
        "101,\"EXAMPLE TRADING CO., LTD.\",-0- ,\"IRAN] [SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"a.k.a. 'ETC'.\"\r\n" +
        "102,\"MUHAMMAD, Ahmad\",\"individual\",\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"DOB 1965; alt. DOB 1966.\"\r\n" +
        "103,\"SEA STAR\",\"vessel\",\"IRAN\",-0- ,\"9ABC1\",\"Crude Oil Tanker\",-0- ,-0- ,\"Freedonia\",-0- ,-0- \r\n" +
        "104,\"O'NEIL, Seamus\",\"individual\",\"SDNTK\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"DOB Jun 1980.\"\r\n" +
        "\u001a";

    public const string Alt =
        "100,1,\"aka\",\"DOE, Johnny\",-0- \r\n" +
        "101,2,\"fka\",\"EXAMPLE TRADERS LIMITED\",-0- \r\n" +
        "\u001a";

    public const string Add =
        "100,1,-0- ,\"Exampletown\",\"Freedonia\",-0- \r\n" +
        "101,2,\"1 Harbor Road\",\"Port City\",\"Sylvania\",-0- \r\n" +
        "\u001a";

    public static SanctionsList Load() =>
        SanctionsList.Load(new StringReader(Sdn), new StringReader(Alt), new StringReader(Add));
}
