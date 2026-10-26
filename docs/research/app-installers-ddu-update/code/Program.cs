using Hateeze.Update;

namespace Hateeze;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--safeboot-guard")) return Hateeze.Ddu.GuardService.Run();
        if (args.Contains("--ddu-stage2")) return Hateeze.Ddu.DduOrchestrator.Stage2();

        if (args.Length > 0 && args[0] == "semver-test")
        {
            string[] order = { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "v2.0.0" };
            for (int i = 0; i + 1 < order.Length; i++)
            {
                var a = SemVer.Parse(order[i]); var b = SemVer.Parse(order[i + 1]);
                Console.WriteLine($"{a} < {b}: {a < b}");
            }
            Console.WriteLine($"1.2.3+build == 1.2.3: {SemVer.Parse("1.2.3+build") == SemVer.Parse("1.2.3")}");
            Console.WriteLine($"TryParse('1.2'): {SemVer.TryParse("1.2", out _)}  TryParse('01.2.3'): {SemVer.TryParse("01.2.3", out _)}");
            return 0;
        }
        if (args.Length > 0 && args[0] == "redirect-test")
        {
            Console.WriteLine("obs latest tag via redirect: " + await GitHubUpdater.GetLatestTagViaRedirectAsync("obsproject", "obs-studio", CancellationToken.None));
            return 0;
        }
        if (args.Length > 0 && args[0] == "ddu-version")
        {
            var v = await Hateeze.Ddu.DduDownloader.GetLatestVersionAsync(CancellationToken.None);
            Console.WriteLine("DDU latest: " + v);
            foreach (var u in Hateeze.Ddu.DduDownloader.PortableUrls(v)) Console.WriteLine("  " + u);
            Console.WriteLine("args: " + Hateeze.Ddu.DduDownloader.BuildArgs(new Hateeze.Ddu.DduOptions(Hateeze.Ddu.GpuVendor.Nvidia)));
            return 0;
        }
        if (args.Length > 0 && args[0] == "readonly-probe")
        {
            Console.WriteLine("BootMode(SM_CLEANBOOT): " + Hateeze.Ddu.SafeBoot.CurrentBootMode);
            Console.WriteLine("Current loader GUID: " + Hateeze.Ddu.SafeBoot.CurrentLoaderGuid());
            Console.WriteLine("safeboot armed: " + Hateeze.Ddu.SafeBoot.IsSafeBootArmed());
            Console.WriteLine("reboot pending: " + Hateeze.Ddu.DduOrchestrator.IsRebootPending());
            Console.WriteLine("shell SID: " + Hateeze.Apps.InteractiveUser.GetShellUserSid() + " elevatedAsOther=" + Hateeze.Apps.InteractiveUser.ElevatedAsDifferentUser());
            var apps = Hateeze.Apps.InstalledAppsDetector.Enumerate();
            Console.WriteLine("uninstall entries: " + apps.Count);
            var discord = Hateeze.Apps.InstalledAppsDetector.Find(apps, new Hateeze.Apps.DetectRule(new[] { "Discord" }, null, null, null, null));
            Console.WriteLine("Discord: " + discord);
            var nv = Hateeze.Apps.InstalledAppsDetector.Find(apps, new Hateeze.Apps.DetectRule(null, @"^\{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8\}_Display\.Driver$", null, null, null));
            Console.WriteLine("NVIDIA driver: " + nv?.DisplayVersion);
            var sig = Hateeze.Security.Authenticode.Verify(@"C:\Program Files (x86)\Steam\steam.exe");
            Console.WriteLine($"steam.exe sig: valid={sig.Valid} code=0x{sig.WinTrustCode:X8} cn={sig.SubjectCn}");
            var sig2 = Hateeze.Security.Authenticode.Verify(@"C:\Program Files\7-Zip\7z.exe");
            Console.WriteLine($"7z.exe sig: valid={sig2.Valid} code=0x{sig2.WinTrustCode:X8} cn={sig2.SubjectCn}");
            dynamic shellApp = Hateeze.Apps.Unelevated.GetDesktopShellApplication();
            Console.WriteLine("Shell.Application obtained via explorer desktop view: " + (shellApp != null));
            return 0;
        }
        Console.WriteLine("usage: semver-test | redirect-test | ddu-version | readonly-probe");
        return 0;
    }
}
