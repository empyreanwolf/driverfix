using System.IO;
using System.Linq;
using WifiFix.Core;
using Xunit;

public class InfParserTests
{
    static string Fixture() => File.ReadAllText(Path.Combine("fixtures", "intel_ax200.inf"));

    [Fact]
    public void Finds_ids_in_ntamd64_section()
    {
        var ids = InfParser.GetHardwareIds(Fixture());
        Assert.Contains(@"PCI\VEN_8086&DEV_2723&SUBSYS_00848086", ids);
        Assert.DoesNotContain(@"PCI\VEN_8086&DEV_2723", ids);
    }

    [Fact]
    public void Ignores_ids_in_comments_and_non_model_sections()
    {
        var ids = InfParser.GetHardwareIds(Fixture());
        Assert.DoesNotContain(@"PCI\VEN_1111&DEV_2222", ids);
        Assert.DoesNotContain(@"PCI\VEN_9999&DEV_9999", ids);
    }

    [Fact]
    public void Reads_driver_date_from_DriverVer()
    {
        var inf = "[Version]\nSignature=\"$WINDOWS NT$\"\nDriverVer = 10/05/2021,22.80.0.9\n";
        Assert.Equal(new System.DateTime(2021, 10, 5), InfParser.GetDriverDate(inf));
        Assert.Null(InfParser.GetDriverDate("[Version]\nSignature=x\n"));
    }

    [Fact]
    public void Dedupes_and_uppercases()
    {
        var ids = InfParser.GetHardwareIds(Fixture());
        Assert.Single(ids);
        Assert.Equal(ids[0], ids[0].ToUpperInvariant());
    }
}
