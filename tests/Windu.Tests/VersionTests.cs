using Windu;
using Xunit;

namespace Windu.Tests;

public class VersionTests
{
    [Fact]
    public void Version_comes_from_the_assembly()
    {
        // Release builds are stamped with -p:Version=…, so this must not be a
        // hard-coded constant or the published binary would misreport itself.
        Assert.Matches(@"^\d+\.\d+\.\d+", Program.Version);
    }

    [Fact]
    public void Version_carries_no_build_metadata_suffix()
    {
        // The informational version can read "1.0.0+a1b2c3d"; the suffix is
        // trimmed so it does not leak into the header or --version.
        Assert.DoesNotContain("+", Program.Version);
    }
}
