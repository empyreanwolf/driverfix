using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WifiFix.Core;
using Xunit;

class Scanner : IDeviceScanner
{
    public List<DeviceInfo> Devices = new();
    public IReadOnlyList<DeviceInfo> Scan() => Devices;
}

class Verifier : IWifiVerifier
{
    public bool Present, Up;
    public bool AnyWifiAdapterPresent() => Present;
    public bool WifiAdapterUp() => Up;
}

class Online : IOnlineProvider
{
    public bool Internet; public int Calls; public string? InfText;
    public bool HasInternet() => Internet;
    public string? Download(DeviceInfo d, string work)
    {
        Calls++;
        if (InfText == null) return null;
        var dir = Path.Combine(work, "dl"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "net.inf"), InfText);
        return dir;
    }
}

class Runner : IProcessRunner
{
    public List<string> Args = new();
    public Func<string, int> Exit = _ => 0;
    public ProcessResult Run(string exe, string args) { Args.Add(args); return new ProcessResult(Exit(args), ""); }
}

class NullLog : ILog { public List<string> Warns = new(); public void Info(string m) { } public void Warn(string m) => Warns.Add(m); }

public class FixRunnerTests
{
    const string Id = @"PCI\VEN_8086&DEV_2723&SUBSYS_00848086";
    static DeviceInfo Wifi(string id = Id) => new("Network Controller", "", 28, new[] { id }, new string[0]);
    static DeviceInfo Bt() => new("Bluetooth Radio", "", 28, new[] { @"USB\VID_8087&PID_0029" }, new string[0]);
    static string InfFor(string id) => "[Manufacturer]\n%M%=S,NTamd64\n[S.NTamd64]\n%D%=I, " + id + "\n";

