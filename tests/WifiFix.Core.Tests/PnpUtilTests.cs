using WifiFix.Core;
using Xunit;

class FakeRunner : IProcessRunner
{
    public string? Exe, Args;
    public int Exit;
    public ProcessResult Run(string exe, string args) { Exe = exe; Args = args; return new ProcessResult(Exit, ""); }
}

public class PnpUtilTests
{
    [Fact]
    public void Install_quotes_path_with_spaces_and_unicode()
    {
        var r = new FakeRunner();
        new PnpUtil(r).Install(@"E:\My Drivers\é\a.inf");
        Assert.Equal("pnputil", r.Exe);
        Assert.Equal("/add-driver \"E:\\My Drivers\\é\\a.inf\" /install", r.Args);
    }

    [Fact] public void Exit_0_is_Ok() => Assert.Equal(InstallResult.Ok, new PnpUtil(new FakeRunner { Exit = 0 }).Install("a.inf"));
    [Fact] public void Exit_3010_is_OkRebootNeeded() => Assert.Equal(InstallResult.OkRebootNeeded, new PnpUtil(new FakeRunner { Exit = 3010 }).Install("a.inf"));
    [Fact] public void Exit_1_is_Failed() => Assert.Equal(InstallResult.Failed, new PnpUtil(new FakeRunner { Exit = 1 }).Install("a.inf"));
}
