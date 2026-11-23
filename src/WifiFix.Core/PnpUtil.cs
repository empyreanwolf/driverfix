namespace WifiFix.Core
{
    public enum InstallResult { Ok, OkRebootNeeded, Failed }

    public class PnpUtil
    {
        readonly IProcessRunner _runner;
        public PnpUtil(IProcessRunner runner) { _runner = runner; }

        public InstallResult Install(string infFullPath)
        {
            var r = _runner.Run("pnputil", "/add-driver \"" + infFullPath + "\" /install");
            if (r.ExitCode == 0) return InstallResult.Ok;
            if (r.ExitCode == 3010) return InstallResult.OkRebootNeeded;
            return InstallResult.Failed;
        }
    }
}