    class Rig
    {
        public Scanner Sc = new(); public Verifier V = new(); public Online On = new(); public Runner R = new();
        public NullLog Log = new(); public PackIndex Idx = new(); public string Root;
        public Rig()
        {
            Root = Path.Combine(Path.GetTempPath(), "wfr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        public FixRunner Make() => new(Sc, V, Idx, new PnpUtil(R), On, Root, Log);
    }

    [Fact]
    public void No_missing_devices_and_no_adapter_returns_NoWifiHardware_and_installs_nothing()
    {
        var r = new Rig();
        Assert.Equal(RunOutcome.NoWifiHardware, r.Make().Run());
        Assert.Empty(r.R.Args);
    }

    [Fact]
    public void No_missing_devices_and_adapter_present_returns_AlreadyWorking()
    {
        var r = new Rig { }; r.V.Present = true;
        Assert.Equal(RunOutcome.AlreadyWorking, r.Make().Run());
        Assert.Empty(r.R.Args);
    }

    [Fact]
    public void Pack_match_installs_without_calling_online()
    {
        var r = new Rig(); r.Sc.Devices.Add(Wifi()); r.Idx.Add("intel/a.inf", new[] { Id }); r.V.Up = true;
        Assert.Equal(RunOutcome.Fixed, r.Make().Run());
        Assert.Single(r.R.Args);
        Assert.Contains("intel", r.R.Args[0]);
        Assert.Equal(0, r.On.Calls);
    }

    [Fact]
    public void No_pack_match_downloads_installs_and_saves_back_to_index()
    {
        var r = new Rig(); r.Sc.Devices.Add(Wifi()); r.On.Internet = true; r.On.InfText = InfFor(Id); r.V.Up = true;
        Assert.Equal(RunOutcome.Fixed, r.Make().Run());
        Assert.Equal(1, r.On.Calls);
        Assert.NotNull(r.Idx.FindInf(new[] { Id }));
        Assert.True(File.Exists(Path.Combine(r.Root, "index.json")));
        Assert.True(Directory.GetFiles(Path.Combine(r.Root, "win10", "downloaded"), "*.inf", SearchOption.AllDirectories).Length == 1);
    }

    [Fact]
    public void Install_failed_falls_through_to_online()
    {
        var r = new Rig(); r.Sc.Devices.Add(Wifi()); r.Idx.Add("intel/a.inf", new[] { Id });
        r.On.Internet = true; r.On.InfText = InfFor(Id); r.V.Up = true;
        r.R.Exit = a => a.Contains("intel") ? 1 : 0;
        Assert.Equal(RunOutcome.Fixed, r.Make().Run());
        Assert.Equal(1, r.On.Calls);
    }

    const string AudioId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256";
    static DeviceInfo Audio() => new("Unknown device", "", 28, new[] { AudioId }, new string[0]);

    [Fact]
    public void Audio_is_ignored_unless_included()
    {
        var r = new Rig(); r.Sc.Devices.Add(Audio()); r.Idx.Add("audio/a.inf", new[] { AudioId });
        Assert.Equal(RunOutcome.NoWifiHardware, r.Make().Run());
        Assert.Empty(r.R.Args);
    }

    [Fact]
    public void Audio_only_run_is_Fixed_even_with_no_wifi_adapter()
    {
        var r = new Rig(); r.Sc.Devices.Add(Audio()); r.Idx.Add("audio/a.inf", new[] { AudioId });
        var run = new FixRunner(r.Sc, r.V, r.Idx, new PnpUtil(r.R), r.On, r.Root, r.Log, null, includeAudio: true);
        Assert.Equal(RunOutcome.Fixed, run.Run());
        Assert.Single(r.R.Args);
    }

    [Fact]
    public void Audio_uses_online_fallback_and_saves_back()
    {
        var r = new Rig(); r.Sc.Devices.Add(Audio()); r.On.Internet = true; r.On.InfText = InfFor(AudioId);
        var run = new FixRunner(r.Sc, r.V, r.Idx, new PnpUtil(r.R), r.On, r.Root, r.Log, null, includeAudio: true);
        Assert.Equal(RunOutcome.Fixed, run.Run());
        Assert.Equal(1, r.On.Calls);
        Assert.NotNull(r.Idx.FindInf(new[] { AudioId }));
    }

    [Fact]
    public void Wifi_and_audio_both_handled_when_audio_included()
    {
        var r = new Rig(); r.V.Up = true;
        r.Sc.Devices.AddRange(new[] { Wifi(), Audio() });
        r.Idx.Add("intel/a.inf", new[] { Id }); r.Idx.Add("audio/a.inf", new[] { AudioId });
        var run = new FixRunner(r.Sc, r.V, r.Idx, new PnpUtil(r.R), r.On, r.Root, r.Log, null, includeAudio: true);
        Assert.Equal(RunOutcome.Fixed, run.Run());
        Assert.Equal(2, r.R.Args.Count);
    }

    [Fact]
    public void Audio_failure_makes_run_NotFixed_even_if_wifi_worked()
    {
        var r = new Rig(); r.V.Up = true;
        r.Sc.Devices.AddRange(new[] { Wifi(), Audio() });
        r.Idx.Add("intel/a.inf", new[] { Id }); r.Idx.Add("audio/a.inf", new[] { AudioId });
        r.R.Exit = a => a.Contains("audio") ? 1 : 0;
        var run = new FixRunner(r.Sc, r.V, r.Idx, new PnpUtil(r.R), r.On, r.Root, r.Log, null, includeAudio: true);
        Assert.Equal(RunOutcome.NotFixed, run.Run());
    }

    [Fact]
    public void Tries_next_pack_candidate_when_first_inf_fails()
    {
        var r = new Rig(); r.Sc.Devices.Add(Wifi()); r.V.Up = true;
        r.Idx.Add("bad/a.inf", new[] { Id }, new DateTime(2024, 1, 1));
        r.Idx.Add("good/b.inf", new[] { Id }, new DateTime(2020, 1, 1));
        r.R.Exit = a => a.Contains("bad") ? 1 : 0;
        Assert.Equal(RunOutcome.Fixed, r.Make().Run());
        Assert.Equal(2, r.R.Args.Count);
        Assert.Equal(0, r.On.Calls);
    }

    [Fact]
    public void Logs_the_pc_model_when_given()
    {
        var r = new Rig(); var log = new RecLog();
        var run = new FixRunner(r.Sc, r.V, r.Idx, new PnpUtil(r.R), r.On, r.Root, log, new MachineInfo("Dell Inc.", "Latitude 5420", "S1"));
        run.Run();
        Assert.Contains(log.Lines, l => l.Contains("Dell Inc. Latitude 5420"));
    }

    class RecLog : ILog { public System.Collections.Generic.List<string> Lines = new(); public void Info(string m) => Lines.Add(m); public void Warn(string m) => Lines.Add(m); }

    [Fact]
    public void Savedback_failure_does_not_fail_run()
    {
        var r = new Rig(); r.Sc.Devices.Add(Wifi()); r.On.Internet = true; r.On.InfText = InfFor(Id); r.V.Up = true;
        // Make "drivers/win10" a FILE so creating the downloaded folder fails.
        File.WriteAllText(Path.Combine(r.Root, "win10"), "x");
        Assert.Equal(RunOutcome.Fixed, r.Make().Run());
        Assert.NotEmpty(r.Log.Warns);
    }

    [Fact]
    public void Two_missing_devices_each_handled_and_bluetooth_untouched()
    {
        var r = new Rig(); var id2 = @"PCI\VEN_10EC&DEV_8822";
        r.Sc.Devices.AddRange(new[] { Wifi(), Wifi(id2), Bt() });
        r.Idx.Add("intel/a.inf", new[] { Id }); r.Idx.Add("realtek/b.inf", new[] { id2 }); r.V.Up = true;
        Assert.Equal(RunOutcome.Fixed, r.Make().Run());
        Assert.Equal(2, r.R.Args.Count);
        Assert.DoesNotContain(r.R.Args, a => a.Contains("0029"));
    }

    [Fact]
    public void Reboot_needed_maps_to_FixedRebootNeeded()
    {
        var r = new Rig(); r.Sc.Devices.Add(Wifi()); r.Idx.Add("intel/a.inf", new[] { Id });
        r.R.Exit = _ => 3010;
        Assert.Equal(RunOutcome.FixedRebootNeeded, r.Make().Run());
    }

    [Fact]
    public void No_match_and_no_internet_returns_NotFixed()
    {
        var r = new Rig(); r.Sc.Devices.Add(Wifi());
        Assert.Equal(RunOutcome.NotFixed, r.Make().Run());
        Assert.Empty(r.R.Args);
    }

    const string PadId = @"ACPI\VEN_DLL&DEV_0A1B";
    static DeviceInfo Pad() => new("Unknown device", "", 28, new[] { PadId }, new string[0]);

    [Fact]
    public void Other_devices_ignored_unless_included()
    {
        var r = new Rig(); r.Sc.Devices.Add(Pad()); r.Idx.Add("pad/a.inf", new[] { PadId });
        Assert.Equal(RunOutcome.NoWifiHardware, r.Make().Run());
        Assert.Empty(r.R.Args);
    }

    [Fact]
    public void Touchpad_installed_from_pack_when_other_included()
    {
        var r = new Rig(); r.Sc.Devices.Add(Pad()); r.Idx.Add("pad/a.inf", new[] { PadId });
        var run = new FixRunner(r.Sc, r.V, r.Idx, new PnpUtil(r.R), r.On, r.Root, r.Log, null, includeOther: true);
        Assert.Equal(RunOutcome.Fixed, run.Run());
        Assert.Single(r.R.Args);
    }

    [Fact]
    public void Touchpad_uses_online_and_failure_is_NotFixed()
    {
        var r = new Rig(); r.Sc.Devices.Add(Pad()); r.On.Internet = true; r.On.InfText = InfFor(PadId);
        var run = new FixRunner(r.Sc, r.V, r.Idx, new PnpUtil(r.R), r.On, r.Root, r.Log, null, includeOther: true);
        Assert.Equal(RunOutcome.Fixed, run.Run());
        Assert.Equal(1, r.On.Calls);
        var r2 = new Rig(); r2.Sc.Devices.Add(Pad()); // no pack, no internet
        var run2 = new FixRunner(r2.Sc, r2.V, r2.Idx, new PnpUtil(r2.R), r2.On, r2.Root, r2.Log, null, includeOther: true);
        Assert.Equal(RunOutcome.NotFixed, run2.Run());
    }
}
